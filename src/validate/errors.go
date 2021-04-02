package validate

import (
	"fmt"

	"whirlwind/logging"
	"whirlwind/typing"
)

// LogUndefined logs an undefined error for the given symbol.
func (w *Walker) LogUndefined(name string, pos *logging.TextPosition) {
	logging.LogCompileError(w.Context, fmt.Sprintf("Symbol `%s` undefined", name), logging.LMKName, pos)
}

// LogNotVisibleInPackage logs an import error in which is a symbol is not able
// to be imported from a foreign package.
func (w *Walker) LogNotVisibleInPackage(symname, pkgname string, pos *logging.TextPosition) {
	logging.LogCompileError(
		w.Context,
		fmt.Sprintf("Symbol `%s` is not externally visible in package `%s`", symname, pkgname),
		logging.LMKName,
		pos,
	)
}

// logRepeatDef logs an error indicate that a symbol has already been defined
func (w *Walker) logRepeatDef(name string, pos *logging.TextPosition) {
	logging.LogCompileError(
		w.Context,
		fmt.Sprintf("Symbol `%s` already defined", name),
		logging.LMKName,
		pos,
	)
}

// logInvalidIntrinsic marks that the given named type cannot be intrinsic.
// Sets `fatalDefError`.
func (w *Walker) logInvalidIntrinsic(name, kind string, pos *logging.TextPosition) {
	w.logError(
		fmt.Sprintf("No intrinsic %s by name `%s`", kind, name),
		logging.LMKUsage,
		pos,
	)
}

// logCoercionError logs an error coercing from one type to another
func (w *Walker) logCoercionError(src, dest typing.DataType, pos *logging.TextPosition) {
	w.logError(
		fmt.Sprintf("Unable to coerce from `%s` to `%s`", src.Repr(), dest.Repr()),
		logging.LMKTyping,
		pos,
	)
}

// logError logs an error of any kind within the walker's file
func (w *Walker) logError(message string, kind int, pos *logging.TextPosition) {
	logging.LogCompileError(
		w.Context,
		message,
		kind,
		pos,
	)
}
