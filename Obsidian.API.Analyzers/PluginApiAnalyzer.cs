using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Obsidian.API.Analyzers;

/// <summary>Checks the reflection contracts used to load plugin modules.</summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class PluginApiAnalyzer : DiagnosticAnalyzer
{
    private const string Api = "Obsidian.API.";

    private static readonly DiagnosticDescriptor EventSignature = Rule(
        "OBSAPI001", "Event handler must accept one event argument",
        "Event handler '{0}' must take exactly one value parameter derived from BaseMinecraftEventArgs",
        "The dispatcher supplies exactly one argument and rejects BaseMinecraftEventArgs itself.");

    private static readonly DiagnosticDescriptor EventReturn = Rule(
        "OBSAPI002", "Event handler asynchronous result is not awaited",
        "Event handler '{0}' must return Task or ValueTask to have its asynchronous work awaited",
        "The event executor awaits only Task and ValueTask, not generic tasks or async void.");

    private static readonly DiagnosticDescriptor CommandReturn = Rule(
        "OBSAPI003", "Command return type is not supported",
        "Command '{0}' must return void, int, Task, ValueTask, Task<int> or ValueTask<int>",
        "Other command results are discarded and other asynchronous return types are not awaited.");

    private static readonly DiagnosticDescriptor RemainingPosition = Rule(
        "OBSAPI004", "Remaining parameter must be last",
        "Parameter '{0}' must be last for Remaining to consume the remaining arguments",
        "Command argument binding only honors Remaining on the last parameter.");

    private static readonly DiagnosticDescriptor InjectionProperty = Rule(
        "OBSAPI005", "Injected property cannot be read",
        "Injected property '{0}' must have a getter and must not be an indexer",
        "Service injection reads the property without index arguments before deciding whether to set it.");

    private static readonly DiagnosticDescriptor ContextProperty = Rule(
        "OBSAPI006", "Command module cannot receive its context",
        "Command module '{0}' must expose a writable, non-indexed CommandContext property accepting CommandContext",
        "The module factory sets the first public property marked CommandContext, including inherited properties.");

    private static readonly DiagnosticDescriptor ModuleConstruction = Rule(
        "OBSAPI007", "Module cannot be constructed",
        "Module '{0}' must be non-abstract, closed and have a public constructor",
        "ActivatorUtilities constructs modules through public constructors. Constructor services may be plugin-defined.");

    private static readonly DiagnosticDescriptor PluginConstruction = Rule(
        "OBSAPI008", "Plugin entry cannot be constructed",
        "Plugin entry '{0}' must be non-abstract, closed and have a public parameterless constructor",
        "The plugin loader uses Activator.CreateInstance rather than constructor injection for the entry type.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(EventSignature, EventReturn, CommandReturn, RemainingPosition,
            InjectionProperty, ContextProperty, ModuleConstruction, PluginConstruction);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(start =>
        {
            var symbols = new ApiSymbols(start.Compilation);
            if (symbols.EventModule is null || symbols.CommandModule is null || symbols.Plugin is null)
                return;

            start.RegisterSymbolAction(c => AnalyzeMethod(c, symbols), SymbolKind.Method);
            start.RegisterSymbolAction(c => AnalyzeProperty(c, symbols), SymbolKind.Property);
            start.RegisterSymbolAction(c => AnalyzeModule(c, symbols), SymbolKind.NamedType);
            start.RegisterCompilationEndAction(c => AnalyzePlugin(c, symbols));
        });
    }

    private static DiagnosticDescriptor Rule(string id, string title, string message, string description) =>
        new(id, title, message, "PluginApi", DiagnosticSeverity.Warning, isEnabledByDefault: true,
            description: description,
            helpLinkUri: "https://github.com/ObsidianMC/Obsidian/blob/26.3.x/docs/plugin-archives.md#plugin-api-analyzers");

    private static void AnalyzeMethod(SymbolAnalysisContext context, ApiSymbols symbols)
    {
        var method = (IMethodSymbol)context.Symbol;
        if (method.MethodKind != MethodKind.Ordinary || method.DeclaredAccessibility != Accessibility.Public)
            return;

        if (DerivesFrom(method.ContainingType, symbols.EventModule) && HasAttribute(method, symbols.EventPriority))
        {
            // Services/EventDispatcher.cs:120-124 validates the event type; :179 supplies one argument.
            if (method.Parameters.Length != 1
                || method.Parameters[0].RefKind != RefKind.None
                || !DerivesFrom(method.Parameters[0].Type, symbols.EventArgs))
            {
                Report(context, EventSignature, method);
            }

            // Events/MinecraftEventExecutor.cs:33-44 only awaits the exact non-generic task types.
            // Synchronous return values may intentionally be discarded, so do not diagnose them.
            if ((method.IsAsync && method.ReturnsVoid) || symbols.IsUnawaitedTask(method.ReturnType))
                Report(context, EventReturn, method);
        }

        if (!symbols.IsCommand(method))
            return;

        // Commands/Framework/CommandExecutor.cs:35-67 and CommandExecutor.Delegate.cs:32-65.
        if (!symbols.IsCommandReturn(method.ReturnType) || (method.IsAsync && method.ReturnsVoid))
            Report(context, CommandReturn, method);

        // Obsidian.API/Commands/Command.cs:150 only honors Remaining on the final parameter.
        foreach (var parameter in method.Parameters.Take(method.Parameters.Length - 1))
        {
            if (HasAttribute(parameter, symbols.Remaining))
                Report(context, RemainingPosition, parameter);
        }
    }

    private static void AnalyzeProperty(SymbolAnalysisContext context, ApiSymbols symbols)
    {
        var property = (IPropertySymbol)context.Symbol;
        // Utilities/Extensions.cs:72-75 excludes static properties and properties holding plugin instances.
        if (property.IsStatic || !HasAttribute(property, symbols.Inject)
            || EqualsType(property.Type, symbols.Plugin) || DerivesFrom(property.Type, symbols.Plugin))
        {
            return;
        }

        // Plugins/ServiceProviders/PluginServiceHandler.cs:22 reads before :29 sets the property.
        // A read-only property may already hold a service, so absence of a setter alone is not an error.
        if (property.GetMethod is null || property.IsIndexer)
            Report(context, InjectionProperty, property);
    }

    private static void AnalyzeModule(SymbolAnalysisContext context, ApiSymbols symbols)
    {
        var type = (INamedTypeSymbol)context.Symbol;
        if (type.TypeKind != TypeKind.Class)
            return;

        var methods = Members(type).OfType<IMethodSymbol>()
            .Where(method => method.DeclaredAccessibility == Accessibility.Public).ToArray();
        var isEvent = DerivesFrom(type, symbols.EventModule)
            && methods.Any(method => HasAttribute(method, symbols.EventPriority));
        var isCommand = symbols.IsCommandModule(type) && methods.Any(symbols.IsCommand);
        if (!isEvent && !isCommand)
            return;

        // Services/EventDispatcher.cs:129 and Commands/Framework/CommandHandler.cs:265,297,310.
        // Do not guess DI registrations or constructor selection: only diagnose definite impossibilities.
        if (type.IsAbstract || IsOpen(type)
            || !type.InstanceConstructors.Any(ctor => ctor.DeclaredAccessibility == Accessibility.Public))
        {
            Report(context, ModuleConstruction, type);
        }

        if (!isCommand || symbols.Context is null)
            return;

        var properties = ReflectedProperties(type)
            .Where(property => property.DeclaredAccessibility == Accessibility.Public
                && HasContextAttribute(property, symbols.ContextAttribute)).ToArray();

        // Commands/CommandModuleFactory.cs:29-32 uses the first public attributed property.
        // Multiple properties have reflection-dependent ordering; do not guess which one will be selected.
        if (properties.Length == 0)
        {
            Report(context, ContextProperty, type);
        }
        else if (properties.Length == 1)
        {
            var property = properties[0];
            var conversion = context.Compilation.ClassifyCommonConversion(symbols.Context, property.Type);
            if (property.SetMethod is null || property.IsIndexer
                || !(conversion.IsIdentity || (conversion.IsImplicit && conversion.IsReference)))
            {
                context.ReportDiagnostic(Diagnostic.Create(ContextProperty,
                    property.Locations.FirstOrDefault(location => location.IsInSource) ?? type.Locations[0], type.Name));
            }
        }
    }

    private static void AnalyzePlugin(CompilationAnalysisContext context, ApiSymbols symbols)
    {
        var entries = Types(context.Compilation.Assembly.GlobalNamespace)
            .Where(type => DerivesFrom(type, symbols.Plugin)).Take(2).ToArray();
        // Plugins/PluginProviders/PluginAssemblyLoader.cs:32-40 picks the first entry in reflection order.
        // Libraries may contain several base/entry types; diagnose only an unambiguous single entry.
        if (entries.Length != 1)
            return;

        var entry = entries[0];
        if (entry.IsAbstract || IsOpen(entry)
            || !entry.InstanceConstructors.Any(ctor => ctor.DeclaredAccessibility == Accessibility.Public
                && ctor.Parameters.Length == 0))
        {
            context.ReportDiagnostic(Diagnostic.Create(PluginConstruction, entry.Locations[0], entry.Name));
        }
    }

    private static IEnumerable<INamedTypeSymbol> Types(INamespaceOrTypeSymbol container)
    {
        foreach (var member in container.GetMembers())
        {
            if (member is INamedTypeSymbol type)
                yield return type;

            if (member is INamespaceOrTypeSymbol nested)
            {
                foreach (var descendant in Types(nested))
                    yield return descendant;
            }
        }
    }

    private static IEnumerable<ISymbol> Members(INamedTypeSymbol type)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            foreach (var member in current.GetMembers())
                yield return member;
        }
    }

    private static IEnumerable<IPropertySymbol> ReflectedProperties(INamedTypeSymbol type)
    {
        var seen = new List<IPropertySymbol>();
        foreach (var property in Members(type).OfType<IPropertySymbol>())
        {
            if (property.DeclaredAccessibility != Accessibility.Public
                || (property.IsStatic && !EqualsType(property.ContainingType, type)))
            {
                continue;
            }

            // Reflection hides properties by name and signature, including the property type.
            var hidden = seen.Any(derived => derived.Name == property.Name
                && EqualsType(derived.Type, property.Type) && derived.IsStatic == property.IsStatic
                && derived.Parameters.Select(parameter => parameter.Type)
                    .SequenceEqual(property.Parameters.Select(parameter => parameter.Type),
                        SymbolEqualityComparer.Default));
            if (!hidden)
            {
                seen.Add(property);
                yield return property;
            }
        }
    }

    private static bool HasContextAttribute(IPropertySymbol property, INamedTypeSymbol? attribute) =>
        HasAttribute(property, attribute)
        || (property.OverriddenProperty is not null && HasContextAttribute(property.OverriddenProperty, attribute));

    private static bool IsOpen(INamedTypeSymbol type) =>
        type.Arity != 0 || (type.ContainingType is not null && IsOpen(type.ContainingType));

    private static bool HasAttribute(ISymbol symbol, INamedTypeSymbol? attribute) =>
        attribute is not null && symbol.GetAttributes().Any(data => EqualsType(data.AttributeClass, attribute));

    private static bool EqualsType(ITypeSymbol? left, ITypeSymbol? right) =>
        left is not null && right is not null && SymbolEqualityComparer.Default.Equals(left, right);

    private static bool DerivesFrom(ITypeSymbol? type, INamedTypeSymbol? parent)
    {
        if (parent is null)
            return false;

        for (var current = type?.BaseType; current is not null; current = current.BaseType)
        {
            if (EqualsType(current, parent))
                return true;
        }

        return false;
    }

    private static void Report(SymbolAnalysisContext context, DiagnosticDescriptor rule, ISymbol symbol) =>
        context.ReportDiagnostic(Diagnostic.Create(rule, symbol.Locations[0], symbol.Name));

    private sealed class ApiSymbols
    {
        private readonly Compilation compilation;

        public ApiSymbols(Compilation compilation)
        {
            this.compilation = compilation;
            EventModule = Find(Api + "MinecraftEventHandler");
            EventArgs = Find(Api + "Events.BaseMinecraftEventArgs");
            EventPriority = Find(Api + "EventPriorityAttribute");
            CommandModule = Find(Api + "Commands.CommandModuleBase");
            Command = Find(Api + "CommandAttribute");
            Overload = Find(Api + "CommandOverloadAttribute");
            Group = Find(Api + "CommandGroupAttribute");
            GroupCommand = Find(Api + "GroupCommandAttribute");
            Remaining = Find(Api + "RemainingAttribute");
            Inject = Find(Api + "Plugins.InjectAttribute");
            Context = Find(Api + "CommandContext");
            ContextAttribute = Find(Api + "CommandContextAttribute");
            Plugin = Find(Api + "Plugins.PluginBase");
        }

        public INamedTypeSymbol? EventModule { get; }
        public INamedTypeSymbol? EventArgs { get; }
        public INamedTypeSymbol? EventPriority { get; }
        public INamedTypeSymbol? CommandModule { get; }
        public INamedTypeSymbol? Command { get; }
        public INamedTypeSymbol? Overload { get; }
        public INamedTypeSymbol? Group { get; }
        public INamedTypeSymbol? GroupCommand { get; }
        public INamedTypeSymbol? Remaining { get; }
        public INamedTypeSymbol? Inject { get; }
        public INamedTypeSymbol? Context { get; }
        public INamedTypeSymbol? ContextAttribute { get; }
        public INamedTypeSymbol? Plugin { get; }

        public bool IsCommandModule(INamedTypeSymbol type) =>
            DerivesFrom(type, CommandModule) || HasAttribute(type, Group);

        public bool IsCommand(IMethodSymbol method)
        {
            if (!IsCommandModule(method.ContainingType))
                return false;

            // Commands/Framework/CommandHandler.cs:257-311 discovers commands, group handlers and named overloads.
            if (HasAttribute(method, GroupCommand) && HasAttribute(method.ContainingType, Group))
                return true;

            if (!HasAttribute(method, Overload))
                return HasAttribute(method, Command);

            return Members(method.ContainingType).OfType<IMethodSymbol>().Any(candidate =>
                candidate.Name == method.Name && candidate.DeclaredAccessibility == Accessibility.Public
                && HasAttribute(candidate, Command) && !HasAttribute(candidate, Overload));
        }

        public bool IsCommandReturn(ITypeSymbol type)
        {
            if (type.SpecialType == SpecialType.System_Void || type.SpecialType == SpecialType.System_Int32)
                return true;

            if (EqualsType(type, Find("System.Threading.Tasks.Task"))
                || EqualsType(type, Find("System.Threading.Tasks.ValueTask")))
            {
                return true;
            }

            return type is INamedTypeSymbol { TypeArguments.Length: 1 } named
                && named.TypeArguments[0].SpecialType == SpecialType.System_Int32
                && (EqualsType(named.OriginalDefinition, Find("System.Threading.Tasks.Task`1"))
                    || EqualsType(named.OriginalDefinition, Find("System.Threading.Tasks.ValueTask`1")));
        }

        public bool IsUnawaitedTask(ITypeSymbol type) =>
            DerivesFrom(type, Find("System.Threading.Tasks.Task"))
            || (type is INamedTypeSymbol named
                && EqualsType(named.OriginalDefinition, Find("System.Threading.Tasks.ValueTask`1")));

        private INamedTypeSymbol? Find(string name) => compilation.GetTypeByMetadataName(name);
    }
}
