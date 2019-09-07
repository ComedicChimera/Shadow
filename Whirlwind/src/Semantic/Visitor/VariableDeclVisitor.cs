﻿using System;
using System.Collections.Generic;
using System.Linq;

using Whirlwind.Types;
using Whirlwind.Syntax;

namespace Whirlwind.Semantic.Visitor
{
    partial class Visitor
    {
        private struct Variable
        {
            public DataType Type;
            public TextPosition Position;

            public Variable(DataType type, TextPosition position)
            {
                Type = type;
                Position = position;
            }
        }

        private void _visitVarDecl(ASTNode stmt, List<Modifier> modifiers)
        {
            bool constant = false, constexpr = false, hasType = false, hasInitializer = false;
            DataType mainType = new NoneType();

            var variables = new Dictionary<string, Variable>();
            var initializers = new Dictionary<string, Tuple<bool, ITypeNode>>();

            foreach (var item in stmt.Content)
            {
                switch (item.Name)
                {
                    case "TOKEN":
                        if (((TokenNode)item).Tok.Type == "CONST")
                            constant = true;
                        else if (((TokenNode)item).Tok.Type == "VOL")
                            modifiers.Add(Modifier.VOLATILE);
                        else if (((TokenNode)item).Tok.Type == "STATIC")
                            modifiers.Add(Modifier.STATIC);
                        break;
                    case "var":
                        {
                            var variableBlock = (ASTNode)item;

                            if (variableBlock.Content.Count == 1)
                                variables[((TokenNode)variableBlock.Content[0]).Tok.Value]
                                    = new Variable(new NoneType(), variableBlock.Content[0].Position);
                            else
                            {
                                string currentIdentifier = "";

                                foreach (var varId in variableBlock.Content)
                                {
                                    if (varId.Name == "var_id")
                                    {
                                        foreach (var elem in ((ASTNode)varId).Content)
                                        {
                                            switch (elem.Name)
                                            {
                                                case "TOKEN":
                                                    if (new[] { "IDENTIFIER", "_" }.Contains(((TokenNode)elem).Tok.Type))
                                                    {
                                                        currentIdentifier = ((TokenNode)elem).Tok.Value;

                                                        variables[currentIdentifier] = new Variable(new NoneType(), elem.Position);
                                                    }
                                                    break;
                                                case "extension":
                                                    DataType dt = _generateType((ASTNode)((ASTNode)elem).Content[1]);
                                                    variables[currentIdentifier] = new Variable(dt, variables[currentIdentifier].Position);
                                                    break;
                                                case "variable_initializer":
                                                    {
                                                        var initNode = (ASTNode)((ASTNode)elem).Content[1];

                                                        if (variables[currentIdentifier].Type is NoneType)
                                                        {
                                                            _couldOwnerExist = true;
                                                            _visitExpr(initNode);
                                                            _couldOwnerExist = false;
                                                        }
                                                        else
                                                        {
                                                            _addContext(initNode);
                                                            _visitExpr(initNode);
                                                            _clearContext();
                                                        }

                                                        if (((TokenNode)((ASTNode)elem).Content[0]).Tok.Type == ":=")
                                                        {
                                                            if (_nodes.Last() is IncompleteNode)
                                                                throw new SemanticException("Unable to perform constexpr evaluation on incomplete type", elem.Position);

                                                            if (!Constexpr.Evaluator.TryEval(_nodes.Last()))
                                                                throw new SemanticException("Non constexpr value with constexpr initializer.", 
                                                                    item.Position);
                                                            else
                                                            {
                                                                ITypeNode node = _nodes.Last();
                                                                _nodes.RemoveAt(_nodes.Count - 1);

                                                                _nodes.Add(Constexpr.Evaluator.Evaluate(node));
                                                            }

                                                            constexpr = true;
                                                        }

                                                        _nodes.Add(new ExprNode(constexpr ? "ConstExprInitializer" : "Initializer", 
                                                            _nodes.Last().Type));
                                                        PushForward();

                                                        var initializer = _nodes.Last();
                                                        _nodes.RemoveAt(_nodes.Count - 1);

                                                        if (!variables[currentIdentifier].Type.Coerce(initializer.Type))
                                                            throw new SemanticException("Initializer type doesn't match type extension", 
                                                                variables[currentIdentifier].Position);

                                                        if (initializer.Type is IncompleteType && 
                                                            ((ExprNode)initializer).Nodes[0] is IncompleteNode inode2)
                                                        {
                                                            _giveContext(inode2, variables[currentIdentifier].Type);

                                                            ((ExprNode)initializer).Nodes[0] = _nodes.Last();
                                                            initializer.Type = _nodes.Last().Type;

                                                            _nodes.RemoveAt(_nodes.Count - 1);
                                                        }

                                                        variables[currentIdentifier] = new Variable(initializer.Type, 
                                                            variables[currentIdentifier].Position);

                                                        initializers[currentIdentifier]
                                                            = new Tuple<bool, ITypeNode>(((TokenNode)((ASTNode)elem).Content[0]).Tok.Type == ":=", 
                                                            initializer);
                                                    }
                                                    break;
                                            }
                                        }
                                    }
                                }
                            }
                        }
                        break;
                    case "extension":
                        mainType = _generateType((ASTNode)((ASTNode)item).Content[1]);
                        hasType = true;
                        break;
                    case "variable_initializer":
                        var mainInitNode = (ASTNode)((ASTNode)item).Content[1];

                        if (hasType)
                        {
                            _addContext(mainInitNode);
                            _visitExpr(mainInitNode);
                            _clearContext();
                        }
                        else
                        {
                            _couldOwnerExist = true;
                            _visitExpr(mainInitNode);
                            _couldOwnerExist = false;
                        }                                               

                        if (((TokenNode)((ASTNode)item).Content[0]).Tok.Type == ":=")
                        {
                            if (_nodes.Last() is IncompleteNode)
                                throw new SemanticException("Unable to perform constexpr evaluation on an incomplete type", item.Position);

                            if (!Constexpr.Evaluator.TryEval(_nodes.Last()))
                                throw new SemanticException("Non constexpr value with constexpr initializer.", item.Position);
                            else
                            {
                                ITypeNode node = _nodes.Last();
                                _nodes.RemoveAt(_nodes.Count - 1);

                                _nodes.Add(Constexpr.Evaluator.Evaluate(node));
                            }

                            constexpr = true;
                        }

                        _nodes.Add(new ExprNode(constexpr ? "ConstExprInitializer" : "Initializer", _nodes.Last().Type));
                        PushForward();

                        if (!hasType && _isVoid(_nodes.Last().Type))
                            throw new SemanticException("Unable to infer type of a variable from initializer", item.Position);
                        else if (!hasType)
                        {
                            mainType = _nodes.Last().Type;
                            hasType = true;
                        }
                        else if (!mainType.Coerce(_nodes.Last().Type))
                            throw new SemanticException("Initializer type doesn't match type extension", item.Position);

                        // we know there is a type that exists to give context to i-node
                        if (_nodes.Last().Type is IncompleteType && ((ExprNode)_nodes.Last()).Nodes[0] is IncompleteNode inode)
                        {
                            _giveContext(inode, mainType);

                            ((ExprNode)_nodes[_nodes.Count - 2]).Nodes[0] = _nodes.Last();
                            _nodes[_nodes.Count - 2].Type = _nodes.Last().Type;

                            _nodes.RemoveAt(_nodes.Count - 1);
                        }

                        hasInitializer = true;
                        break;
                }
            }

            if (hasType && hasInitializer && mainType.Classify() == TypeClassifier.TUPLE && variables.Keys.Count > 1)
            {
                TupleType tupleType = (TupleType)mainType;

                if (variables.Count == tupleType.Types.Count)
                {
                    int i = 0, j = 0;
                    string id;
                    DataType dt;

                    while (i < variables.Count && j < tupleType.Types.Count)
                    {
                        id = variables.Keys.ElementAt(i);
                        dt = tupleType.Types[j];

                        if (initializers.ContainsKey(id))
                            throw new SemanticException("Unable to perform tuple based initialization on pre-initialized values", variables[id].Position);
                        else if (_isVoid(variables.Values.ElementAt(i).Type))
                            variables[id] = new Variable(dt, variables[id].Position);
                        else if (!variables[id].Type.Coerce(dt))
                            throw new SemanticException("Tuple types and variable types must match", variables[id].Position);

                        i++;
                        j++;
                    }
                }
                else
                    throw new SemanticException("The number of variables must match the size of tuple being assigned", stmt.Position);
            }
            else
            {
                for (int i = 0; i < variables.Count; i++)
                {
                    var key = variables.Keys.ToList()[i];
                    if (_isVoid(variables[key].Type))
                    {
                        if (hasType)
                            variables[key] = new Variable(mainType, variables[key].Position);
                        else
                            throw new SemanticException("Unable to infer type of variable", variables[key].Position);
                    }
                }
            }

            foreach (var variable in variables.Keys)
            {
                if (variable == "_")
                    continue;

                Symbol symbol;

                if (initializers.ContainsKey(variable) && initializers[variable].Item1)
                    symbol = new Symbol(variable, variables[variable].Type, ((ExprNode)initializers[variable].Item2).Nodes[0]);
                else if (constexpr && !initializers.ContainsKey(variable))
                    // last item on stack will always be the main initializer if constexpr
                    symbol = new Symbol(variable, variables[variable].Type, ((ExprNode)_nodes.Last()).Nodes[0]);
                else
                    symbol = new Symbol(variable, variables[variable].Type);

                foreach (var modifier in modifiers)
                    symbol.Modifiers.Add(modifier);

                if (constant)
                    symbol.DataType.Constant = true;

                if (!_table.AddSymbol(symbol))
                    throw new SemanticException("Variable declared multiple times in the current scope", variables[variable].Position);

                _nodes.Add(new IdentifierNode(variable, variables[variable].Type));

                if (initializers.ContainsKey(variable))
                {
                    _nodes.Add(initializers[variable].Item2);
                    _nodes.Add(new ExprNode("Var", variables[variable].Type));
                    PushForward(2);
                }
                else
                {
                    _nodes.Add(new ExprNode("Var", variables[variable].Type));
                    PushForward();
                }
            }

            _nodes.Add(new ExprNode("Variables", new NoneType()));
            PushForward(variables.Keys.Where(x => x != "_").Count());

            string statementName;

            if (constexpr)
                statementName = "DeclareConstexpr";
            else if (constant)
                statementName = "DeclareConstant";
            else
                statementName = "DeclareVariable";

            _nodes.Add(new StatementNode(statementName));
            PushForward();
            ((StatementNode)_nodes[_nodes.Count - 1]).Nodes.Reverse();

            if (hasInitializer && initializers.Count == 0)
                PushForward();
        }
    }
}
