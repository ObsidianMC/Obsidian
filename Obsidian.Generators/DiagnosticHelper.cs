using Microsoft.CodeAnalysis;

#nullable enable

namespace Obsidian.Generators
{
    public static class DiagnosticHelper
    {
        public static void ReportDiagnostic(this GeneratorExecutionContext context, DiagnosticSeverity severity, string text, Location? location = null)
        {
            string id = severity switch
            {
                DiagnosticSeverity.Error => "ERR000",
                DiagnosticSeverity.Warning => "WRN000",
                DiagnosticSeverity.Info => "INF000",
                DiagnosticSeverity.Hidden => "HID000",
                _ => "UKW000"
            };
            var descriptor = new DiagnosticDescriptor(id, text, text, "Generators", severity, true);
            var diagnostic = Diagnostic.Create(descriptor, location ?? Location.None);
            context.ReportDiagnostic(diagnostic);
        }
    }
}
