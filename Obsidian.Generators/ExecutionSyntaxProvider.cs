using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Obsidian.Generators
{
    public class ExecutionSyntaxProvider<T> : ISyntaxProvider<T> where T : SyntaxNode
    {
        private GeneratorExecutionContext context;
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
            foreach (var syntaxNode in context.Compilation.SyntaxTrees)
            {
                context.CancellationToken.ThrowIfCancellationRequested();

                foreach (var subnode in syntaxNode.GetRoot().DescendantNodesAndSelf().OfType<T>())
                {
                    if (predicate(subnode))
                        yield return subnode;
                }
            }
        }

        public ISyntaxProvider<T> WithContext(GeneratorExecutionContext context)
        {
            this.context = context;
            return this;
        }

        public void OnVisitSyntaxNode(SyntaxNode syntaxNode)
        {
            return;
        }
    }
}
