using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Obsidian.SourceGenerators.Packets.Attributes;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Obsidian.SourceGenerators.Packets
{
    internal delegate bool PreactionCallback(MethodBuildingContext context);
    internal delegate void PostactionCallback(MethodBuildingContext context);

    internal sealed class Property : AttributeOwner
    {
        public PreactionCallback Writing;
        public PostactionCallback Written;
        public PreactionCallback Reading;
        public PostactionCallback Read;

        public string Name { get; private set; }
        public string Type { get; set; }
        public INamedTypeSymbol ContainingType { get; set; }
        public MemberDeclarationSyntax DeclarationSyntax { get; set; }
        public bool IsGeneric { get; private set; }
        public bool IsCollection { get; set; }
        public string CollectionType { get; set; }
        public string Length { get; set; }
        public int Order { get; set; }

        public Property()
        {
        }

        public Property(FieldDeclarationSyntax field, ISymbol symbol)
        {
            Name = field.Declaration.Variables.First().Identifier.Text;
            DeclarationSyntax = field;
            ContainingType = symbol.ContainingType;
            SetType(field.Declaration.Type);
            IsGeneric = ContainingType.TypeParameters.Any(genericType => genericType.Name == Type);
            SetAttributes(field.AttributeLists.SelectMany(list => list.Attributes));
        }

        public Property(PropertyDeclarationSyntax property, ISymbol symbol)
        {
            Name = property.Identifier.Text;
            DeclarationSyntax = property;
            ContainingType = symbol.ContainingType;
            SetType(property.Type);
            IsGeneric = ContainingType.TypeParameters.Any(genericType => genericType.Name == Type);
            SetAttributes(property.AttributeLists.SelectMany(list => list.Attributes));
        }

        private void SetType(TypeSyntax typeSyntax)
        {
            Type = typeSyntax.ToString();
            if (Type.Contains('.'))
            {
                Type = Type.Substring(Type.IndexOf('.') + 1);
            }
            if (Type.EndsWith("[]"))
            {
                CollectionType = Type;
                Type = Type.Substring(0, Type.Length - 2);
                IsCollection = true;
                Length = "Length";
            }
            else if (Type.EndsWith(">"))
            {
                CollectionType = Type;
                int typeStart = Type.IndexOf('<') + 1;
                Type = Type.Substring(typeStart, Type.Length - typeStart - 1);
                IsCollection = true;
                Length = "Count";
            }
        }

        private void SetAttributes(IEnumerable<AttributeSyntax> attributes)
        {
            Attributes = AttributeFactory.ParseValidAttributesSorted(attributes);

            Flags = AttributeFlags.None;
            for (int i = 0; i < Attributes.Length; i++)
            {
                Flags |= Attributes[i].Flag;

                if (Attributes[i] is FieldBehavior field)
                {
                    Order = field.Order;
                }
            }
        }

        public string NewCollection(string length)
        {
            if (!IsCollection)
                throw new InvalidOperationException();

            return CollectionType.EndsWith("[]") ?
                $"new {Type}[{length}]" :
                $"new {CollectionType}({length})";
        }

        public Property Clone()
        {
            return new Property
            {
                Type = Type,
                DeclarationSyntax = DeclarationSyntax,
                CollectionType = CollectionType,
                ContainingType = ContainingType,
                IsCollection = IsCollection,
                IsGeneric = IsGeneric,
                Length = Length,
                Order = Order,
                Attributes = Attributes,
                Flags = Flags,
                Name = Name
            };
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
}
