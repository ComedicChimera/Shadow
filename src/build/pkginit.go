package build

import (
	"fmt"
	"hash/fnv"
	"io/ioutil"
	"path/filepath"

	"github.com/ComedicChimera/whirlwind/src/common"
	"github.com/ComedicChimera/whirlwind/src/logging"
	"github.com/ComedicChimera/whirlwind/src/syntax"
	"github.com/ComedicChimera/whirlwind/src/typing"
)

// SrcFileExtension is used to indicate what the file extension is for a
// Whirlwind source file (used to identify files when loading packages)
const SrcFileExtension = ".wrl"

// initPackage takes a directory path and parses all files in the directory and
// creates entries for them in a new package created based on the directory's
// name.  It does not extract any definitions or do anything more than
// initialize a package based on the contents and name of a provided directory.
// Note: User should check LogModule after this is called as file level errors
// are not returned!  `abspath` should be the absolute path to the package. It
// also returns a boolean flag indicating whether or not compilation should
// proceed.
func (c *Compiler) initPackage(abspath string) (*common.WhirlPackage, error, bool) {
	pkgName := filepath.Base(abspath)

	if !isValidPkgName(pkgName) {
		return nil, fmt.Errorf("Invalid package name: `%s`", pkgName), false
	}

	pkg := &common.WhirlPackage{
		PackageID:         getPackageID(abspath),
		Name:              pkgName,
		RootDirectory:     abspath,
		Files:             make(map[string]*common.WhirlFile),
		ImportTable:       make(map[uint]*common.WhirlImport),
		OperatorOverloads: make(map[int][]*common.WhirlOperatorOverload),
		GlobalTable:       make(map[string]*common.Symbol),
		GlobalBindings:    &typing.BindingRegistry{},
	}

	// try to open the package directory
	files, err := ioutil.ReadDir(abspath)
	if err != nil {
		logging.LogStdError(err)
	}

	// walk each file in the directory.  all file level errors (eg. syntax
	// errors) are logged with the log module for display later -- this is
	// to ensure that every file is walked at least once
	for _, fInfo := range files {
		if !fInfo.IsDir() && filepath.Ext(fInfo.Name()) == SrcFileExtension {
			fpath := filepath.Join(abspath, fInfo.Name())
			c.lctx.FilePath = fpath

			sc, err := syntax.NewScanner(fpath, c.lctx)

			if err != nil {
				logging.LogStdError(err)
				continue
			}

			shouldCompile, tags, err := c.preprocessFile(sc)
			if err != nil {
				logging.LogStdError(err)
				continue
			}

			if !shouldCompile {
				continue
			}

			ast, err := c.parser.Parse(sc)

			if err != nil {
				logging.LogStdError(err)
				continue
			}

			abranch := ast.(*syntax.ASTBranch)
			pkg.Files[fpath] = &common.WhirlFile{
				AST:                    abranch,
				MetadataTags:           tags,
				LocalTable:             make(map[string]*common.WhirlSymbolImport),
				LocalOperatorOverloads: make(map[int][]typing.DataType),
				VisiblePackages:        make(map[string]*common.WhirlPackage),
				LocalBindings:          &typing.BindingRegistry{},
			}
		}
	}

	if err != nil {
		return nil, err, false
	}

	if len(pkg.Files) == 0 {
		return nil, fmt.Errorf("Unable to load package by name `%s` because it contains no source files", pkg.Name), false
	}

	c.depGraph[pkg.PackageID] = pkg
	return pkg, nil, logging.ShouldProceed()
}

// isValidPkgName tests if the package name would be a usable identifier within
// Whirlwind. If it is not, the package name is considered to be invalid and an
// error should be thrown.
func isValidPkgName(pkgName string) bool {
	if syntax.IsLetter(rune(pkgName[0])) || pkgName[0] == '_' {
		for i := 1; i < len(pkgName); i++ {
			if !syntax.IsLetter(rune(pkgName[i])) && !syntax.IsDigit(rune(pkgName[i])) && pkgName[i] != '_' {
				return false
			}
		}

		return true
	}

	return false
}

// getPackageID calculates a package ID hash based on a package's file path
func getPackageID(abspath string) uint {
	h := fnv.New32a()
	h.Write([]byte(abspath))
	return uint(h.Sum32())
}
