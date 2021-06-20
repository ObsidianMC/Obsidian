using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Obsidian.SourceGenerators.Registry
{
    [Generator]
    public sealed class RegistryAssetsGenerator : ISourceGenerator
    {
        public void Initialize(GeneratorInitializationContext context)
        {
        }

        public void Execute(GeneratorExecutionContext context)
        {
            if (!System.Diagnostics.Debugger.IsAttached && false)
            {
                System.Diagnostics.Debugger.Launch();
            }

            try
            {
                DangerousExecute(context);
            }
            catch (Exception e)
            {
                context.ReportDiagnostic(DiagnosticSeverity.Error, $"Exception in {nameof(RegistryAssetsGenerator)}: {e}");
            }
        }

        public void DangerousExecute(GeneratorExecutionContext context)
        {
            var materialsParser = new MaterialsParser();
            materialsParser.ParseMaterials(context);

            foreach (AssetsParser parser in GetParsers())
            {
                string json = context.GetAsset(parser.SourceFile);

                if (json is null)
                {
                    context.ReportDiagnostic(DiagnosticSeverity.Error, $"Asset {parser.SourceFile} not found.");
                    continue;
                }

                parser.ParseAsset(context, json);
            }
        }

        private static IEnumerable<AssetsParser> GetParsers()
        {
            return Assembly
                .GetExecutingAssembly()
                .GetTypes()
                .Where(type => type.BaseType == typeof(AssetsParser))
                .Select(type => Activator.CreateInstance(type))
                .OfType<AssetsParser>();
        }
    }
}
