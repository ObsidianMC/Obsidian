using Obsidian.SourceGenerators.Packets.Attributes;

namespace Obsidian.SourceGenerators.Packets;

internal sealed class MethodsRegistry
{
    public IReadOnlyList<Method> WriteMethods => writeMethods;
    public IReadOnlyList<Method> ReadMethods => readMethods;

    private readonly List<Method> writeMethods = [];
    private readonly List<Method> readMethods = [];

    public bool TryGetWriteMethod(Property property, out Method method)
    {
        return TryGetMethod(writeMethods, property, false, out method);
    }

    public bool TryGetWriteMethod(Property property, bool collection, out Method method)
    {
        return TryGetMethod(writeMethods, property, collection, out method);
    }

    public bool TryGetReadMethod(Property property, out Method method)
    {
        return TryGetMethod(readMethods, property, false, out method);
    }

    public bool TryGetReadMethod(Property property, bool collection, out Method method)
    {
        return TryGetMethod(readMethods, property, collection, out method);
    }

    private bool TryGetMethod(IEnumerable<Method> source, Property property, bool collection, out Method outMethod)
    {
        string? propertyType = collection ? property.CollectionType : property.Type;
        foreach (Method method in source)
        {
            if (method.Type != propertyType)
                continue;

            if (method.Attributes.Any(attribute => !attribute.Matches(property)))
                continue;

            if (property.Attributes.Any(attribute => !attribute.Matches(method)))
                continue;

            outMethod = method;
            return true;
        }

        outMethod = null!;
        return false;
    }

    public void Offer(SourceProductionContext context, MethodDeclarationSyntax method)
    {
        if (method is null)
        {
            throw new ArgumentNullException(nameof(method));
        }

        string name = method.Identifier.Text;
        AttributeSyntax[] attributes = method.AttributeLists.SelectMany(list => list.Attributes).ToArray();
        for (int i = 0; i < attributes.Length; i++)
        {
            string attributeName = attributes[i].Name.ToString();

            if (Vocabulary.AttributeNamesEqual(attributeName, Vocabulary.WriteMethodAttribute))
            {
                if (!TryGetWriteMethodType(context, method, out string type))
                    return;

                var attributeBehaviors = AttributeFactory.ParseValidAttributesSorted(attributes);

                writeMethods.Add(new Method(name, type, attributeBehaviors));
                break;
            }
            else if (Vocabulary.AttributeNamesEqual(attributeName, Vocabulary.ReadMethodAttribute))
            {
                if (!TryGetReadMethodType(context, method, out string type))
                    return;

                var attributeBehaviors = AttributeFactory.ParseValidAttributesSorted(attributes);

                readMethods.Add(new Method(name, type, attributeBehaviors));
                break;
            }
        }
    }

    private bool TryGetWriteMethodType(SourceProductionContext context, MethodDeclarationSyntax method, out string type)
    {
        type = null!;

        var parameters = method.ParameterList.Parameters;
        if (parameters.Count == 0)
        {
            DiagnosticHelper.ReportDiagnostic(context, DiagnosticSeverity.Warning, "Write methods must have one parameter. Method will not register.", method);
            return false;
        }
        if (parameters.Count(param => param.Default != null) > 1)
        {
            DiagnosticHelper.ReportDiagnostic(context, DiagnosticSeverity.Warning, "Write methods must have one parameter. Method will not register.", method);
            return false;
        }

        ParameterSyntax parameter = parameters.First();
        if (parameter.Modifiers.Count > 0)
        {
            DiagnosticHelper.ReportDiagnostic(context, DiagnosticSeverity.Warning, "Parameter can't have modifiers. Method will not register.", parameter);
            return false;
        }

        type = parameter.Type!.ToString();
        return true;
    }

    private bool TryGetReadMethodType(SourceProductionContext context, MethodDeclarationSyntax method, out string type)
    {
        type = method.ReturnType.ToString();

        if (method.ParameterList.Parameters.Count(parameter => parameter.Default is null) > 0)
        {
            DiagnosticHelper.ReportDiagnostic(context, DiagnosticSeverity.Warning, "Read method can't have parameters. Method will not register.", method);
            return false;
        }

        if (type == "void")
        {
            DiagnosticHelper.ReportDiagnostic(context, DiagnosticSeverity.Warning, "Read method return type can't be void. Method will not register.", method);
            return false;
        }

        return true;
    }
}
