package validate

import (
	"fmt"

	"github.com/ComedicChimera/whirlwind/src/common"
	"github.com/ComedicChimera/whirlwind/src/logging"
	"github.com/ComedicChimera/whirlwind/src/syntax"
	"github.com/ComedicChimera/whirlwind/src/typing"
)

var validTypeSetIntrinsics = map[string]struct{}{
	"Vector":         {},
	"TypedVector":    {},
	"IntegralVector": {},
	"Tuple":          {},
}

// WalkDef walks the AST of any given definition and attempts to determine if it
// can be defined.  If it can, it returns the produced HIR node and the value
// `true`. If it cannot, then it determines first if the only errors are due to
// missing symbols, in which case it returns a map of unknowns and `true`.
// Otherwise, it returns `false`. All unmentioned values for each case will be
// nil. This is the main definition analysis function and accepts a `definition`
// node.  This function is mainly intended to work with the Resolver and
// PackageAssembler.  It also always returns the name of the definition is
// possible.
func (w *Walker) WalkDef(dast *syntax.ASTBranch) (common.HIRNode, string, map[string]*common.UnknownSymbol, bool) {
	def, dt, ok := w.walkDefRaw(dast)

	// make sure w.currentDefName is always cleared (for contexts where there is
	// no name to override it -- eg. interface binding)
	defer (func() {
		w.currentDefName = ""

		// clear self-type data last so that generics can still recognize it
		w.selfType = nil
		w.selfTypeUsed = false
		w.selfTypeRequiresRef = false
	})()

	if ok {
		// apply generic context before returning definition; we can assume that
		// if we reached this point, there are no unknowns to account for
		def, dt = w.applyGenericContext(def, dt)

		// update the shared opaque symbol with the contents of this definition
		// as necessary (not always resolving since we could be dealing with
		// local functions).  Generic and non-generic should always match up
		// because the opaque type is generated based on the definition
		if w.resolving && w.sharedOpaqueSymbol.SrcPackageID == w.SrcPackage.PackageID && w.sharedOpaqueSymbol.Name == w.currentDefName {
			if ot, ok := w.sharedOpaqueSymbol.Type.(*typing.OpaqueType); ok {
				ot.EvalType = dt
			} else if ogt, ok := w.sharedOpaqueSymbol.Type.(*typing.OpaqueGenericType); ok {
				// none of the errors caused by this should effect the
				// generation of this definition -- don't cause unnecessary
				// errors (even in recursive case error is already logged)
				ogt.Evaluate(dt.(*typing.GenericType), w.solver)
			}
		}

		return def, w.currentDefName, nil, true
	}

	// clear the generic context
	w.genericCtx = nil

	// handle fatal definition errors
	if w.fatalDefError {
		w.fatalDefError = false

		return nil, "", nil, false
	}

	// collect and clear our unknowns after they have collected for the definition
	unknowns := w.unknowns
	w.clearUnknowns()

	return nil, w.currentDefName, unknowns, true
}

// walkDefRaw walks a definition without handling any generics or any of the
// clean up. If this function succeeds, it returns the node generated and the
// internal "type" of the node for use in generics as necessary.  It effectively
// acts a wrapper to all of the individual `walk` functions for the various
// kinds of definitions.
func (w *Walker) walkDefRaw(dast *syntax.ASTBranch) (common.HIRNode, typing.DataType, bool) {
	switch dast.Name {
	case "type_def":
		if tdnode, ok := w.walkTypeDef(dast); ok {
			return tdnode, tdnode.Sym.Type, true
		}
	}

	return nil, nil, false
}

// walkTypeDef walks a `type_def` node and returns the appropriate HIRNode and a
// boolean indicating if walking was successful.  It does not indicate if an
// errors were fatal.  This should be checked using the `FatalDefError` flag.
func (w *Walker) walkTypeDef(dast *syntax.ASTBranch) (*common.HIRTypeDef, bool) {
	closedType := false
	var name string
	var namePosition *logging.TextPosition
	var dt typing.DataType
	fieldInits := make(map[string]common.HIRNode)

	for _, item := range dast.Content {
		switch v := item.(type) {
		case *syntax.ASTBranch:
			switch v.Name {
			case "generic_tag":
				if !w.primeGenericContext(v) {
					return nil, false
				}
			case "newtype":
				if w.hasFlag("intrinsic") {
					w.logInvalidIntrinsic(name, "defined type", namePosition)
					return nil, false
				}

				// indents and dedents are removed by parser so suffix will
				// always be first element
				suffixNode := v.BranchAt(0)
				if suffixNode.Name == "alg_suffix" {
					if adt, ok := w.walkAlgebraicSuffix(suffixNode, name, namePosition); ok {
						dt = adt
					} else {
						return nil, false
					}
				} else {
					if sdt, ok := w.walkStructSuffix(suffixNode, name, fieldInits); ok {
						dt = sdt
					} else {
						return nil, false
					}
				}

			case "typeset":
				// NOTE: typesets don't define a self-type since such a
				// definition would have literally no meaning (especially if it
				// only contained one type -- itself)
				if types, ok := w.walkOffsetTypeList(v, 1, 0); ok {
					intrinsic := w.hasFlag("intrinsic")

					if intrinsic {
						if _, ok := validTypeSetIntrinsics[name]; !ok {
							w.logInvalidIntrinsic(name, "type set", namePosition)
							return nil, false
						}
					}

					dt = &typing.TypeSet{
						Name:         name,
						SrcPackageID: w.SrcPackage.PackageID,
						Types:        types,
						Intrinsic:    intrinsic,
					}
				} else {
					return nil, false
				}
			}
		case *syntax.ASTLeaf:
			switch v.Kind {
			case syntax.CLOSED:
				closedType = true
			case syntax.IDENTIFIER:
				name = v.Value
				w.currentDefName = name
				namePosition = v.Position()
			}
		}
	}

	symbol := &common.Symbol{
		Name:       name,
		Type:       dt,
		DefKind:    common.DefKindTypeDef,
		DeclStatus: w.DeclStatus,
		Constant:   true,
	}

	if !w.define(symbol) {
		w.logRepeatDef(name, namePosition, true)
		return nil, false
	}

	// only declare algebraic variants if the definition of the core algebraic
	// type succeeded (that way we don't have random definitions floating around
	// without a parent)
	if at, ok := dt.(*typing.AlgebraicType); ok {
		at.Closed = closedType

		if !closedType {
			for iname, vari := range at.Variants {
				symbol := &common.Symbol{
					Name:       iname,
					Type:       vari,
					Constant:   true,
					DefKind:    common.DefKindTypeDef,
					DeclStatus: w.DeclStatus,
				}

				if !w.define(symbol) {
					w.logFatalDefError(
						fmt.Sprintf("Algebraic type `%s` must be marked `closed` as its variant `%s` shares a name with an already-defined symbol", name, iname),
						logging.LMKName,
						namePosition,
					)
					return nil, false
				}
			}
		}
	} else if closedType {
		// you can't use `closed` on a type that isn't algebraic
		w.logFatalDefError(
			fmt.Sprintf("`closed` property not applicable on type `%s`", dt.Repr()),
			logging.LMKUsage,
			dast.Content[0].Position(),
		)
		return nil, false
	}

	return &common.HIRTypeDef{
		Sym:        symbol,
		FieldInits: fieldInits,
	}, true
}

// walkAlgebraicSuffix walks an `alg_suffix` node in a type definition
func (w *Walker) walkAlgebraicSuffix(suffix *syntax.ASTBranch, name string, namePosition *logging.TextPosition) (typing.DataType, bool) {
	algType := &typing.AlgebraicType{
		Name:         name,
		SrcPackageID: w.SrcPackage.PackageID,
		Variants:     make(map[string]*typing.AlgebraicVariant),
	}

	// set the selfType field
	w.setSelfType(algType)

	for _, item := range suffix.Content {
		algVariBranch := item.(*syntax.ASTBranch)

		algVariant := &typing.AlgebraicVariant{
			Parent: algType,
			Name:   algVariBranch.LeafAt(1).Value,
		}

		if algVariBranch.Len() == 3 {
			if values, ok := w.walkOffsetTypeList(algVariBranch.BranchAt(2), 1, 1); ok {
				algVariant.Values = values
			} else {
				return nil, false
			}
		}

		algType.Variants[algVariant.Name] = algVariant
	}

	// if the selfType is used in the only variant then the type is recursively
	// defined and has no alternate form that prevents such recursion (ie. no
	// "base case") and so we must throw an error.
	if w.selfTypeUsed && len(algType.Variants) == 1 {
		w.logFatalDefError(
			fmt.Sprintf("Algebraic type `%s` defined recursively with no base case", name),
			logging.LMKUsage,
			namePosition,
		)
		return nil, false
	}

	return algType, true
}

// walkStructSuffix walks a `struct_suffix` node in a type definition
func (w *Walker) walkStructSuffix(suffix *syntax.ASTBranch, name string, fieldInits map[string]common.HIRNode) (typing.DataType, bool) {
	structType := &typing.StructType{
		Name:         name,
		SrcPackageID: w.SrcPackage.PackageID,
		Packed:       w.hasFlag("packed"),
		Fields:       make(map[string]*typing.TypeValue),
	}

	// set the selfType field and appropriate flag
	w.setSelfType(structType)
	w.selfTypeRequiresRef = true

	for _, item := range suffix.Content {
		if branch, ok := item.(*syntax.ASTBranch); ok {
			if branch.Name == "struct_member" {
				if fnames, tv, init, ok := w.walkTypeValues(branch); ok {
					// multiple fields can share the same type value and
					// initializer (for efficiency)
					for fname, pos := range fnames {
						if _, ok := structType.Fields[fname]; ok {
							w.logFatalDefError(
								fmt.Sprintf("Field `%s` of struct `%s` defined multiple times", fname, name),
								logging.LMKName,
								pos,
							)
							return nil, false
						}

						structType.Fields[fname] = tv

						if init != nil {
							fieldInits[fname] = init
						}
					}
				} else {
					return nil, false
				}
			} else /* inherit */ {
				if dt, ok := w.walkTypeLabel(branch); ok {
					if st, ok := dt.(*typing.StructType); ok {
						// inherits cannot be self-referential
						if typing.Equals(st, structType) {
							w.logFatalDefError(
								fmt.Sprintf("Struct `%s` cannot inherit from itself", name),
								logging.LMKUsage,
								branch.Position(),
							)
							return nil, false
						}

						structType.Inherit = st
					} else {
						// structs can only inherit from other structs
						w.logFatalDefError(
							fmt.Sprintf("Struct `%s` must inherit from another struct not `%s`", name, dt.Repr()),
							logging.LMKUsage,
							branch.Position(),
						)
						return nil, false
					}
				} else {
					return nil, false
				}
			}
		}
	}

	return structType, true
}

// walkTypeValues walks any node that is of the form of a type value (ie.
// `identifier_list` followed by `type_ext`) and generates a single common type
// value and a map of names and positions from it.  It also handles `vol` and
// `const` modifiers and returns any initializers it finds.
func (w *Walker) walkTypeValues(branch *syntax.ASTBranch) (map[string]*logging.TextPosition, *typing.TypeValue, common.HIRNode, bool) {
	ctv := &typing.TypeValue{}
	var names map[string]*logging.TextPosition
	var initializer common.HIRNode

	for _, item := range branch.Content {
		switch v := item.(type) {
		case *syntax.ASTBranch:
			switch v.Name {
			case "identifier_list":
				names = w.walkIdList(v)
			case "type_ext":
				if dt, ok := w.walkTypeExt(v); ok {
					ctv.Type = dt
				} else {
					return nil, nil, nil, false
				}
			case "initializer":
				initializer = (*common.HIRIncomplete)(v.BranchAt(1))
			}
		case *syntax.ASTLeaf:
			switch v.Kind {
			case syntax.VOL:
				ctv.Volatile = true
			case syntax.CONST:
				ctv.Constant = true
			}
		}
	}

	return names, ctv, initializer, true
}
