namespace Obsidian.SourceGenerators.Packets;

internal sealed class MethodBuildingContext(string streamName, string dataName, Property property, IReadOnlyList<Property> allProperties, CodeBuilder codeBuilder, Method method, MethodsRegistry methodsRegistry, SourceProductionContext context)
{
    public string StreamName { get; } = streamName;
    public string DataName { get; } = dataName;
    public Method Method { get; } = method;
    public CodeBuilder CodeBuilder { get; } = codeBuilder;
    public MethodsRegistry MethodsRegistry { get; } = methodsRegistry;
    public SourceProductionContext GeneratorContext { get; } = context;
    public Property Property { get; } = property;
    public IReadOnlyList<Property> AllProperties { get; } = allProperties;
}
