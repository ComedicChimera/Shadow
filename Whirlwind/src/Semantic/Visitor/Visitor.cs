﻿using System.Collections.Generic;
using System.Linq;
using System;

using Whirlwind.Syntax;
using Whirlwind.Types;

namespace Whirlwind.Semantic.Visitor
{
    // The Visitor Context Struct
    // used to preserve context when descending into
    // a lambda body without corrupting the context
    // of that expression and preventing the corruption
    // of the above context
    struct VisitorContext
    {
        public readonly bool 
            CouldLambdaContextExist, 
            CouldTypeClassContextExist,
            CouldOwnerExist,
            IsSetContext;

        public readonly CustomType TypeClassContext;

        public VisitorContext(bool clc, bool ctcc, bool co, bool isc, CustomType tcc)
        {
            CouldLambdaContextExist = clc;
            CouldTypeClassContextExist = ctcc;
            CouldOwnerExist = co;
            IsSetContext = isc;
            TypeClassContext = tcc;
        }
    }

    // The Visitor Class
    // Responsible for performing the
    // bulk of semantic analysis and
    // constructing the Semantic Action Tree
    partial class Visitor
    {
        // ------------
        // Visitor Data
        // ------------

        // exposed list of all semantic exceptions accumulated
        // during visitation; processed outside of visitor
        public List<SemanticException> ErrorQueue;

        // visitor shared constructs
        private List<ITypeNode> _nodes;
        private SymbolTable _table;
        private Dictionary<string, string> _annotatedSettings;

        // compiler-provided shared data
        private string _namePrefix;
        private bool _constexprOptimizerEnabled;
        
        // visitor state data
        private CustomType _typeClassContext;

        // visitor state flags
        private bool _couldLambdaContextExist = false, _couldTypeClassContextExist = false;
        private bool _didTypeClassCtxInferFail = false;
        private bool _isTypeCast = false;
        private bool _isGenericSelfContext = false;
        private bool _couldOwnerExist = false, _isFinalizer = false;
        private bool _isSetContext = false;
        private bool _isExprStmt = false;
        private bool _wrapsNextAnnotBlock = false;
        private bool _functionCanHaveNoBody = false;

        public Visitor(string namePrefix, bool constexprOptimizerEnabled)
        {
            ErrorQueue = new List<SemanticException>();

            _nodes = new List<ITypeNode>();
            _table = new SymbolTable();
            _annotatedSettings = new Dictionary<string, string>();

            _namePrefix = namePrefix;
            _constexprOptimizerEnabled = constexprOptimizerEnabled;
        }

        // ----------------------
        // Main Visitor Functions
        // ----------------------

        // main visit function
        public void Visit(ASTNode ast)
        {
            _nodes.Add(new BlockNode("Package"));

            var variableDecls = new List<Tuple<ASTNode, List<Modifier>>>();

            // visit block nodes first
            foreach (INode node in ast.Content)
            {
                switch (node.Name)
                {
                    case "block_decl":
                        _visitBlockDecl((ASTNode)node, new List<Modifier>());
                        break;
                    case "variable_decl":
                        variableDecls.Add(new Tuple<ASTNode, List<Modifier>>((ASTNode)node, new List<Modifier>()));
                        continue;
                    case "export_decl":
                        ASTNode decl = (ASTNode)((ASTNode)node).Content[1];

                        switch (decl.Name)
                        {
                            case "block_decl":
                                _visitBlockDecl(decl, new List<Modifier>() { Modifier.EXPORTED });
                                break;
                            case "variable_decl":
                                variableDecls.Add(new Tuple<ASTNode, List<Modifier>>(decl, new List<Modifier>() { Modifier.EXPORTED }));
                                continue;
                            case "include_stmt":
                                _visitInclude(decl, true);
                                break;
                        }
                        break;
                    // add any logic surrounding inclusion
                    case "include_stmt":
                        _visitInclude((ASTNode)node, true);
                        break;
                    case "annotation":
                        // if no block was found, perform check here (make sure it isn't ignored
                        if (_wrapsNextAnnotBlock)
                            throw new SemanticException("Unable to stack annotations", node.Position);

                        _visitAnnotation((ASTNode)node);
                        
                        // only merge if it is not expecting a block
                        if (!_wrapsNextAnnotBlock)
                            MergeToBlock();

                        // prevent state from being cleared/checked
                        continue;
                    // catches rogue tokens and things of the like
                    default:
                        continue;
                }

                // both cases only reached after non-annotation block
                if (_wrapsNextAnnotBlock)
                    throw new SemanticException("Invalid application of annotation to block", node.Position);
                if (_functionCanHaveNoBody)
                    _functionCanHaveNoBody = false;

                MergeToBlock();
            }

            // if annotation block never satisfied, error
            if (_wrapsNextAnnotBlock)
                throw new SemanticException("Annotation missing block", ast.Content.Last().Position);

            // visit variable declaration second
            foreach (var varDecl in variableDecls)
            {
                _visitVarDecl(varDecl.Item1, varDecl.Item2);
                MergeToBlock();
            }

            _completeTree();
        }

        // returns final, visited node
        public ITypeNode Result() => _nodes.First();

        // returns exported list of symbols
        public SymbolTable Table() => _table;

        // returns the list of visitor flags
        public Dictionary<string, string> Flags() => _annotatedSettings;

        // ------------------------------------
        // Construction Stack Control Functions
        // ------------------------------------

        // moves all the ending nodes on the CS stack into
        // the node at the specified depth in order
        private void MergeBack(int depth = 1)
        {
            if (_nodes.Count <= depth)
                return;
            for (int i = 0; i < depth; i++)
            {
                ((TreeNode)_nodes[_nodes.Count - (depth - i + 1)]).Nodes.Add(_nodes[_nodes.Count - (depth - i)]);
                _nodes.RemoveAt(_nodes.Count - (depth - i));
            }
        }

        // moves all the nodes preceding the ending node
        // as far back as the specified depth on the CS stack 
        // into the ending node in order
        private void PushForward(int depth = 1)
        {
            if (_nodes.Count <= depth)
                return;
            var ending = (TreeNode)_nodes.Last();
            _nodes.RemoveLast();

            for (int i = 0; i < depth; i++)
            {
                int pos = _nodes.Count - (depth - i);
                ending.Nodes.Add(_nodes[pos]);
                _nodes.RemoveAt(pos);
            }

            _nodes.Add((ITypeNode)ending);
        }

        // moves the node preceding the ending node
        // into the ending node's block
        private void PushToBlock()
        {
            BlockNode parent = (BlockNode)_nodes.Last();
            _nodes.RemoveLast();

            parent.Block.Add(_nodes.Last());
            _nodes.RemoveLast();

            _nodes.Add(parent);
        }

        // moves the ending node into the preceding
        // node's block
        private void MergeToBlock()
        {
            ITypeNode child = _nodes.Last();
            _nodes.RemoveLast();

            ((BlockNode)_nodes.Last()).Block.Add(child);
        }

        // -------------------------------------------
        // Context-Based/Inductive Inferencing Helpers
        // -------------------------------------------

        // sets type class context label and calls check for setting
        // lambda context label
        private void _addContext(ASTNode node)
        {
            _couldTypeClassContextExist = true;
            _couldOwnerExist = true;

            _addLambdaContext(node);
        }

        // checks if lambda context could exist and sets appropriate label if it can
        private void _addLambdaContext(ASTNode node)
        {
            if (node.Name == "lambda")
                _couldLambdaContextExist = true;
            else if (node.Content.Count == 1 && node.Content[0] is ASTNode anode)
                _addLambdaContext(anode);
        }

        // applies context if possible, errors if not
        private void _giveContext(IncompleteNode inode, DataType ctx)
        {
            if (ctx is FunctionType fctx)
                _visitLambda(inode.AST, fctx);
            else if (ctx is CustomType tctx)
            {
                _typeClassContext = tctx;

                _visitExpr(inode.AST);

                _typeClassContext = null;
            }
            else
            {
                string errorMsg;

                if (_didTypeClassCtxInferFail)
                {
                    errorMsg = "Unable to infer type class context for expression";
                    _didTypeClassCtxInferFail = false;
                }
                else
                    errorMsg = "Unable to infer type(s) of lambda arguments";

                throw new SemanticException(errorMsg, inode.AST.Position);
            }
        }

        // sets all expression level visitor flags to false
        private void _clearContext()
        {
            _couldLambdaContextExist = false;
            _couldTypeClassContextExist = false;
            _couldOwnerExist = false;
            _isSetContext = false;

            _typeClassContext = null;
        }

        // saves the visitor context into an object
        // no need to copy here b/c references are 
        // recreated by whatever state they are being
        // transistioned to and cleared by clear context
        // before hand
        private VisitorContext _saveContext()
            => new VisitorContext(
                   _couldLambdaContextExist,
                   _couldTypeClassContextExist,
                   _couldOwnerExist,
                   _isSetContext,
                   _typeClassContext
               );

        // restores the current visitor context
        // from an object
        private void _restoreContext(VisitorContext vctx)
        {
            _couldLambdaContextExist = vctx.CouldLambdaContextExist;
            _couldTypeClassContextExist = vctx.CouldTypeClassContextExist;
            _couldOwnerExist = vctx.CouldOwnerExist;
            _isSetContext = vctx.IsSetContext;
            _typeClassContext = vctx.TypeClassContext;
        }

        // -------------------
        // Utility Function(s)
        // -------------------

        // checks to see if a type is void
        private bool _isVoid(DataType type)
        {
            if (type.Classify() == TypeClassifier.VOID || type.Classify() == TypeClassifier.NULL)
                return true;
            else if (type.Classify() == TypeClassifier.DICT)
            {
                var dictType = (DictType)type;

                return _isVoid(dictType.KeyType) || _isVoid(dictType.ValueType);
            }
            else if (type is IIterable)
                return _isVoid(((IIterable)type).GetIterator());
            else
                return false;
        }
    }
}
