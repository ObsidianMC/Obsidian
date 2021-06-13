using Microsoft.CodeAnalysis;

namespace Obsidian.SourceGenerators.Packets
{
    internal sealed class MethodBuildingContext
    {
        public string StreamName { get; }
        public string DataName { get; }
        public Method Method { get; }
        public CodeBuilder CodeBuilder { get; }
        public MethodsRegistry MethodsRegistry { get; }
        public GeneratorExecutionContext GeneratorContext { get; }
        public Property Property { get; }

        public MethodBuildingContext(string streamName, string dataName, Property property, CodeBuilder codeBuilder, Method method, MethodsRegistry methodsRegistry, GeneratorExecutionContext context)
        {
            StreamName = streamName;
            CodeBuilder = codeBuilder;
            MethodsRegistry = methodsRegistry;
            GeneratorContext = context;
            DataName = dataName;
            Method = method;
            Property = property;
        }
    }
}
