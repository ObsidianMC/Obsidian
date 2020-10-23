using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Linq;

namespace Obsidian.Plugins.PluginProviders
{
    public class UncompiledPluginProvider : IPluginProvider
    {
        internal static PortableExecutableReference[] metadataReferences;
        internal static CSharpCompilationOptions compilationOptions;
        
        static UncompiledPluginProvider()
        {
            var trustedAssembliesPaths = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")).Split(Path.PathSeparator);
            metadataReferences = trustedAssembliesPaths.Select(reference => MetadataReference.CreateFromFile(reference)).ToArray();
            compilationOptions = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Release);
        }

        public UncompiledPluginProvider()
        {
        }

        public PluginContainer GetPlugin(string path, ILogger logger)
        {
            string name = Path.GetFileNameWithoutExtension(path);

            FileStream fileStream;
            try
            {
                fileStream = File.OpenRead(path);
            }
            catch
            {
                logger.LogError($"Reloading '{Path.GetFileName(path)}' failed, file is not accessible.");
                return new PluginContainer(null, new PluginInfo(name), null, null, name);
            }
            SyntaxTree syntaxTree = CSharpSyntaxTree.ParseText(SourceText.From(fileStream));
            fileStream.Close();
            var compilation = CSharpCompilation.Create(name,
                                                       new[] { syntaxTree },
                                                       metadataReferences,
                                                       compilationOptions);
            using var memoryStream = new MemoryStream();
            EmitResult emitResult = compilation.Emit(memoryStream);
            
            if (!emitResult.Success)
            {
                if (logger != null)
                {
                    foreach (var diagnostic in emitResult.Diagnostics)
                    {
                        if (diagnostic.Severity != DiagnosticSeverity.Error || diagnostic.IsWarningAsError)
                            continue;

                        logger.LogError($"Compilation failed: {diagnostic.Location} {diagnostic.GetMessage()}");
                    }
                }

                return new PluginContainer(null, new PluginInfo(name), null, null, name);
            }
            else
            {
                memoryStream.Seek(0, SeekOrigin.Begin);

                var loadContext = new PluginLoadContext(name + "LoadContext", path);
                var assembly = loadContext.LoadFromStream(memoryStream);
                return PluginProviderSelector.CompiledPluginProvider.HandlePlugin(loadContext, assembly, path, logger);
            }
        }
    }
}
