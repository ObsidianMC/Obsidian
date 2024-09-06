using Obsidian.SourceGenerators.Packets.Attributes;

namespace Obsidian.SourceGenerators.Packets;

internal delegate bool PreactionCallback(MethodBuildingContext context);
internal delegate void PostactionCallback(MethodBuildingContext context);

internal sealed class Property : AttributeOwner
{
    public PreactionCallback? Writing;
    public PostactionCallback? Written;
    public PreactionCallback? Reading;
    public PostactionCallback? Read;

    public string Name { get; }
    public string Type { get; private set; }
    public INamedTypeSymbol ContainingType { get; }
    public MemberDeclarationSyntax DeclarationSyntax { get; }
    public bool IsGeneric { get; }
    public bool IsCollection => CollectionType is not null;
    public string? CollectionType { get; }
    public string? Length { get; }
    public int Order { get; }

    private Property(Property property) : base(property.Flags, property.Attributes)
    {
        Type = property.Type;
        DeclarationSyntax = property.DeclarationSyntax;
        CollectionType = property.CollectionType;
        ContainingType = property.ContainingType;
        IsGeneric = property.IsGeneric;
        Length = property.Length;
        Order = property.Order;
        Name = property.Name;
        Writing = property.Writing;
        Written = property.Written;
        Reading = property.Reading;
        Read = property.Read;
    }

    private Property(string name, MemberDeclarationSyntax declaration, INamedTypeSymbol containingType, TypeSyntax type, AttributeBehaviorBase[] attributes)
        : base(AggregateFlags(attributes), attributes)
    {
        Name = name;
        DeclarationSyntax = declaration;
        ContainingType = containingType;
        (Type, CollectionType, Length) = DetermineType(type);
        Order = DetermineOrder(attributes);
        IsGeneric = containingType.TypeParameters.Any(genericType => genericType.Name == Type);
    }

    public Property(FieldDeclarationSyntax field, ISymbol symbol)
        : this(field.Declaration.Variables.First().Identifier.Text, field, symbol.ContainingType, field.Declaration.Type, CollectAttributes(field))
    {
    }

    public Property(PropertyDeclarationSyntax property, ISymbol symbol)
        : this(property.Identifier.Text, property, symbol.ContainingType, property.Type, CollectAttributes(property))
    {
    }

    internal Property(string name, string type, AttributeFlags flags, AttributeBehaviorBase[] attributes)
        : base(flags, attributes)
    {
        Name = name;
        Type = type;
        ContainingType = null!;
        DeclarationSyntax = null!;
    }

    private static (string Type, string? CollectionType, string? Length) DetermineType(TypeSyntax typeSyntax)
    {
        string type = GetRelativeTypeName(typeSyntax.ToString());

        if (type.EndsWith("[]"))
        {
            return (type.Substring(0, type.Length - 2), type, "Length");
        }

        if (type.EndsWith(">"))
        {
            int genericArgumentIndex = type.IndexOf('<') + 1;
            string genericArgument = type.Substring(genericArgumentIndex, type.Length - genericArgumentIndex - 1);
            return (genericArgument, type, "Count");
        }

        return (type, null, null);
    }

    private static AttributeBehaviorBase[] CollectAttributes(MemberDeclarationSyntax declaration)
    {
        IEnumerable<AttributeSyntax> attributes = declaration.AttributeLists.SelectMany(list => list.Attributes);
        return AttributeFactory.ParseValidAttributesSorted(attributes);
    }

    private static int DetermineOrder(AttributeBehaviorBase[] attributes)
    {
        for (int i = 0; i < attributes.Length; i++)
        {
            if (attributes[i] is FieldBehavior field)
            {
                return field.Order;
            }
        }

        return default;
    }

    public string GetNewCollectionExpression(string length)
    {
        if (!IsCollection)
        {
            throw new InvalidOperationException();
        }

        return CollectionType!.EndsWith("[]") ?
            $"new {Type}[{length}]" :
            $"new {CollectionType}({length})";
    }

    public Property Clone()
    {
        return new Property(this);
    }

    public Property CloneWithType(string type)
    {
        Property clone = Clone();
        clone.Type = type;
        return clone;
    }

    public override string ToString()
    {
        return Name;
    }
}
