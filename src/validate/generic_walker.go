package validate

import (
	"fmt"

	"whirlwind/common"
	"whirlwind/logging"
	"whirlwind/syntax"
	"whirlwind/typing"
)

// primeGenericContext should be called whenever a `generic_tag` node is
// encountered in a definition.  It parses the tag and appropriately populates
// the `TypeParams` slice with all of the encounter type parameters.  It returns
// `true` if the parsing was successful.
func (w *Walker) primeGenericContext(genericTag *syntax.ASTBranch, isInterf bool) bool {
	wc := make([]*typing.WildcardType, genericTag.Len()/2)
	names := make(map[string]struct{})

	for i, item := range genericTag.Content[1 : genericTag.Len()-1] {
		if i%2 == 1 {
			continue
		}

		param := item.(*syntax.ASTBranch)

		name := param.LeafAt(0).Value
		if _, ok := names[name]; !ok {
			names[name] = struct{}{}
		} else {
			w.logError(
				fmt.Sprintf("Multiple type parameters declared with name `%s`", name),
				logging.LMKName,
				param.Content[0].Position(),
			)

			return false
		}

		if param.Len() == 1 {
			wc[i/2] = &typing.WildcardType{Name: name}
		} else if typeList, ok := w.walkGenericTypeConstraint(param); ok {
			wc[i/2] = &typing.WildcardType{
				Name:        name,
				Constraints: typeList,
			}
		} else {
			return false
		}
	}

	if isInterf {
		w.interfGenericCtx = wc
	} else {
		w.genericCtx = wc
	}

	return true
}

// createGenericInstance creates a new instance of the given generic type or
// generic opaque type.  It will log an error if the type passed in is not
// generic.
func (w *Walker) createGenericInstance(generic typing.DataType, genericPos *logging.TextPosition, paramsBranch *syntax.ASTBranch) (typing.DataType, bool) {
	params, ok := w.walkTypeList(paramsBranch)

	if !ok {
		return nil, false
	}

	switch v := generic.(type) {
	case *typing.GenericType:
		if gi, ok := w.solver.CreateGenericInstance(v, params, paramsBranch); ok {
			return gi, ok
		} else {
			return nil, ok
		}
	case *typing.OpaqueGenericType:
		if v.EvalType == nil {
			ogi := &typing.OpaqueGenericInstanceType{
				OpaqueGeneric:    v,
				TypeParams:       params,
				TypeParamsBranch: paramsBranch,
			}

			v.Instances = append(v.Instances, ogi)

			return ogi, true
		}

		if gi, ok := w.solver.CreateGenericInstance(v.EvalType, params, paramsBranch); ok {
			return gi, ok
		} else {
			return nil, ok
		}
		// TODO: Wildcard generics
	}

	// not a generic type -- error
	w.logError(
		fmt.Sprintf("Unable to pass type parameters to non-generic type `%s`", generic.Repr()),
		logging.LMKTyping,
		genericPos,
	)

	return nil, false
}

// setSelfType sets the selfType field accounting for generic context
func (w *Walker) setSelfType(st typing.DataType) {
	// handle both kinds of generic context where determining whether or not to
	// wrap out self-type in a generic.  Since only type definitions and
	// interfaces used this field, `interfGenericCtx` will never interfere with
	// regular type generation since interfaces cannot contain type definitions
	// nor can they contain other interfaces.  So always checking
	// `interfGenericCtx` is not a problem
	if w.genericCtx != nil {
		w.selfType = &typing.GenericType{
			TypeParams: w.genericCtx,
			Template:   st,
		}
	} else if w.interfGenericCtx != nil {
		w.selfType = &typing.GenericType{
			TypeParams: w.interfGenericCtx,
			Template:   st,
		}
	} else {
		// if there is no generic context, then we can just assign as is
		w.selfType = st
	}
}

// applyGenericContext should be called at the end of every definition.  This
// function checks if a generic context exists (in `TypeParams`).  If so,
// returns the appropriately constructed `HIRGeneric` and clears the generic
// context for the next definition.  If there is no context, it simply returns
// the definition passed in.
func (w *Walker) applyGenericContext(node common.HIRNode, dt typing.DataType) (common.HIRNode, typing.DataType) {
	if w.genericCtx == nil {
		return node, dt
	}

	// if there is a selfType, then we know that selfType stores a pre-built
	// GenericType type used for selfType referencing (so we don't need to
	// create a new one at all)
	var gt *typing.GenericType
	if w.selfType != nil {
		gt = w.selfType.(*typing.GenericType)
	} else {
		gt = &typing.GenericType{
			TypeParams: w.genericCtx,
			Template:   dt,
		}
	}

	// wrap our generic into a HIRGeneric
	gen := &common.HIRGeneric{
		Generic:     gt,
		GenericNode: node,
	}

	// find the symbol of the declared data type so its type can be overwritten
	// with the generic type (should always succeed b/c this is called
	// immediately after a definition)
	symbol, _ := w.globalLookup(w.currentDefName)
	symbol.Type = gt

	// if there is a definition node stored, we need to update it
	if symbol.DefNode != nil {
		symbol.DefNode = gen
	}

	// update algebraic variants of open generic algebraic types
	if at, ok := dt.(*typing.AlgebraicType); ok {
		if !at.Closed {
			for i, vari := range at.Variants {
				// we know the variant exists -- we can just look it up
				vs, _ := w.globalLookup(vari.Name)

				vs.Type = &typing.GenericAlgebraicVariantType{
					GenericParent: gt,
					VariantPos:    i,
				}
			}
		}
	}

	w.genericCtx = nil
	return gen, symbol.Type
}

// applyGenericContextToSpecial applies the generic context to a specialization
// to create a parametric specialization as necessary
func (w *Walker) applyGenericContextToSpecial(gt *typing.GenericType, genericSpecial *typing.GenericSpecialization, body common.HIRNode) common.HIRNode {
	if w.genericCtx == nil {
		return &common.HIRSpecialDef{
			RootGeneric: gt,
			TypeParams:  genericSpecial.MatchingTypes,
			Body:        body,
		}
	} else {
		// clear the generic context since we no longer need it as a flag
		w.genericCtx = nil

		// set up the shared slice that both the typing.GenericSpecialization
		// and the common.HIRParametricSpecialDef will share.  It can just
		// be `nil` for now.
		var parametricInstanceSlice [][]typing.DataType

		genericSpecial.ParametricInstances = &parametricInstanceSlice
		return &common.HIRParametricSpecialDef{
			RootGeneric:         gt,
			TypeParams:          genericSpecial.MatchingTypes,
			ParametricInstances: &parametricInstanceSlice,
			Body:                body,
		}
	}
}

// applyGenericContextToOpDef applies the generic context specifically to an
// operator definition (as opposed to a more general definition).  It returns
// the generic node as well as the true (wrapped) signature
func (w *Walker) applyGenericContextToOpDef(opdef *common.HIROperDef) (common.HIRNode, typing.DataType) {
	if w.genericCtx != nil {
		gt := &typing.GenericType{
			TypeParams: w.genericCtx,
			Template:   opdef.Signature,
		}

		w.genericCtx = nil
		return &common.HIRGeneric{
			Generic:     gt,
			GenericNode: opdef,
		}, gt
	}

	return opdef, opdef.Signature
}

// applyGenericContextToMethod applies the generic context to a method and
// returns the newly computed values (as necessary - basically a
// reimplementation of `applyGenericContext` for methods of an interface;
// working without a symbol)
func (w *Walker) applyGenericContextToMethod(dt typing.DataType, node common.HIRNode) (typing.DataType, common.HIRNode) {
	if w.genericCtx != nil {
		// selfType is never valid a generic here
		gt := &typing.GenericType{
			TypeParams: w.genericCtx,
			Template:   dt,
		}

		dt = gt

		node = &common.HIRGeneric{
			Generic:     gt,
			GenericNode: node,
		}

		w.genericCtx = nil
	}

	return dt, node
}
