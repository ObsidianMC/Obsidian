namespace Obsidian.SourceGenerators.Packets;

internal sealed class MethodBuildingContext
{
    public string StreamName { get; }
    public string DataName { get; }
    public Method Method { get; }
    public CodeBuilder CodeBuilder { get; }
    public MethodsRegistry MethodsRegistry { get; }
    public GeneratorExecutionContext GeneratorContext { get; }
    public Property Property { get; }
    public IReadOnlyList<Property> AllProperties { get; }

    public MethodBuildingContext(string streamName, string dataName, Property property, IReadOnlyList<Property> allProperties, CodeBuilder codeBuilder, Method method, MethodsRegistry methodsRegistry, GeneratorExecutionContext context)
    {
        StreamName = streamName;
        DataName = dataName;
        Property = property;
        AllProperties = allProperties;
        CodeBuilder = codeBuilder;
        Method = method;
        MethodsRegistry = methodsRegistry;
        GeneratorContext = context;
    }
}
