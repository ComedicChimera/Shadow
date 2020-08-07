package analysis

import (
	"github.com/ComedicChimera/whirlwind/src/common"
	"github.com/ComedicChimera/whirlwind/src/syntax"
	"github.com/ComedicChimera/whirlwind/src/types"
)

// Walker is used to walk down a file AST, perform semantic analysis and
// checking, and convert it into a HIR tree.  The Walker first walks through the
// top level of a file and then walks down the predicates.  NOTE: All imports
// must be resolved before the Walker begins walking the HIR tree.
type Walker struct {
	Builder *PackageBuilder
	File    *common.WhirlFile

	// Root represents the root of the currently parsed file
	Root *common.HIRRoot

	// Scopes is used to represent the local, enclosing scopes of functions and
	// blocks.  It is not preserved b/c the back-end doesn't actually need the
	// local scopes to generate the output code.
	Scopes []*Scope

	// DeclStatus is used to indicate how a symbol should be declared (exported
	// or internal)
	DeclStatus int

	// CtxAnnotations are the visible global and local annotations in any location
	CtxAnnotations map[string]string

	// GenericContextStack stores the visible sets of TypeParams (generic
	// contexts) in a stack to allow for layering
	GenericContextStack []map[string]*types.TypeParam
}

// NewWalker creates a new walker for a file in a given package
func NewWalker(pb *PackageBuilder, file *common.WhirlFile) *Walker {
	return &Walker{Builder: pb, File: file, Root: &common.HIRRoot{}}
}

// WalkFile begins walking a file from the top level
func (w *Walker) WalkFile() bool {
	w.DeclStatus = common.DSInternal

	// we don't do any error-suppressing in this function as if a definition
	// doesn't pass, it cause a slough of errors in other definitions that
	// aren't actually useful to the end user and just serve to clutter up
	// compiler output.
	for _, item := range w.File.AST.Content {
		// all top-level items are ASTBranches
		branch := item.(*syntax.ASTBranch)

		// we should already have collected imports at this point so we don't
		// need to check for them here.
		switch branch.Name {
		case "export_block":
			w.DeclStatus = common.DSExported

			// the `top_level` node is always the third node in `export_block`
			if !w.walkDefinitions(branch.Content[2].(*syntax.ASTBranch)) {
				return false
			}

			w.DeclStatus = common.DSInternal
		case "top_level":
			if !w.walkDefinitions(branch) {
				return false
			}
		}
	}

	return true
}

// WalkPredicates walks all of the unevaluated predicates
func (w *Walker) WalkPredicates() bool {
	// cause all symbols in the predicates to be declared locally
	w.DeclStatus = common.DSLocal

	return true
}

// namesFromIDList walks an identifier lists and yields the string names
// contained therein
func namesFromIDList(list *syntax.ASTBranch) []string {
	names := make([]string, len(list.Content)/2+1)

	n := 0
	for _, item := range list.Content {
		leaf := item.(*syntax.ASTLeaf)

		if leaf.Kind == syntax.IDENTIFIER {
			names[n] = leaf.Value
			n++
		}
	}

	return names
}

// typesFromExprList returns a slice of a types from an []HIRExpr
func typesFromExprList(exprList []common.HIRExpr) []types.DataType {
	types := make([]types.DataType, len(exprList))

	for i, expr := range exprList {
		types[i] = expr.Type()
	}

	return types
}