using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Obsidian.SourceGenerators.Registry;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Xunit;

namespace Obsidian.Tests;

/// <summary>
/// Runs <see cref="LootRegistryGenerator"/> on small synthetic inputs and checks the build diagnostics it reports (OBSLT001–004).
/// The generator only runs for a compilation named Obsidian.API, and finds loot model classes by their <c>[LootType]</c>.
/// </summary>
public class LootGeneratorDiagnostics
{
    // The minimal loot model the generator emits against: a table with pools, entries with functions, and two tagged types.
    private const string Model = """
        using System;
        namespace Obsidian.API.Loot
        {
            [AttributeUsage(AttributeTargets.Class)]
            public sealed class LootTypeAttribute(string resourceLocation) : Attribute { public string ResourceLocation { get; } = resourceLocation; }

            public sealed class LootTable
            {
                public string Identifier { get; set; } = "";
                public string Type { get; set; } = "";
                public LootPool[] Pools { get; set; } = [];
            }

            public sealed class LootPool
            {
                public float Rolls { get; set; }
                public LootEntry[] Entries { get; set; } = [];
            }

            public abstract class LootEntry
            {
                public Obsidian.API.Loot.Functions.LootFunction[] Functions { get; set; } = [];
            }

            [LootType("minecraft:item")]
            public sealed class ItemEntry : LootEntry
            {
                public Obsidian.API.Inventory.Item? Name { get; set; }
            }

        }
        namespace Obsidian.API.Inventory
        {
            public sealed class Item { }
        }
        namespace Obsidian.API.Loot.Functions
        {
            public abstract class LootFunction { }

            [Obsidian.API.Loot.LootType("minecraft:set_count")]
            public sealed class SetCountFunction : LootFunction
            {
                public float Count { get; set; }
            }
        }
        """;

    private const string EmptyRegistry = "{}";

    // A block table with one pool holding one entry.
    private static string Table(string entry) =>
        $$"""{ "pools": [ { "rolls": 1, "entries": [ {{entry}} ] } ] }""";

    // The blocks category file: table ids (minecraft:blocks/<name>) mapped to their tables, as datagen writes it.
    private static string Category(params (string Id, string Table)[] tables) =>
        "{ " + string.Join(", ", tables.Select(table => $"\"{table.Id}\": {table.Table}")) + " }";

    private static List<string> Diagnostics(string blocksCategory, string items = EmptyRegistry)
    {
        var compilation = CSharpCompilation.Create("Obsidian.API",
            [CSharpSyntaxTree.ParseText(Model)],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var texts = new List<AdditionalText>
        {
            new InMemoryText("enchantments", EmptyRegistry),
            new InMemoryText("instruments", EmptyRegistry),
            new InMemoryText("tags", EmptyRegistry),
            new InMemoryText("items", items),
            new InMemoryText("loot_table/blocks", blocksCategory),
        };

        var driver = CSharpGeneratorDriver.Create(
            ImmutableArray.Create(new LootRegistryGenerator().AsSourceGenerator()),
            additionalTexts: texts.ToImmutableArray());

        var result = driver.RunGenerators(compilation).GetRunResult();
        return result.Diagnostics.Select(diagnostic => diagnostic.Id).ToList();
    }

    [Fact]
    public void UnknownLootTypeIsReported()
    {
        var ids = Diagnostics(Category(("minecraft:blocks/test", Table("""{ "type": "minecraft:item", "name": "minecraft:stone", "functions": [ { "function": "minecraft:not_a_function" } ] }""") )),
            items: """{ "minecraft:stone": { "protocol_id": 1 } }""");

        Assert.Contains("OBSLT001", ids);
    }

    [Fact]
    public void UnknownPropertyIsReported()
    {
        var ids = Diagnostics(Category(("minecraft:blocks/test", Table("""{ "type": "minecraft:item", "name": "minecraft:stone", "functions": [ { "function": "minecraft:set_count", "count": 2, "not_a_key": 1 } ] }""") )),
            items: """{ "minecraft:stone": { "protocol_id": 1 } }""");

        Assert.Contains("OBSLT002", ids);
    }

    [Fact]
    public void UnknownItemReferenceIsReported()
    {
        var ids = Diagnostics(Category(("minecraft:blocks/test", Table("""{ "type": "minecraft:item", "name": "minecraft:not_an_item" }""") )),
            items: """{ "minecraft:stone": { "protocol_id": 1 } }""");

        Assert.Contains("OBSLT003", ids);
    }

    [Fact]
    public void DuplicateMemberNameIsReported()
    {
        // Both ids turn into the member name StoneSlab.
        var ids = Diagnostics(Category(
            ("minecraft:blocks/stone_slab", Table("""{ "type": "minecraft:item", "name": "minecraft:stone" }""")),
            ("minecraft:blocks/stone-slab", Table("""{ "type": "minecraft:item", "name": "minecraft:stone" }""")) ),
            items: """{ "minecraft:stone": { "protocol_id": 1 } }""");

        Assert.Contains("OBSLT004", ids);
    }

    [Fact]
    public void ValidTableReportsNoDiagnostics()
    {
        var ids = Diagnostics(Category(("minecraft:blocks/test", Table("""{ "type": "minecraft:item", "name": "minecraft:stone", "functions": [ { "function": "minecraft:set_count", "count": 2 } ] }""") )),
            items: """{ "minecraft:stone": { "protocol_id": 1 } }""");

        Assert.DoesNotContain(ids, id => id.StartsWith("OBSLT", System.StringComparison.Ordinal));
    }

    private sealed class InMemoryText(string name, string text) : AdditionalText
    {
        // The generator reads assets by their path under Assets/, e.g. Assets/loot_table/blocks.json.
        public override string Path => "/src/Assets/" + name + ".json";

        public override SourceText GetText(CancellationToken cancellationToken = default) => SourceText.From(text);
    }
}
