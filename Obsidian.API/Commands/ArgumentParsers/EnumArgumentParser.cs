using Obsidian.API.Utilities;
using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;

namespace Obsidian.API.Commands.ArgumentParsers;

/// <summary>
/// Reads a member of an enum by name. The command handler makes one for any enum parameter type without a registered
/// parser, so enum parameters need no setup and suggest their members while a player types.
/// </summary>
/// <remarks>
/// Members are suggested in snake_case, as vanilla names its values (<c>GameMode.Creative</c> is <c>creative</c>), and
/// read in snake_case or as their C# name, ignoring case. Numbers aren't accepted.
/// </remarks>
[ArgumentParser("brigadier:string")]
public sealed partial class EnumArgumentParser : BaseArgumentParser, ISuggestionProvider
{
    private readonly FrozenDictionary<string, object> valuesByName;
    private readonly CommandSuggestion[] suggestions;

    public Type EnumType { get; }

    public EnumArgumentParser(Type enumType)
    {
        if (!enumType.IsEnum)
            throw new ArgumentException($"{enumType} is not an enum.", nameof(enumType));

        this.EnumType = enumType;

        var names = Enum.GetNames(enumType);
        var values = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        // Keyed without underscores so that snake_case and C# names both match.
        foreach (var name in names)
            values.TryAdd(RemoveUnderscores(name), Enum.Parse(enumType, name));

        this.valuesByName = values.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        this.suggestions = [.. names.Select(name => new CommandSuggestion(name.ToSnakeCase()))];
    }

    public ValueTask<IEnumerable<CommandSuggestion>> GetSuggestionsAsync(CommandContext context) =>
        ValueTask.FromResult<IEnumerable<CommandSuggestion>>(this.suggestions);

    public override void Write(INetStreamWriter writer)
    {
        base.Write(writer);

        // Member names are single words.
        writer.WriteVarInt((int)StringType.SingleWord);
    }

    internal override bool TryParseArgument(string input, CommandContext ctx, [NotNullWhen(true)] out object? result) =>
        this.valuesByName.TryGetValue(RemoveUnderscores(input), out result);

    private static string RemoveUnderscores(string name) => name.Replace("_", string.Empty);
}
