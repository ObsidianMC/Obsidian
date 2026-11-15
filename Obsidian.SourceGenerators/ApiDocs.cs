using Microsoft.CodeAnalysis.CSharp;
using System.Text;

namespace Obsidian.SourceGenerators;

// Docs for the public types the generators emit into Obsidian.API. Generated members have no docs, so every generated
// file turns CS1591 off, and each generated public top-level type gets one summary, filled from a template below,
// unless a hand-written part of the same type already has one.
internal static class ApiDocs
{
    // Turns CS1591 (a public member without an XML comment) off for the rest of the generated file.
    public const string MissingDocsPragma = "#pragma warning disable CS1591";

    public const string Items = "Every vanilla item, by name.";

    public const string Tags =
        "Vanilla's tags, grouped into a nested class for each registry. Each tag is a property that lists its entries.";

    public const string Codecs =
        "Vanilla's registry entries, such as biomes and dimensions, with one nested class per registry, "
        + "each entry by name.";

    public const string SoundIds =
        "Vanilla's sound names, as strings to pass wherever a sound is played by name, grouped into nested classes "
        + "such as <c>Block</c> and <c>Entity</c>.";

    public const string ContextNumberProviders =
        "Vanilla's context-dependent number providers and predicates, each keyed by its id: <see cref=\"Ints\"/>, "
        + "<see cref=\"Floats\"/> and <see cref=\"Predicates\"/>.";

    // Whether a hand-written part of the type in Obsidian.API already documents it, so the generated part needn't.
    public static bool IsDocumented(Compilation compilation, string metadataName)
    {
        var symbol = compilation.GetTypeByMetadataName(metadataName);

        return symbol is not null && IsDocumented(symbol);
    }

    // Reads the declarations' syntax, not the symbol's documentation, which is empty unless the compilation parses
    // doc comments (GenerateDocumentationFile). Without that a /// comment is a single-line comment, so both count.
    public static bool IsDocumented(INamedTypeSymbol symbol) =>
        symbol.DeclaringSyntaxReferences.Any(reference => HasDocComment(reference.GetSyntax()));

    private static bool HasDocComment(SyntaxNode declaration) =>
        declaration.GetLeadingTrivia().Any(IsDocComment);

    private static bool IsDocComment(SyntaxTrivia trivia)
    {
        if (trivia.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia))
            return true;

        return trivia.IsKind(SyntaxKind.SingleLineCommentTrivia)
            && trivia.ToString().StartsWith("///", StringComparison.Ordinal);
    }

    // The state struct of a block. The name is the block's own, the block name the sanitized one its types use.
    public static string BlockState(string name, string blockName) =>
        $"The state of the {Words(name)} block: its property values, and the state <see cref=\"Id\"/> they map to. "
        + $"Build one with <c>{blockName}StateBuilder</c>.";

    public static string BlockStateBuilder(string stateName) =>
        $"Builds a <see cref=\"{stateName}\"/>: set its properties, then call <see cref=\"Build\"/>.";

    public static string BlockProperty(string name) =>
        $"The values of the {Words(name)} block property. A block state holds one of them.";

    // The registry key is the vanilla name of the registry, such as "entity_type".
    public static string RegistryEnum(string registryKey) =>
        $"Every vanilla {registryKey.Replace('_', ' ')}, by name. Each value is vanilla's protocol id for it.";

    // The parser's full class name, such as "BlockPositionArgumentParser". The suffix is dropped from the words.
    public static string ArgumentParser(string className) =>
        $"Parses the {Words(className.Replace("ArgumentParser", string.Empty))} command argument. "
        + "<see cref=\"Identifier\"/> is the parser name that vanilla's clients use.";

    // Lowercase words of a PascalCase name: "EntityMetadataType" is "entity metadata type".
    private static string Words(string pascalCase)
    {
        var words = new StringBuilder();
        for (var i = 0; i < pascalCase.Length; i++)
        {
            var current = pascalCase[i];

            if (i > 0 && char.IsUpper(current) && StartsWord(pascalCase, i))
                words.Append(' ');

            words.Append(char.ToLowerInvariant(current));
        }

        return words.ToString();
    }

    // A capital starts a word after a lower-case letter or digit, and before a lower-case letter that follows a run of
    // capitals. So "TNTBlock" is "tnt block": the B starts "Block", and the capitals in between stay together.
    private static bool StartsWord(string value, int index) =>
        !char.IsUpper(value[index - 1]) || (index + 1 < value.Length && char.IsLower(value[index + 1]));
}
