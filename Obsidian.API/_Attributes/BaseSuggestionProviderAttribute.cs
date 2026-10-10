using Obsidian.API.Commands;

namespace Obsidian.API;

/// <summary>
/// Supplies suggestions for a command parameter while a player types it. Subclass it for suggestions computed at
/// typing time, such as names of things that exist on the server; use <see cref="SuggestionsAttribute"/> for a fixed list.
/// </summary>
/// <remarks>
/// The client asks the server for these suggestions, so they reach players only for parameters of commands they may
/// run. The command handler keeps the suggestions that start with what the player has typed, so a provider may return
/// every candidate.
/// </remarks>
[AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false)]
public abstract class BaseSuggestionProviderAttribute : Attribute
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
