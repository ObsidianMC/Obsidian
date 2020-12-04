using Microsoft.CodeAnalysis;

namespace Obsidian.Generators
{
    public static class DiagnosticDescriptors
    {
        public static readonly DiagnosticDescriptor NoSerializationMethod = new DiagnosticDescriptor("DBG001", "This data type doesn't have serialization method associated with it", "This data type doesn't have serialization method associated with it", "SerializationMethodGeneration", DiagnosticSeverity.Warning, true);
        public static readonly DiagnosticDescriptor NoDeserializationMethod = new DiagnosticDescriptor("DBG002", "This data type doesn't have deserialization method associated with it", "This data type doesn't have deserialization method associated with it", "SerializationMethodGeneration", DiagnosticSeverity.Warning, true);
        public static readonly DiagnosticDescriptor ContainingTypeNotViable = new DiagnosticDescriptor("DBG003", "This type is not viable for containing members marked with FieldAttribute", "This type is not viable for containing members marked with FieldAttribute", "SerializationMethodGeneration", DiagnosticSeverity.Warning, true);

        public static Diagnostic Create(DiagnosticSeverity severity, string message, SyntaxNode source = null)
        {
            return Diagnostic.Create(new DiagnosticDescriptor("DBG000", message, message, "GeneratorDiagnostic", severity, true), source?.GetLocation());
        }
    }
}
