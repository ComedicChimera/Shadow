﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using LLVMSharp;

using Whirlwind.Semantic;
using Whirlwind.Types;

namespace Whirlwind.Generation
{
    partial class Generator
    {
        private delegate LLVMValueRef BinopBuilder(LLVMBuilderRef builder, LLVMValueRef vRef, LLVMValueRef rRef, string name);

        private delegate LLVMValueRef IntCompareBinopBuilder(LLVMBuilderRef builder, LLVMIntPredicate pred,
            LLVMValueRef vRef, LLVMValueRef rRef, string name);
        private delegate LLVMValueRef RealCompareBinopBuilder(LLVMBuilderRef builder, LLVMRealPredicate pred,
            LLVMValueRef vRef, LLVMValueRef rRef, string name);

        private delegate BinopBuilder NumericBinopBuilderFactory(int category);

        private LLVMValueRef _generateExpr(ITypeNode expr)
        {
            if (expr is ValueNode vnode)
                return _generateExprValue(vnode);
            // hopefully this is ok
            else if (expr is IdentifierNode inode)
                return _getNamedValue(inode.IdName);
            else if (expr is ConstexprNode cnode)
                return _generateExpr(cnode.ConstValue);
            // only other option is expr node
            else
            {
                var enode = (ExprNode)expr;

                switch (expr.Name)
                {
                    case "Array":
                        {
                            var elemType = ((ArrayType)expr.Type).ElementType;

                            var llvmElementType = _convertType(elemType);
                            var llvmArrayType = LLVM.ArrayType(llvmElementType, (uint)enode.Nodes.Count);

                            var arrLit = LLVM.BuildAlloca(_builder, llvmArrayType, "array_lit");

                            uint i = 0;
                            foreach (var item in enode.Nodes)
                            {
                                var vRef = _generateExpr(item);

                                if (!elemType.Equals(item.Type))
                                    vRef = _cast(vRef, item.Type, elemType);

                                var elemPtr = LLVM.BuildGEP(_builder, arrLit,
                                    new[] {
                                        LLVM.ConstInt(LLVM.Int32Type(), 0, new LLVMBool(0)),
                                        LLVM.ConstInt(LLVM.Int32Type(), i, new LLVMBool(0))
                                    },
                                    "elem_ptr"
                                    );

                                LLVM.BuildStore(_builder, vRef, elemPtr);

                                i++;
                            }

                            var arrPtr = LLVM.BuildInBoundsGEP(_builder, arrLit,
                                new[] { LLVM.ConstInt(LLVM.Int32Type(), 0, new LLVMBool(0)) },
                                "arr_ptr");

                            // create struct first!
                            return arrPtr;
                        }
                    case "Add":
                        {
                            if (expr.Type is ArrayType at)
                            {

                            }
                            else if (expr.Type is ListType lt)
                            {

                            }
                            else if (expr.Type is SimpleType st && st.Type == SimpleType.SimpleClassifier.STRING)
                            {

                            }
                            else
                            {
                                return _buildNumericBinop(category =>
                                {
                                    if (category == 2)
                                        return LLVM.BuildFAdd;
                                    else
                                        return LLVM.BuildAdd;
                                }, enode);
                            }
                        }
                        break;
                    case "Sub":
                        return _buildNumericBinop(category =>
                        {
                            if (category == 2)
                                return LLVM.BuildFSub;
                            else
                                return LLVM.BuildSub;
                        }, enode);                     
                    case "Mul":
                        return _buildNumericBinop(category =>
                        {
                            if (category == 2)
                                return LLVM.BuildFMul;
                            else
                                return LLVM.BuildMul;
                        }, enode);
                    // TODO: add NaN checking to both div and floordiv
                    case "Div":
                        return _buildNumericBinop(category =>
                        {
                            if (category == 2)
                                return LLVM.BuildFDiv;
                            else if (category == 1)
                                return LLVM.BuildUDiv;
                            else
                                return LLVM.BuildSDiv;
                        }, enode);
                    // TODO: see note on NaN checking
                    // TODO: make sure floor div is compatable with overload capability
                    case "Floordiv":
                        {
                            var result = _buildNumericBinop(category =>
                            {
                                if (category == 2)
                                    return LLVM.BuildFDiv;
                                else if (category == 1)
                                    return LLVM.BuildUDiv;
                                else
                                    return LLVM.BuildSDiv;
                            }, enode);

                            var commonType = _getCommonType(enode);
                            if (!enode.Type.Equals(commonType))
                                return _cast(result, commonType, enode.Type);

                            return result;
                        }
                    // TODO: add NaN checking
                    case "Mod":                        
                        return _buildNumericBinop(category =>
                        {
                            if (category == 2)
                                return LLVM.BuildFRem;
                            else if (category == 1)
                                return LLVM.BuildURem;
                            else
                                return LLVM.BuildSRem;
                        }, enode);
                    // TODO: add power operator implementation
                    case "LShift":
                        return _buildBinop(LLVM.BuildShl, enode, _getCommonType(enode));
                    // TODO: test binary right shift operation to make sure it is on signed integral types
                    case "RShift":
                        {
                            if (expr.Type is SimpleType st && !st.Unsigned)
                                return _buildBinop(LLVM.BuildAShr, enode, _getCommonType(enode));

                            return _buildBinop(LLVM.BuildLShr, enode, _getCommonType(enode));
                        }
                    case "Eq":
                        return _buildNumericBinop(category =>
                        {
                            if (category == 2)
                                return _buildCompareBinop(LLVM.BuildFCmp, LLVMRealPredicate.LLVMRealOEQ);

                            return _buildCompareBinop(LLVM.BuildICmp, LLVMIntPredicate.LLVMIntEQ);

                        }, enode);
                    case "Neq":
                        return _buildNumericBinop(category =>
                        {
                            if (category == 2)
                                return _buildCompareBinop(LLVM.BuildFCmp, LLVMRealPredicate.LLVMRealONE);

                            return _buildCompareBinop(LLVM.BuildICmp, LLVMIntPredicate.LLVMIntNE);

                        }, enode);
                    case "Gt":
                        return _buildNumericBinop(category =>
                        {
                            if (category == 2)
                                return _buildCompareBinop(LLVM.BuildFCmp, LLVMRealPredicate.LLVMRealOGT);
                            else if (category == 1)
                                return _buildCompareBinop(LLVM.BuildICmp, LLVMIntPredicate.LLVMIntUGT);

                            return _buildCompareBinop(LLVM.BuildICmp, LLVMIntPredicate.LLVMIntSGT);
                        }, enode);
                    case "Lt":
                        return _buildNumericBinop(category =>
                        {
                            if (category == 2)
                                return _buildCompareBinop(LLVM.BuildFCmp, LLVMRealPredicate.LLVMRealOLT);
                            else if (category == 1)
                                return _buildCompareBinop(LLVM.BuildICmp, LLVMIntPredicate.LLVMIntULT);

                            return _buildCompareBinop(LLVM.BuildICmp, LLVMIntPredicate.LLVMIntSLT);
                        }, enode);
                    case "GtEq":
                        return _buildNumericBinop(category =>
                        {
                            if (category == 2)
                                return _buildCompareBinop(LLVM.BuildFCmp, LLVMRealPredicate.LLVMRealOGE);
                            else if (category == 1)
                                return _buildCompareBinop(LLVM.BuildICmp, LLVMIntPredicate.LLVMIntUGE);

                            return _buildCompareBinop(LLVM.BuildICmp, LLVMIntPredicate.LLVMIntSGE);
                        }, enode);
                    case "LtEq":
                        return _buildNumericBinop(category =>
                        {
                            if (category == 2)
                                return _buildCompareBinop(LLVM.BuildFCmp, LLVMRealPredicate.LLVMRealOLE);
                            else if (category == 1)
                                return _buildCompareBinop(LLVM.BuildICmp, LLVMIntPredicate.LLVMIntULE);

                            return _buildCompareBinop(LLVM.BuildICmp, LLVMIntPredicate.LLVMIntSLE);
                        }, enode);
                    case "And":
                        return _buildBinop(LLVM.BuildAnd, enode, _getCommonType(enode));
                    case "Or":
                        return _buildBinop(LLVM.BuildOr, enode, _getCommonType(enode));
                    case "Xor":
                        return _buildBinop(LLVM.BuildXor, enode, _getCommonType(enode));
                        /*
                        case "Neq":
                            return _buildBinop((b, v1, v2, name) => LLVM.BuildICmp(b, LLVMIntPredicate.LLVMIntNE, v1, v2, name), enode);
                        case "Gt":
                            return _buildNumericBinop(category =>
                            {
                                if (category == 2)
                                    return 
                            }, enode);*/
                }
            }

            return _ignoreValueRef();
        }

        private BinopBuilder _buildCompareBinop(IntCompareBinopBuilder cbb, LLVMIntPredicate predicate)
        {
            return (b, lv, rv, name) => cbb(b, predicate, lv, rv, name);
        }

        private BinopBuilder _buildCompareBinop(RealCompareBinopBuilder rcbb, LLVMRealPredicate predicate)
        {
            return (b, lv, rv, name) => rcbb(b, predicate, lv, rv, name);
        }

        private LLVMValueRef _buildNumericBinop(NumericBinopBuilderFactory nbbfactory, ExprNode node)
        {
            // check for overloads

            var commonType = _getCommonType(node);
            int instrCat = 0;

            // we assume the common type is a simple type if it is being interpreted as numeric
            if (((SimpleType)commonType).Unsigned)
                instrCat = 1;
            else if (new[] { SimpleType.SimpleClassifier.FLOAT, SimpleType.SimpleClassifier.DOUBLE}
                .Contains(((SimpleType)commonType).Type))
            {
                instrCat = 2;
            }

            var binopBuilder = nbbfactory(instrCat);

            return _buildBinop(binopBuilder, node, commonType, true);
        }

        private LLVMValueRef _buildBinop(BinopBuilder bbuilder, ExprNode node, DataType commonType, bool noOverloads = false)
        {
            var operands = _buildOperands(node.Nodes, commonType);

            var leftOperand = operands[0];

            foreach (var rightOperand in operands.Skip(1))
            {
                // check for overloads

                leftOperand = bbuilder(_builder, leftOperand, rightOperand, node.Name.ToLower() + "_tmp");
            }

            return leftOperand;
        }

        private DataType _getCommonType(ExprNode node)
        {
            var commonType = node.Nodes[0].Type;

            foreach (var item in node.Nodes)
            {
                if (!commonType.Coerce(item.Type) && item.Type.Coerce(commonType))
                    commonType = item.Type;
            }

            return commonType;
        }

        private List<LLVMValueRef> _buildOperands(List<ITypeNode> nodes, DataType commonType)
        {
            var results = new List<LLVMValueRef>();

            foreach (var node in nodes)
            {
                var g = _generateExpr(node);

                if (!commonType.Equals(node.Type))
                    results.Add(_cast(g, node.Type, commonType));
                else
                    results.Add(g);
            }

            return results;
        }

        private bool _buildOperOverload(ITypeNode expr, out LLVMValueRef res)
        {
            res = _ignoreValueRef();
            return false;
        } 
    }
}
