using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;

namespace Obsidian.Generators
{
    public class ProgressiveSyntaxProvider<T> : ISyntaxProvider<T> where T : SyntaxNode
    {
        private List<T> matches = new List<T>();
        private Predicate<T> predicate;

        public ProgressiveSyntaxProvider() : this(t => true)
        {
        }

        public ProgressiveSyntaxProvider(Predicate<T> predicate)
        {
            this.predicate = predicate;
        }

        public IEnumerable<T> GetSyntaxNodes()
        {
            return matches;
        }

        public void OnVisitSyntaxNode(SyntaxNode syntaxNode)
        {
            if (syntaxNode is T t && predicate(t))
                matches.Add(t);
        }

        public ISyntaxProvider<T> WithContext(GeneratorExecutionContext context)
        {
            return this;
        }
    }
}
