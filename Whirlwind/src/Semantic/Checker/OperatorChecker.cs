﻿using Whirlwind.Types;
using Whirlwind.Parser;
using Whirlwind.Semantic.Visitor;

using System.Collections.Generic;
using System.Linq;

namespace Whirlwind.Semantic.Checker
{
    partial class Checker
    {
        // takes in rootType by reference so it can handle overloads
        public static void CheckOperand(ref IDataType rootType, IDataType operandType, string op, TextPosition position)
        {
            bool valid = false;
            switch (op)
            {
                case "+":
                    {
                        if (Numeric(operandType))
                            valid = true;
                        else if (new SimpleType(SimpleType.DataType.STRING).Coerce(operandType))
                            valid = true;
                        else if (new[] { "ARRAY", "LIST" }.Contains(operandType.Classify()))
                            valid = true;
                        else if (HasOverload(rootType, "__add__", new List<ParameterValue>() { new ParameterValue(operandType) }, out IDataType returnType))
                        {
                            rootType = returnType;
                            return;
                        }
                    }
                    break;
                case "*":
                case "-":
                case "/":
                case "%":
                case "^":
                    {
                        if (Numeric(operandType))
                        {
                            valid = true;
                            break;
                        }

                        string methodName;
                        switch (op)
                        {
                            case "-":
                                methodName = "__sub__";
                                break;
                            case "*":
                                methodName = "__mul__";
                                break;
                            case "%":
                                methodName = "__mod__";
                                break;
                            case "/":
                                methodName = "__div__";
                                break;
                            default:
                                methodName = "__pow__";
                                break;
                        }

                        if (HasOverload(rootType, methodName, new List<ParameterValue>() { new ParameterValue(operandType) }, out IDataType returnType))
                        {
                            rootType = returnType;
                            return;
                        }
                    }
                    break;
                case ">>":
                case "<<":
                    {
                        if (rootType.Classify() == "SIMPLE_TYPE" && new SimpleType(SimpleType.DataType.INTEGER).Coerce(operandType))
                            return;
                        else if (HasOverload(rootType, op == ">>" ? "__lshift__" : "__rshift__", new List<ParameterValue>() { new ParameterValue(operandType) }, out IDataType returnType))
                        {
                            rootType = returnType;
                            return;
                        }
                    }
                    break;
                case "==":
                case "!=":
                    {
                        if (HasOverload(rootType, op == "==" ? "__eq__" : "__neq__", new List<ParameterValue>() { new ParameterValue(operandType) }, out IDataType returnType))
                        {
                            rootType = returnType;
                            return;
                        }
                        // all that is needed is operator congruence
                        valid = true;
                    }
                    break;
                case ">":
                case "<":
                case ">=":
                case "<=":
                    {
                        if (Numeric(operandType))
                        {
                            valid = true;
                            break;
                        }

                        string methodName;
                        switch (op)
                        {
                            case ">":
                                methodName = "__gt__";
                                break;
                            case "<":
                                methodName = "__lt__";
                                break;
                            case ">=":
                                methodName = "__gteq__";
                                break;
                            // <=
                            default:
                                methodName = "__lteq__";
                                break;
                        }

                        if (HasOverload(rootType, methodName, new List<ParameterValue>() { new ParameterValue(operandType) }, out IDataType returnType))
                        {
                            rootType = returnType;
                            return;
                        }
                    }
                    break;
                case "AND":
                case "OR":
                case "XOR":
                    {
                        if (operandType.Classify() == "SIMPLE_TYPE" && rootType.Classify() == "SIMPLE_TYPE")
                        {
                            if (rootType == operandType)
                                return;
                        }
                        else if (HasOverload(rootType, $"__{op.ToLower()}__", new List<ParameterValue>() { new ParameterValue(operandType) }, out IDataType returnType))
                        {
                            rootType = returnType;
                            return;
                        }
                    }
                    break;
            }

            if (valid)
            {
                if (!rootType.Coerce(operandType))
                {
                    if (operandType.Coerce(rootType))
                        rootType = operandType;
                    else
                        throw new SemanticException($"All operands of the '{op}' operator must be of similar types", position);
                }

                if (op == "/")
                    rootType = new SimpleType(SimpleType.DataType.FLOAT);
            }
            else
                throw new SemanticException($"Invalid operands for '{op}' operator", position);
        }
    }
}
