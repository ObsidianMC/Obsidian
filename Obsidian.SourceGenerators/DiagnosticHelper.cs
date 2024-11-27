#nullable enable

namespace Obsidian.SourceGenerators;

public static class DiagnosticHelper
{
    public static void ReportDiagnostic(this SourceProductionContext context, DiagnosticSeverity severity, string text, SyntaxNode target)
    {
        context.ReportDiagnostic(severity, text, target.GetLocation());
    }

    public static void ReportDiagnostic(this SourceProductionContext context, DiagnosticSeverity severity, string text, Location? location = null)
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

    public static void ReportDiagnostic(this SourceProductionContext context, DiagnosticDescriptor descriptor, Location? location = null)
    {
        var diagnostic = Diagnostic.Create(descriptor, location ?? Location.None);
        context.ReportDiagnostic(diagnostic);
    }
}
