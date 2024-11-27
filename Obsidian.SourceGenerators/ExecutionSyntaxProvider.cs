//namespace Obsidian.SourceGenerators;

//public abstract class ExecutionSyntaxProvider<T> : ISyntaxProvider<T> where T : SyntaxNode
//{
//    public GeneratorExecutionContext Context { get; set; }

//    public IEnumerable<T> GetSyntaxNodes()
//    {
//        foreach (var syntaxNode in Context.Compilation.SyntaxTrees)
//        {
//            Context.CancellationToken.ThrowIfCancellationRequested();

//            foreach (var subnode in syntaxNode.GetRoot().DescendantNodesAndSelf().OfType<T>())
//            {
//                if (HandleNode(subnode))
//                    yield return subnode;
//            }
//        }
//    }

//    public ISyntaxProvider<T> WithContext(GeneratorExecutionContext context)
//    {
//        this.Context = context;
//        return this;
//    }

//    public void OnVisitSyntaxNode(SyntaxNode syntaxNode)
//    {
//        return;
//    }

//    protected abstract bool HandleNode(T node);
//}
