﻿using System;
using System.Linq;
using Shadow.Parser;

namespace Shadow
{
    class Compiler
    {
        private Scanner _scanner;
        private ShadowParser _parser;

        public Compiler(string tokenPath, string grammarPath)
        {
            _scanner = new Scanner(tokenPath);

            var gramloader = new GramLoader();
            _parser = new ShadowParser(gramloader.Load(grammarPath));

        }

        public void Build(string text)
        {
            var tokens = _scanner.Scan(text);

            ASTNode ast;
            try
            {
                ast = _parser.Parse(tokens);
            }
            catch (InvalidSyntaxException isx)
            {
                if (isx.Tok.Type == "EOF")
                    Console.WriteLine("Unexpected End of File");
                else
                {
                    int line = GetLine(text, isx.Tok.Index), column = GetColumn(text, isx.Tok.Index);
                    Console.WriteLine($"Unexpected Token: \'{isx.Tok.Value}\' at (Line:{line + 1}, Column: {column})");
                    Console.WriteLine($"\n\t{text.Split('\n')[line].Trim('\t')}");
                    Console.WriteLine("\t" + String.Concat(Enumerable.Repeat(" ", column - 1)) + String.Concat(Enumerable.Repeat("^", isx.Tok.Value.Length)));
                }
                return;
            }
            Console.WriteLine(ast.ToString());
        }

        private int GetLine(string text, int ndx)
        {
            return text.Substring(0, ndx + 1).Count(x => x == '\n');
        }

        private int GetColumn(string text, int ndx)
        {
            var splitText = text.Substring(0, ndx + 1).Split('\n');
            return splitText[splitText.Count() - 1].Trim('\t').Length;
        }
    }
}
