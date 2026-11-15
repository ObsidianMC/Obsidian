#nullable enable

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Obsidian.API.Analyzers;
using Obsidian.Tests.Utilities;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Xunit;

namespace Obsidian.Tests;

public sealed class PluginApiAnalyzerTests
{
    private const string Marker = "/*here*/";
    private const string Imports = """
        using System;
        using System.Threading.Tasks;
        using Obsidian.API;
        using Obsidian.API.Commands;
        using Obsidian.API.Events;
        using Obsidian.API.Plugins;

        """;

    private static readonly CSharpParseOptions ParseOptions = new(LanguageVersion.Preview);
    private static readonly ImmutableArray<MetadataReference> References = GetReferences();
    private static readonly string[] ContractTypes = ["MinecraftEventHandler", "CommandContext", "Plugins.PluginBase"];

    private sealed record Case(string Name, string Source, string? Id = null);

    [Fact]
    public async Task ReflectionContracts_ReportOnlyConfirmedMistakes()
    {
        var cases = new List<Case>
        {
            new("event missing argument", Event("[EventPriority] public void /*here*/Run() { }"), "OBSAPI001"),
            new("event wrong argument", Event("[EventPriority] public void /*here*/Run(int value) { }"), "OBSAPI001"),
            new("event base argument", Event("[EventPriority] public void /*here*/Run(BaseMinecraftEventArgs e) { }"),
                "OBSAPI001"),
            new("event extra argument", Event("[EventPriority] public void /*here*/Run(PlayerJoinEventArgs e, int n) { }"),
                "OBSAPI001"),
            new("event by reference", Event("[EventPriority] public void /*here*/Run(ref PlayerJoinEventArgs e) { }"),
                "OBSAPI001"),
            new("event synchronous", Event("[EventPriority] public void Run(PlayerJoinEventArgs e) { }")),
            new("event generic task", Event("""
                [EventPriority]
                public Task<int> /*here*/Run(PlayerJoinEventArgs e) => Task.FromResult(1);
                """), "OBSAPI002"),
            new("event generic value task", Event("""
                [EventPriority]
                public ValueTask<int> /*here*/Run(PlayerJoinEventArgs e) => new(1);
                """), "OBSAPI002"),
            new("event async void", Event("""
                [EventPriority]
                public async void /*here*/Run(PlayerJoinEventArgs e) => await Task.Yield();
                """), "OBSAPI002"),
            new("event task", Event("[EventPriority] public Task Run(PlayerJoinEventArgs e) => Task.CompletedTask;")),
            new("event value task", Event("[EventPriority] public ValueTask Run(PlayerJoinEventArgs e) => default;")),
            new("event discarded synchronous value", Event("[EventPriority] public int Run(PlayerJoinEventArgs e) => 1;")),
            new("command unsupported", Command("[Command(\"run\")] public string /*here*/Run() => \"ignored\";"),
                "OBSAPI003"),
            new("command generic task", Command("""
                [Command("run")]
                public Task<string> /*here*/Run() => Task.FromResult("ignored");
                """), "OBSAPI003"),
            new("command async void", Command("""
                [Command("run")]
                public async void /*here*/Run() => await Task.Yield();
                """), "OBSAPI003"),
            new("remaining misplaced", Command("""
                [Command("run")]
                public void Run([Remaining] string /*here*/text, int count) { }
                """), "OBSAPI004"),
            new("remaining last", Command("""
                [Command("run")]
                public void Run(int count, [Remaining] string text) { }
                """)),
            new("custom parser allowed", Command("""
                [Command("run")]
                public void Run(DateTime customParsedArgument) { }
                """)),
            new("injection write only", """
                public class Probe
                {
                    [Inject]
                    public object /*here*/Service { set { } }
                }
                """, "OBSAPI005"),
            new("injection indexed", """
                public class Probe
                {
                    [Inject]
                    public object /*here*/this[int index]
                    {
                        get => null;
                        set { }
                    }
                }
                """, "OBSAPI005"),
            new("injection private setter", """
                public class Probe
                {
                    [Inject]
                    public object Service { get; private set; }
                }
                """),
            new("injection private property", """
                public class Probe
                {
                    [Inject]
                    private object Service { get; set; }
                }
                """),
            new("injection already initialized", """
                public class Probe
                {
                    [Inject]
                    public object Service { get; } = new object();
                }
                """),
            new("context read only", Command("""
                [CommandContext]
                public new CommandContext /*here*/CommandContext => null;
                [Command("run")]
                public void Run() { }
                """), "OBSAPI006"),
            new("context incompatible", Group("""
                [CommandContext]
                public string /*here*/Context { get; set; }
                [GroupCommand]
                public void Run() { }
                """), "OBSAPI006"),
            new("context absent", Group("[GroupCommand] public void Run() { }", "/*here*/Probe"), "OBSAPI006"),
            new("context object private setter", Group("""
                [CommandContext]
                public object Context { get; private set; }
                [GroupCommand]
                public void Run() { }
                """)),
            new("context inherited", Command("[Command(\"run\")] public void Run() { }")),
            new("context different-type hiding", Command("""
                public new string CommandContext { get; set; }
                [Command("run")]
                public void Run() { }
                """)),
            new("context override attribute inherited", """
                public class ContextBase
                {
                    [CommandContext]
                    public virtual CommandContext Context { get; set; }
                }
                [CommandGroup("group")]
                public class Probe : ContextBase
                {
                    public override CommandContext Context { get; set; }
                    [GroupCommand]
                    public void Run() { }
                }
                """),
            new("module private constructor", Event("""
                private Probe() { }
                [EventPriority]
                public void Run(PlayerJoinEventArgs e) { }
                """, "/*here*/Probe"), "OBSAPI007"),
            new("module injected constructor", Event("""
                public Probe(IServiceProvider services) { }
                [EventPriority]
                public void Run(PlayerJoinEventArgs e) { }
                """)),
            new("module abstract", """
                public abstract class /*here*/Probe : MinecraftEventHandler
                {
                    public Probe() { }
                    [EventPriority]
                    public void Run(PlayerJoinEventArgs e) { }
                }
                """, "OBSAPI007"),
            new("module generic", Event("""
                [EventPriority]
                public void Run(PlayerJoinEventArgs e) { }
                """, "/*here*/Probe<T>"), "OBSAPI007"),
            new("plugin constructor", """
                public class /*here*/Probe : PluginBase
                {
                    public Probe(int value) { }
                }
                """, "OBSAPI008"),
            new("plugin implicit constructor", "public class Probe : PluginBase { }"),
            new("plugin abstract", """
                public abstract class /*here*/Probe : PluginBase
                {
                    public Probe() { }
                }
                """, "OBSAPI008"),
            new("plugin generic", "public class /*here*/Probe<T> : PluginBase { }", "OBSAPI008"),
            new("multiple plugin types left to runtime", """
                public abstract class PluginHelper : PluginBase { }
                public class Probe : PluginHelper { }
                """),
            new("unrelated attributes", """
                public class Probe
                {
                    [EventPriority]
                    [Command("unused")]
                    public string Run([Remaining] string text, int count) => text;
                }
                """),
            new("nonpublic event ignored", Event("[EventPriority] private void Run() { }")),
            new("orphan overload ignored", Command("[CommandOverload] public string Run() => \"unused\";")),
            new("same-named foreign attribute ignored", """
                namespace Other
                {
                    public class EventPriorityAttribute : Attribute { }
                    public class Probe : MinecraftEventHandler
                    {
                        [Other.EventPriority]
                        public void Run() { }
                    }
                }
                """)
        };

        foreach (var (returnType, expression) in new[]
        {
            ("void", "{ }"),
            ("int", "=> 1;"),
            ("Task", "=> Task.CompletedTask;"),
            ("ValueTask", "=> default;"),
            ("Task<int>", "=> Task.FromResult(1);"),
            ("ValueTask<int>", "=> new(1);")
        })
        {
            cases.Add(new Case($"command supported {returnType}",
                Command($"[Command(\"run\")] public {returnType} Run() {expression}")));
        }

        cases.Add(new Case("named overload", Command("""
            [Command("run")]
            public void Run() { }
            [CommandOverload]
            public string /*here*/Run(int n) => "ignored";
            """), "OBSAPI003"));
        cases.Add(new Case("group handler", Group("""
            [CommandContext]
            public CommandContext Context { get; set; }
            [GroupCommand]
            public string /*here*/Run() => "ignored";
            """), "OBSAPI003"));

        // Every case is checked even if an earlier case failed, with one assertion listing all mismatches.
        await FixtureCases.AssertAllAsync(cases, test => test.Name, async test =>
        {
            var source = Imports + test.Source;
            var compilation = Compile(test.Name, [CSharpSyntaxTree.ParseText(source, ParseOptions)]);
            var errors = compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error);
            Assert.Empty(errors);

            var diagnostics = await Analyze(compilation);
            if (test.Id is null)
            {
                Assert.Empty(diagnostics);
                return;
            }

            var diagnostic = Assert.Single(diagnostics);
            Assert.Equal(test.Id, diagnostic.Id);
            Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
            var expectedOffset = source.IndexOf(Marker, StringComparison.Ordinal) + Marker.Length;
            Assert.True(expectedOffset >= Marker.Length, "Bad case must mark the expected diagnostic location.");
            Assert.Equal(expectedOffset, diagnostic.Location.SourceSpan.Start);
        });
    }

    [Fact]
    public async Task EngineSampleAndServerModules_ProduceNoPluginApiDiagnostics()
    {
        var root = RepositoryRoot();
        var sample = Path.Combine(root, "Obsidian.Tests.EngineSample");
        var server = Path.Combine(root, "Obsidian");
        var projects = new[]
        {
            (Name: "EngineSample", Files: SourceFiles(sample).ToArray(),
                Usings: "global using System; global using System.Threading.Tasks;"),
            (Name: "ServerModules", Files: SourceFiles(Path.Combine(server, "Commands", "Modules"))
                .Concat(Directory.GetFiles(Path.Combine(server, "Events"), "*EventHandler*.cs"))
                .Append(Path.Combine(server, "GlobalUsings.cs")).ToArray(), Usings: "")
        };

        await FixtureCases.AssertAllAsync(projects, project => project.Name, async project =>
        {
            Assert.NotEmpty(project.Files);
            var trees = project.Files.Select(file => CSharpSyntaxTree.ParseText(
                File.ReadAllText(file), ParseOptions, file)).ToList();
            trees.Add(CSharpSyntaxTree.ParseText(project.Usings, ParseOptions));
            var compilation = Compile(project.Name, trees);

            // Analyze the real declarations against the built assemblies. Bodies can refer to generated or internal
            // members, but API attributes and contract types must resolve so this smoke check cannot pass vacuously.
            foreach (var type in ContractTypes)
                Assert.NotNull(compilation.GetTypeByMetadataName("Obsidian.API." + type));

            var types = trees.SelectMany(tree => tree.GetRoot().DescendantNodes()
                .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax>()
                .Select(node => compilation.GetSemanticModel(tree).GetDeclaredSymbol(node)))
                .OfType<INamedTypeSymbol>().ToArray();
            Assert.Contains(types, type => type.BaseType?.ContainingAssembly.Name == "Obsidian.API");

            var attributed = types.SelectMany(type => type.GetMembers())
                .Where(member => member.GetAttributes().Any(attribute =>
                    attribute.AttributeClass?.ContainingAssembly.Name == "Obsidian.API")).ToArray();
            if (project.Name == "ServerModules")
            {
                Assert.NotEmpty(attributed);
            }
            else
            {
                // The sample uses delegate registrations, but its actual PluginBase entry is still checked.
                Assert.Contains(types, type => type.BaseType?.ToDisplayString() == "Obsidian.API.Plugins.PluginBase");
            }
            foreach (var method in attributed.OfType<IMethodSymbol>())
            {
                Assert.NotEqual(TypeKind.Error, method.ReturnType.TypeKind);
                Assert.All(method.Parameters, parameter => Assert.NotEqual(TypeKind.Error, parameter.Type.TypeKind));
            }

            Assert.Empty(await Analyze(compilation));
        });
    }

    private static CSharpCompilation Compile(string name, IEnumerable<SyntaxTree> trees) =>
        CSharpCompilation.Create(name, trees, References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

    private static Task<ImmutableArray<Diagnostic>> Analyze(CSharpCompilation compilation) =>
        compilation.WithAnalyzers([new PluginApiAnalyzer()]).GetAnalyzerDiagnosticsAsync();

    private static ImmutableArray<MetadataReference> GetReferences()
    {
        var framework = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator);
        var assemblies = framework.Concat(Directory.GetFiles(AppContext.BaseDirectory, "*.dll"))
            .DistinctBy(Path.GetFileNameWithoutExtension);
        return [.. assemblies.Select(path => MetadataReference.CreateFromFile(path))];
    }

    private static IEnumerable<string> SourceFiles(string folder) =>
        Directory.EnumerateFiles(folder, "*.cs", SearchOption.AllDirectories)
            .Where(file => !Path.GetRelativePath(folder, file).Split(Path.DirectorySeparatorChar)
                .Any(part => part is "bin" or "obj" or "Generated"));

    private static string RepositoryRoot([CallerFilePath] string file = "") =>
        Path.GetDirectoryName(Path.GetDirectoryName(file))!;

    private static string Event(string members, string name = "Probe") => $$"""
        public class {{name}} : MinecraftEventHandler
        {
            {{members}}
        }
        """;

    private static string Command(string members) => $$"""
        public class Probe : CommandModuleBase
        {
            {{members}}
        }
        """;

    private static string Group(string members, string name = "Probe") => $$"""
        [CommandGroup("group")]
        public class {{name}}
        {
            {{members}}
        }
        """;
}
