using Microsoft.CodeAnalysis;
using System.Collections.Generic;

namespace Obsidian.SourceGenerators;

public interface ISyntaxProvider<out T> : ISyntaxReceiver where T : SyntaxNode
{
    public GeneratorExecutionContext Context { get; set; }
    public IEnumerable<T> GetSyntaxNodes();
    public ISyntaxProvider<T> WithContext(GeneratorExecutionContext context);
}
