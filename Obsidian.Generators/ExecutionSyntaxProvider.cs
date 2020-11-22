using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Obsidian.Generators
{
    public class ExecutionSyntaxProvider<T> : ISyntaxProvider<T> where T : SyntaxNode
    {
        public GeneratorExecutionContext Context { get; set; }
        private Predicate<T> predicate;

        public ExecutionSyntaxProvider() : this(t => true)
        {
        }

        public ExecutionSyntaxProvider(Predicate<T> predicate)
        {
            this.predicate = predicate;
        }

        public IEnumerable<T> GetSyntaxNodes()
        {
            foreach (var syntaxNode in Context.Compilation.SyntaxTrees)
            {
                Context.CancellationToken.ThrowIfCancellationRequested();

                foreach (var subnode in syntaxNode.GetRoot().DescendantNodesAndSelf().OfType<T>())
                {
                    if (HandleNode(subnode))
                        yield return subnode;
                }
            }
        }

        public ISyntaxProvider<T> WithContext(GeneratorExecutionContext context)
        {
            this.Context = context;
            return this;
        }

        public void OnVisitSyntaxNode(SyntaxNode syntaxNode)
        {
            return;
        }

        protected virtual bool HandleNode(T node)
        {
            return predicate(node);
        }
    }
}
