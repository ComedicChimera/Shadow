package typing

// This file describes the conversions between different types (coercions and
// casts) and contains the implementation of CoerceTo and CastTo for the Solver.

// Rule of Coercion:
// The rule of coercion specifies that a coercion should be legal between two
// things of (relatively) equivalent value.  That means no coercion should
// result in a significant change in the meaning of the value of a type or in
// that value itself.  Thus, `rune` to `string` is a valid coercion since , the
// text value of the `rune` can be preserved across that coercion.  However,
// something like `int` to `uint` could change the numeric value of the original
// data significantly, and something like `bool` to `int` dramatically changes
// the meaning of the data stored in the boolean value. Thus, those two
// operations should be casts (requiring explicit denotation) and not coercions.

// CoerceTo implements coercion checking for the solver.  It checks if two types
// are equal or can be made equal through implicit casting.  This function does
// NOT work for unknown types.
func (s *Solver) CoerceTo(src, dest DataType) bool {
	if Equals(src, dest) {
		return true
	}

	src, dest = InnerType(src), InnerType(dest)

	// we need to check for coercions from Wildcard types since they aren't
	// handled below
	if swt, ok := src.(*WildcardType); ok {
		if swt.Value == nil {
			for _, r := range swt.Constraints {
				if s.CoerceTo(r, dest) {
					return true
				}
			}
		} else {
			return s.CoerceTo(swt.Value, dest)
		}
	}

	switch dv := dest.(type) {
	case *PrimitiveType:
		// check for any (since all types can coerce to it)
		if dv.PrimKind == PrimKindUnit && dv.PrimSpec == 1 {
			return true
		}

		if spt, ok := src.(*PrimitiveType); ok {
			// floating to floating
			if dv.PrimKind == PrimKindFloating {
				return spt.PrimKind == PrimKindFloating && spt.PrimSpec < dv.PrimSpec
			} else if dv.PrimKind == PrimKindIntegral {
				// integral to integral
				if spt.PrimKind == PrimKindIntegral {
					// in order for the coercion to succeed, the two integrals must
					// both have the same signedness (that is both unsigned or both
					// signed) which since the PrimSpecs for integrals alternate
					// between signed and unsigned means they must be equal (mod 2)
					// and since coercion only applied upward (that is short to int
					// but not int to short), the current (this) PrimSpec must be
					// greater than the other PrimSpec
					return dv.PrimSpec%2 == spt.PrimSpec%2 && dv.PrimSpec > spt.PrimSpec
				}
			} else if dv.PrimKind == PrimKindText {
				// rune to string
				return dv.PrimSpec == 1 && spt.PrimSpec == 0
			}
		}
	case TupleType:
		if stt, ok := src.(TupleType); ok {
			for i, item := range dv {
				if !s.CoerceTo(stt[i], item) {
					return false
				}
			}

			return true
		}
	case *VectorType:
		if svt, ok := src.(*VectorType); ok {
			return s.CoerceTo(svt.ElemType, dv.ElemType) && dv.Size == svt.Size
		}
	case *RefType:
		// The coercion possible on references is non-const to const.  This has
		// to do with the fact that &int and &uint although similar at the
		// surface mean very different things: there is no inherent relation
		// between the memory the point to.  A reference's identity is based on
		// what it points to and its value. Thus, such a coercion would not only
		// mean a duplication of the reference itself but also the memory it
		// points to -- since such duplication could mean a wide variety of
		// different things it is better to simply not allow such coercions.
		// Constancy coercion only applies to match regular constancy rules
		// (where a variable can "coerce" to a constant).
		if srt, ok := src.(*RefType); ok {
			return Equals(dv.ElemType, srt.ElemType) && dv.Constant && !srt.Constant
		}
	case *InterfType:
		// Any type that implicitly or explicitly implements an interface can be
		// coerced to it (duck typing)
		return s.ImplementsInterf(src, dv)
	case *ConstraintType:
		// Although constraints aren't used as physical types, they do still
		// implement coercion for operations like type inference and validation
		// of passed in generic parameters.

		// Any intrinsic constraints require special coercion checking; eg.
		// `Vector` is modified to be coercible to from all vectors
		if dv.Intrinsic {
			switch dv.Name {
			case "Vector":
				_, ok := src.(*VectorType)
				return ok
			case "Tuple":
				_, ok := src.(TupleType)
				return ok
			case "TypedVector":
				// Since TypedVector must always be used as its generic
				// instance, the only type it stores is by definition is type
				// parameter. Thus, when doing this comparison, we simply check
				// that the vectors element type is equal to that type parameter
				// to test if the coercion succeeds.
				if svt, ok := src.(*VectorType); ok {
					return Equals(svt.ElemType, dv.Types[0])
				}
			case "IntegralVector":
				if svt, ok := src.(*VectorType); ok {
					return s.CoerceTo(svt.ElemType, &PrimitiveType{
						PrimKind: PrimKindIntegral,
						PrimSpec: PrimIntI64,
					}) || s.CoerceTo(svt.ElemType, &PrimitiveType{
						PrimKind: PrimKindIntegral,
						PrimSpec: PrimIntU64,
					})
				}
			}
		} else if scons, ok := src.(*ConstraintType); ok {
			// if we coercing one constraint to another, then we need to make
			// sure that every possible type in the source constraint is
			// coercible to the destination constraint -- we can check this by
			// simply coercing each individual element to the destination
			// constraint
			for _, item := range scons.Types {
				if !s.CoerceTo(item, dv) {
					return false
				}
			}

			// if we reach here, all coerced fine
			return true
		} else {
			// if the source is not itself a type constraint, then we can simply
			// say that coercion is possible if source is coercible to any
			// element of the constraint.  Note that constraints cannot, by
			// definition contain other constraints
			for _, item := range dv.Types {
				if s.CoerceTo(src, item) {
					return true
				}
			}
		}
	case *WildcardType:
		// the only WildcardTypes that reach here have failed the equals check
		// meaning they either have a value that coercion should be checked on
		// or the check didn't pass the constraints on exact equality (so we
		// have to first check value and then check constraints)
		if dv.Value == nil {
			for _, cons := range dv.Constraints {
				if s.CoerceTo(src, cons) {
					return true
				}
			}
		} else {
			return s.CoerceTo(src, dv.Value)
		}
	}

	// Note on Struct Coercion:
	// ------------------------
	// Structs cannot be coerced mainly because is structure is unique both in its
	// name and package ID -- where it is declared and what is represents are
	// fundamental aspects of its meaning.  Moreover, if two structs have
	// differently named fields then coercion and casting makes no sense since there
	// is no correspondence between the fields.

	// all other types don't define any form of coercion.  See the note at the
	// end of the `CastTo` function for some more information on why certain
	// types don't define coercion or casting
	return false
}

// CastTo implements the explicit checking.  This function ONLY checks for
// explicit casts.  Thus, to fulfill the full function of a type cast CoerceTo
// should be called first (along with any necessary inferencing mechanisms).
// NOTE: This function will panic if an `UnknownType` is passed to it
func (s *Solver) CastTo(src, dest DataType) bool {
	src, dest = InnerType(src), InnerType(dest)

	// we need to check for casts to WildcardTypes since such casts will not be
	// properly handled below (same problem occurs at top of CoerceTo)
	if dwt, ok := dest.(*WildcardType); ok {
		if dwt.Value == nil {
			for _, cons := range dwt.Constraints {
				if s.CastTo(src, cons) {
					return true
				}
			}
		} else {
			return s.CastTo(src, dwt.Value)
		}
	}

	switch sv := src.(type) {
	case *PrimitiveType:
		// any to some other type always succeeds (naively here)
		if sv.PrimKind == PrimKindUnit && sv.PrimSpec == 1 {
			return true
		}

		// primitives (outside of any) can only be cast to other primitives
		if dpt, ok := dest.(*PrimitiveType); ok {
			// all numeric types can be cast between each other
			if sv.Numeric() && dpt.Numeric() {
				return true
			}

			// bool to integral
			return sv.PrimKind == PrimKindBoolean && dpt.PrimKind == PrimKindIntegral
		}
	case TupleType:
		if dtt, ok := dest.(TupleType); ok {
			for i, item := range sv {
				if !s.CastTo(item, dtt[i]) {
					return false
				}
			}

			return true
		}
	case *VectorType:
		if dvt, ok := dest.(*VectorType); ok {
			return s.CastTo(sv.ElemType, dvt.ElemType) && sv.Size == dvt.Size
		}
	case *StructType:
		// Structs can be cast.  However, they can only be cast if they have
		// identical fields since there is some relation between the data the
		// structs store (this also allows for structs with the same name and
		// fields in different packages to be cast between in each other).
		// Consider the example of the `Vec2` and `Point2D` structs.  They have
		// the same fields (`x` and `y`) and types `int`. Although they are
		// different structs representing different things, a cast still makes
		// sense since it is rational to reinterpret the data of `Point2D` to
		// actually represent the components of a `Vec2`.  By contrast, even if
		// two structs if the same field names, if the fields are different in
		// any way (even two coercible types), then the cast will fail since the
		// two fields may have very different meanings.
		if dst, ok := dest.(*StructType); ok {
			// Packing changes the representation of the data so it should be
			// semantic illogical to cast between a packed and unpacked struct.
			if sv.Packed != dst.Packed {
				return false
			}

			if len(sv.Fields) != len(dst.Fields) {
				return false
			}

			for name, field := range sv.Fields {
				if dfield, ok := dst.Fields[name]; !ok || !field.Equals(dfield) {
					return false
				}
			}

			// in order for two structs to have the same fields they must have
			// the same inherit
			return Equals(sv.Inherit, dst.Inherit)
		}
	case *InterfType:
		// It is possible to cast from one interface to another as long as the
		// two interfaces don't have conflicting method definitions -- this type
		// of casting can fail a runtime
		if dit, ok := dest.(*InterfType); ok {
			for name, method := range sv.Methods {
				if dmethod, ok := dit.Methods[name]; ok {
					if !Equals(method.Signature, dmethod.Signature) {
						return false
					}
				}
			}

			// we can't check anymore thoroughly here -- we don't know the
			// internal type of whatever interface value we are casting
			return true
		}

		// Interfaces can be cast to any type that implements them (naively)
		return s.ImplementsInterf(dest, sv)
	case *AlgebraicType:
		// Algebraic types can be cast following the same logic as structs: if
		// they have the same variants, they can be "reinterpreted" to
		// equivalent
		if dat, ok := dest.(*AlgebraicType); ok {
			if len(sv.Variants) != len(dat.Variants) {
				return false
			}

			for i, variant := range sv.Variants {
				if !Equals(variant, dat.Variants[i]) {
					return false
				}
			}

			return sv.Closed == dat.Closed
		}
	case *ConstraintType:
		// Type sets can be cast to anything that could be one of their
		// elements. Intrinsic type sets do not define this reverse coercion
		// operation since they are ONLY intended to be used as generic
		// constraints -- this is enforced on instance
		return ContainsType(dest, sv.Types)
	case *WildcardType:
		// same logic that was used for coercion applies when value is not nil,
		// but when value is nil because `x as WildcardType` was checked at the
		// top of this function, we are going backwards (`WildcardType as x`).
		// The reverse logic is also mostly true here so this should be fine.
		if sv.Value == nil {
			for _, cons := range sv.Constraints {
				if s.CastTo(cons, dest) {
					return true
				}
			}
		} else {
			return s.CastTo(sv.Value, dest)
		}
	}

	// Notes on Some "Non-Castable/Non-Coercible" Types
	// ------------------------------------------------
	// 1. References:
	// Converting a reference to an integer value is an unsafe operation (that
	// must be performed using an intrinsic stored in `unsafe`).  All other
	// casts are invalid because either they violate the memory model or
	// fundamentally reinterpret the memory the reference points to which is
	// also considered unsafe.
	// 2. Functions:
	// Functions cannot be coerced or cast because doing so would require
	// creating an implicit wrapper around the original function that casts the
	// arguments on every call -- you can't "cast" functions in LLVM, the
	// operation makes no sense.
	// 3. Regions:
	// Since all regions are inherently equal, not other coercions or casts are
	// necessary (since equality must be checked separately from coercion).

	// no other types implement any additional casting logic
	return false
}
