﻿using Whirlwind.Parser;
using Whirlwind.Types;
using static Whirlwind.Semantic.Checker.Checker;

using System.Collections.Generic;
using System.Linq;

namespace Whirlwind.Semantic.Visitor
{
    partial class Visitor
    {
        public void _visitIterator(ASTNode node)
        {
            // expects previous node to be the iterable value
            var iterable = _nodes.Last().Type;
            IDataType iteratorType;

            if (Iterable(iterable))
            {
                iteratorType = _getIterableElementType(iterable);
            }
            else
                throw new SemanticException("Unable to create iterator over non-iterable value", node.Position);

            // all are tokens
            string[] identifiers = node.Content.Select(x => ((TokenNode)x).Tok)
                .Where(x => x.Type == "IDENTIFIER")
                .Select(x => x.Value)
                .ToArray();

            var iteratorTypes = iteratorType.Classify().StartsWith("TUPLE") ? ((TupleType)iteratorType).Types : new List<IDataType>() { iteratorType };

            if (identifiers.Length != iteratorTypes.Count)
                throw new SemanticException("Base iterator and it's alias's don't match", node.Position);

            for (int i = 0; i < identifiers.Length; i++)
            {
                _table.AddSymbol(new Symbol(identifiers[i], iteratorTypes[i]));
            }

            _nodes.Add(new TreeNode(
                "Iterator",
                new SimpleType(),
                Enumerable.Range(0, identifiers.Length)
                    .Select(i => new ValueNode("Identifier", iteratorTypes[i], identifiers[i]))
                    .Select(x => x as ITypeNode)
                    .ToList()
            ));
        }

        private IDataType _getIterableElementType(IDataType iterable)
        {
            if (iterable is IIterable)
                return (iterable as IIterable).GetIterator();
            // should never fail - not a true overload so check not required
            else if (((ModuleInstance)iterable).GetProperty("__next__", out Symbol method))
            {
                // all iterable __next__ methods return a specific element type (Element<T>)
                var elementType = ((FunctionType)method.DataType).ReturnType;

                return ((StructType)elementType).Members["val"];
            }
            return new SimpleType();
        }

        private IDataType _generateTemplate(TemplateType baseType, ASTNode templateSpecifier)
        {
            // template_spec -> type_list
            var typeList = _generateTypeList((ASTNode)templateSpecifier.Content[1]);
            
            if (baseType.CreateTemplate(typeList, out IDataType dt))
                return dt;
            else
                throw new SemanticException("Invalid type specifier for the given template", templateSpecifier.Position);

        }
    }
}