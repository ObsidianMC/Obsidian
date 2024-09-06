using System.Linq.Expressions;
using System.Reflection;

namespace Obsidian.SourceGenerators.Packets.Attributes;

internal static class AttributeFactory
{
    private delegate AttributeBehaviorBase FactoryMethod(AttributeSyntax syntax);

    private static Dictionary<string, FactoryMethod> methods = GetFactoryMethods();

    public static bool TryParse(AttributeSyntax attribute, out AttributeBehaviorBase attributeBehaviorBase)
    {
        string attributeName = attribute.Name.ToString();
        if (attributeName.EndsWith("Attribute"))
            attributeName = attributeName.Substring(0, attributeName.Length - 9);

        attributeBehaviorBase = methods.TryGetValue(attributeName, out FactoryMethod factoryMethod) ? factoryMethod(attribute) : null!;

        return attributeBehaviorBase is not null;
    }

    public static AttributeBehaviorBase[] ParseValidAttributesSorted(IEnumerable<AttributeSyntax> attributes)
    {
        var result = new List<AttributeBehaviorBase>();

        foreach (AttributeSyntax attribute in attributes)
        {
            if (TryParse(attribute, out var attributeBehavior))
                result.Add(attributeBehavior);
        }

        result.Sort((a, b) => a.Flag.CompareTo(b.Flag));
        return result.ToArray();
    }

    private static Dictionary<string, FactoryMethod> GetFactoryMethods()
    {
        var factoryMethods = new Dictionary<string, FactoryMethod>();
        var attributeTypes = Assembly
            .GetExecutingAssembly()
            .GetTypes()
            .Where(type => type.BaseType == typeof(AttributeBehaviorBase));

        foreach (Type type in attributeTypes)
        {
            string attributeName = type.Name.Substring(0, type.Name.Length - 8);
            FactoryMethod method = GetFactoryMethod(type);
            factoryMethods.Add(attributeName, method);
        }

        return factoryMethods;
    }

    private static FactoryMethod GetFactoryMethod(this Type type)
    {
        var parameter = Expression.Parameter(typeof(AttributeSyntax));
        var ctor = type.GetConstructor([typeof(AttributeSyntax)]);
        var lambda = Expression.Lambda<FactoryMethod>(Expression.New(ctor, parameter), parameter);
        return lambda.Compile();
    }
}
