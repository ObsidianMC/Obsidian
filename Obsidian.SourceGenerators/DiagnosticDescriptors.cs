using Microsoft.CodeAnalysis;

namespace Obsidian.SourceGenerators
{
    public static class DiagnosticDescriptors
    {
        public static readonly DiagnosticDescriptor NoSerializationMethod = new("DBG001", "This data type doesn't have serialization method associated with it", "This data type doesn't have serialization method associated with it", "SerializationMethodGeneration", DiagnosticSeverity.Warning, true);
        public static readonly DiagnosticDescriptor NoDeserializationMethod = new("DBG002", "This data type doesn't have deserialization method associated with it", "This data type doesn't have deserialization method associated with it", "SerializationMethodGeneration", DiagnosticSeverity.Warning, true);
        public static readonly DiagnosticDescriptor ContainingTypeNotViable = new("DBG003", "This type is not viable for containing members marked with FieldAttribute", "This type is not viable for containing members marked with FieldAttribute", "SerializationMethodGeneration", DiagnosticSeverity.Warning, true);
    }
}
