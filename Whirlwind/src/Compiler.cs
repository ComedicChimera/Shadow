﻿using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;

using Whirlwind.Syntax;
using Whirlwind.Semantic.Visitor;
using Whirlwind.Semantic;
using Whirlwind.Generation;
using Whirlwind.Types;

using static Whirlwind.WhirlGlobals;

namespace Whirlwind
{
    class Compiler
    {
        private Scanner _scanner;
        private Parser _parser;
        private PackageManager _pm;

        private bool _compiledMainPackage;
        private Dictionary<string, DataType> _typeImpls;

        public Compiler(string tokenPath, string grammarPath)
        {
            _scanner = new Scanner(WHIRL_PATH + tokenPath);

            var gramloader = new GramLoader();
            _parser = new Parser(gramloader.Load(WHIRL_PATH + grammarPath));

            _pm = new PackageManager();

            _compiledMainPackage = false;
            _typeImpls = new Dictionary<string, DataType>();
        }

        public bool Build(string path)
        {
            return Build(path, out PackageType _);
        }

        public bool Build(string path, out PackageType pkgType)
        {
            string namePrefix = _pm.ConvertPathToPrefix(path);

            if (namePrefix == "")
                namePrefix = "";
            else if (!namePrefix.EndsWith("::"))
                namePrefix += "::";

            var currentCtx = _pm.ImportContext;

            pkgType = null;

            if (_pm.LoadPackage(path, namePrefix.Trim(':'), out Package pkg))
            {
                if (pkg.Compiled)
                {
                    pkgType = pkg.Type;
                    return true;
                }
                    
                for (int i = 0; i < pkg.Files.Count; i++)
                {
                    string fName = pkg.Files.Keys.ElementAt(i);

                    string text = File.ReadAllText(fName);

                    var tokens = _scanner.Scan(text);

                    ASTNode ast = _runParser(tokens, text, fName);

                    if (ast == null)
                        return false;

                    pkg.Files[fName] = ast;
                }

                var pa = new PackageAssembler(pkg);
                var finalAst = pa.Assemble();

                var visitor = new Visitor(namePrefix, false, _typeImpls);

                if (!_runVisitor(visitor, finalAst, pkg))
                    return false;

                var table = visitor.Table();
                var generator = new Generator(table, visitor.Flags(), _typeImpls, namePrefix);

                try
                {
                    generator.Generate(visitor.Result(), namePrefix.Trim(':') + ".llvm");
                }
                catch (GeneratorException ge)
                {
                    Console.WriteLine("Generator Error: " + ge.ErrorMessage);
                }

                var eTable = table.Filter(s => s.Modifiers.Contains(Modifier.EXPORTED));

                // update _pm package data
                pkg.Compiled = true;
                pkg.Type = new PackageType(eTable);

                _pm.ImportContext = currentCtx;

                pkgType = pkg.Type;
                return true;
            }
            else
                return false;

            /*
            bool isMainPackage;

            if (!_compiledMainPackage)
            {
                isMainPackage = true;
                _compiledMainPackage = true;
            }
            else
                isMainPackage = false;

            if (!namePrefix.StartsWith("lib::std::__core__"))
                text = "include { ... } from __core__::prelude; " + text;

            if (namePrefix != "lib::std::__core__::types::type_impls::")
                text = "include __core__::types::type_impls as __type_impls;\n" + text;

            var tokens = _scanner.Scan(text);

            ASTNode ast = _runParser(_parser, tokens, text, namePrefix);

            if (ast == null)
                return;
            
            var visitor = new Visitor(namePrefix, false, _typeImpls);

            if (!_runVisitor(visitor, ast, text, namePrefix))
                return;

            var fullTable = visitor.Table();
            var sat = visitor.Result();

            if (isMainPackage)
            {
                if (!fullTable.Lookup("main", out Symbol symbol))
                {
                    Console.WriteLine("Main file missing main function definition");
                    return;
                }

                if (symbol.DataType.Classify() != TypeClassifier.FUNCTION)
                {
                    Console.WriteLine("Symbol `main` in main file must be a function");
                    return;
                }

                var userMainDefinition = (FunctionType)symbol.DataType;

                if (!_generateUserMainCall(userMainDefinition, out string userMainCall))
                {
                    Console.WriteLine("Invalid main function declaration");
                    return;
                }

                var mainTemplate = File.ReadAllText(WHIRL_PATH + "lib/std/__core__/main.wrl")
                    .Replace("// $INSERT_MAIN_CALL$", userMainCall);

                var mtTokens = _scanner.Scan(mainTemplate);

                var mtAst = _runParser(_parser, mtTokens, mainTemplate, namePrefix);

                if (mtAst == null)
                    return;

                var mtVisitor = new Visitor("", false, _typeImpls);
                mtVisitor.Table().AddSymbol(symbol.Copy());

                if (!_runVisitor(mtVisitor, mtAst, mainTemplate, namePrefix))
                    return;

                foreach (var item in mtVisitor.Table().GetScope().Skip(1))
                {
                    if (!fullTable.AddSymbol(item))
                    {
                        if (item.Name == "__main")
                        {
                            Console.WriteLine("Use of reserved name in main file");
                            return;
                        }                           
                    }
                }

                ((BlockNode)sat).Block.AddRange(((BlockNode)mtVisitor.Result()).Block);
            }

            var generator = new Generator(fullTable, visitor.Flags(), _typeImpls, namePrefix);

            try
            {
                // supplement in real file name when appropriate
                generator.Generate(sat, "test.llvm");
            }
            catch (GeneratorException ge)
            {
                Console.WriteLine("Generation Error: " + ge.ErrorMessage);
            }

            table = fullTable.Filter(x => x.Modifiers.Contains(Modifier.EXPORTED));

            */
        }

        private ASTNode _runParser(List<Token> tokens, string text, string package)
        {
            try
            {
                return _parser.Parse(tokens);
            }
            catch (InvalidSyntaxException isx)
            {
                ErrorDisplay.DisplayError(text, package, isx);
                return null;
            }
        }

        private bool _runVisitor(Visitor visitor, ASTNode ast, Package pkg)
        {
            try
            {
                visitor.Visit(ast);
            }
            catch (SemanticException smex)
            {
                ErrorDisplay.DisplayError(pkg, smex);
                return false;
            }

            if (visitor.ErrorQueue.Count > 0)
            {
                foreach (var error in visitor.ErrorQueue)
                    ErrorDisplay.DisplayError(pkg, error);

                return false;
            }

            Console.WriteLine(visitor.Result().ToString());

            return true;
        }

        private bool _generateUserMainCall(FunctionType mainFnType, out string callString)
        {
            if (mainFnType.ReturnType.Classify() == TypeClassifier.NONE)
                callString = "main(";
            else if (new SimpleType(SimpleType.SimpleClassifier.INTEGER).Equals(mainFnType.ReturnType))
                callString = "exitCode = main(";
            else
            {
                callString = "";
                return false;
            }

            if (mainFnType.Parameters.Count == 0)
                callString += ");";
            else if (mainFnType.Parameters.Count == 1)
            {
                var arg1 = mainFnType.Parameters.First().DataType;

                if (new ArrayType(new SimpleType(SimpleType.SimpleClassifier.STRING, true), -1).Equals(arg1))
                    callString += "args);";
                else
                {
                    callString = "";
                    return false;
                }
            }
            else
            {
                callString = "";
                return false;
            }

            return true;
        }
    }
}
