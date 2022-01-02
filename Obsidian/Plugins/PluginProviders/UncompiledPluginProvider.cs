using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.Logging;
using System.IO;

namespace Obsidian.Plugins.PluginProviders;

public class UncompiledPluginProvider : IPluginProvider
{
    private static WeakReference<PortableExecutableReference[]> _metadataReferences = new WeakReference<PortableExecutableReference[]>(null);
    internal static PortableExecutableReference[] MetadataReferences
    {
        get
        {
            if (_metadataReferences.TryGetTarget(out var value) && value != null)
                return value;
            var references = GetPortableExecutableReferences();
            _metadataReferences.SetTarget(references);
            return references;
        }
    }
    internal static CSharpCompilationOptions CompilationOptions { get; set; }

    static UncompiledPluginProvider()
    {
        CompilationOptions = new CSharpCompilationOptions(outputKind: OutputKind.DynamicallyLinkedLibrary,
#if RELEASE
                                                              optimizationLevel: OptimizationLevel.Release);
#elif DEBUG
                                                              optimizationLevel: OptimizationLevel.Debug);
#endif
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
            return new PluginContainer(new PluginInfo(name), name);
        }
        SyntaxTree syntaxTree = CSharpSyntaxTree.ParseText(SourceText.From(fileStream));
        fileStream.Dispose();
        var compilation = CSharpCompilation.Create(name,
                                                   new[] { syntaxTree },
                                                   MetadataReferences,
                                                   CompilationOptions);
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

            return new PluginContainer(new PluginInfo(name), name);
        }
        else
        {
            memoryStream.Seek(0, SeekOrigin.Begin);

            var loadContext = new PluginLoadContext(name + "LoadContext", path);
            var assembly = loadContext.LoadFromStream(memoryStream);
            return PluginProviderSelector.CompiledPluginProvider.HandlePlugin(loadContext, assembly, path, logger);
        }
    }

    private static PortableExecutableReference[] GetPortableExecutableReferences()
    {
        var trustedAssembliesPaths = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")).Split(Path.PathSeparator);
        return trustedAssembliesPaths.Select(reference => MetadataReference.CreateFromFile(reference)).ToArray();
    }
}
