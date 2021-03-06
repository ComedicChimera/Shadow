package typing

import (
	"strings"

	"whirlwind/logging"
	"whirlwind/syntax"
)

// OpaqueType is used to represent a type that has yet to be defined but is
// needed to define other types.  It helps to facilitate mutual/cyclic
// dependency resolution between individual symbols
type OpaqueType struct {
	// Name of the opaque type created
	Name string

	// EvalType stores the data type that is the evaluated/determined type for
	// this opaque type in much the same way that a WildcardType stores in an
	// internal type to be evaluated later
	EvalType DataType
}

// All of OpaqueType's methods treat it as if it is it's evaluated type if such
// a type exists.  The behavior in the case where it isn't is written above the
// method.

// equals always returns false since it is only called if no inner type exists.
func (op *OpaqueType) equals(other DataType) bool {
	return false
}

// Repr returns the name of opaque type that is given if the EvalType is unknown
func (op *OpaqueType) Repr() string {
	if op.EvalType != nil {
		return op.EvalType.Repr()
	}

	return op.Name
}

// copyTemplate duplicates the EvalType if it can and acts as an identity
// function if it doesn't (returns the OpaqueType reference)
func (op *OpaqueType) copyTemplate() DataType {
	if op.EvalType != nil {
		return op.EvalType.copyTemplate()
	}

	// don't copy so that we can avoid losing the shared data type reference
	return op
}

// OpaqueGenericType is a special variant of the standard opaque type that works
// for generics.  It effectively creates links to all of the generate types so
// that any errors that occurred when creating the generate that were not
// detected because the generic did not yet exist can be rectified.  Unlike
// OpaqueType, this type is not meant to linger in the type system -- its is
// only used to facilitate opaque behavior and detect when an opaque generate is
// necessary.
type OpaqueGenericType struct {
	// EvalType works the same as it does in `OpaqueType` except it is
	// explicitly generic
	EvalType *GenericType

	// Instances stores all of the generic instances that were created based on
	// this opaque type
	Instances []*OpaqueGenericInstanceType
}

// equals for an opaque generic type always returns false since it is only
// called if an inner type does not exist.
func (og *OpaqueGenericType) equals(other DataType) bool {
	return false
}

// Repr simply calls the repr of the inner generic if such a generic exists.
// Otherwise, it returns a special string indicating that the inner generic is
// indeterminate.
func (og *OpaqueGenericType) Repr() string {
	if og.EvalType == nil {
		return "<opaque generic>"
	}

	return og.EvalType.Repr()
}

// This method should never be used since the only time generics are copied
// directly is in interfaces (as method) which do not experience opaque symbol
// resolution.  If this method is called, a fatal error will occur.
func (og *OpaqueGenericType) copyTemplate() DataType {
	// COMMENT FOR PREVIOUS IMPL:
	// copyTemplate attempts to copy the inner generic if it exists; otherwise,
	// it simply inserts a nil.  All the instances are also copied *and* their
	// OpaqueGeneric references overwritten to point to the copy.  DependsOn and
	// RequiresRef are not.  NOTE: I have no clue when this method would be
	// used. copy := &OpaqueGenericType{}

	// if og.EvalType != nil {
	// 	copy.EvalType = og.EvalType.copyTemplate().(*GenericType)
	// }

	// copy.Instances = make([]*OpaqueGenericInstanceType, len(og.Instances))
	// for i, inst := range og.Instances {
	// 	instCopy := inst.copyTemplate().(*OpaqueGenericInstanceType)
	// 	instCopy.OpaqueGeneric = copy
	// 	copy.Instances[i] = instCopy
	// }

	// return copy
	logging.LogFatal("`copyTemplate` called on `OpaqueGeneric`")
	return nil
}

// Evaluate takes in a type to act as the evaluated generic, updates the opaque
// type, checks all of the instances, and logs an appropriate error if something
// goes wrong
func (ogt *OpaqueGenericType) Evaluate(gt *GenericType, s *Solver) bool {
	ogt.EvalType = gt

	instancesMatched := true
	for _, instance := range ogt.Instances {
		if generate, ok := s.CreateGenericInstance(gt, instance.TypeParams, instance.TypeParamsBranch); ok {
			instance.MemoizedGenerate = generate
		} else {
			instancesMatched = false
		}

	}

	return instancesMatched
}

// OpaqueGenericInstanceType is used as a temporary placeholder for a generate
// that is created based off of a generic opaque type.  These types can linger
// in the type system but are innately memoized to prevent generating
// unnecessary generics over and over.
type OpaqueGenericInstanceType struct {
	// OpaqueGeneric stores a reference to the opaque generic that this generate
	// spawns from
	OpaqueGeneric *OpaqueGenericType

	// TypeParams is the slice of type parameters passed in to create this generate
	TypeParams []DataType

	// MemoizedGenerate stores the generate type that this generic instance
	// corresponds to (after type parameter substitution).  It is generated once
	// and is, in effect, the type that this generic instance refers to.
	MemoizedGenerate DataType

	// TypeParamsBranch stores the `type_list` branch that holds all of the type
	// parameter values used for this instance.  It is used in error handling.
	TypeParamsBranch *syntax.ASTBranch
}

// equals will always return false since if this method is called directly:
// no inner type exists and no generate can be returned.
func (ogi *OpaqueGenericInstanceType) equals(other DataType) bool {
	return false
}

// Repr simply acts like repr for a generic instance type.  However, if the the
// MemoizedGenerate has not been determined yet, it returns a special repr
// indicating as such.
func (ogi *OpaqueGenericInstanceType) Repr() string {
	if ogi.MemoizedGenerate == nil {
		return "<opaque generate>"
	}

	// same repr as is used for `GenericInstanceType`
	sb := strings.Builder{}
	sb.WriteString(ogi.OpaqueGeneric.EvalType.Template.Repr())

	sb.WriteRune('<')
	for i, tp := range ogi.TypeParams {
		sb.WriteString(tp.Repr())

		if i < len(ogi.TypeParams)-1 {
			sb.WriteString(", ")
		}
	}
	sb.WriteRune('>')

	return sb.String()
}

// copyTemplate preserve the internal opaque generic reference while copying the
// type parameters and memoized generic.  In addition, it will add itself to the
// original opaque generic instance
func (ogi *OpaqueGenericInstanceType) copyTemplate() DataType {
	copy := &OpaqueGenericInstanceType{
		OpaqueGeneric:    ogi.OpaqueGeneric,
		TypeParams:       copyTemplateSlice(ogi.TypeParams),
		MemoizedGenerate: ogi.MemoizedGenerate, // this field may be `nil`
		TypeParamsBranch: ogi.TypeParamsBranch,
	}

	ogi.OpaqueGeneric.Instances = append(ogi.OpaqueGeneric.Instances, copy)
	return copy
}
