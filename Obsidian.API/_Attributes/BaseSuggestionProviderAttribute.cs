using Obsidian.API.Commands;

namespace Obsidian.API;

/// <summary>
/// Supplies suggestions for one command parameter. Subclass it for suggestions computed at typing time, such as names
/// of things that exist on the server; use <see cref="SuggestionsAttribute"/> for a fixed list.
/// </summary>
/// <remarks>See <see cref="ISuggestionProvider"/> for how suggestions are offered.</remarks>
[AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false)]
public abstract class BaseSuggestionProviderAttribute : Attribute, ISuggestionProvider
{
    public abstract ValueTask<IEnumerable<CommandSuggestion>> GetSuggestionsAsync(CommandContext context);
}

/// <summary>Suggests a fixed set of values for a command parameter.</summary>
/// <example><c>public Task SetTime([Suggestions("day", "night")] string value)</c></example>
public sealed class SuggestionsAttribute(params string[] values) : BaseSuggestionProviderAttribute
{
    public IReadOnlyList<string> Values { get; } = values;

    public override ValueTask<IEnumerable<CommandSuggestion>> GetSuggestionsAsync(CommandContext context) =>
        ValueTask.FromResult(this.Values.Select(value => new CommandSuggestion(value)));
}
