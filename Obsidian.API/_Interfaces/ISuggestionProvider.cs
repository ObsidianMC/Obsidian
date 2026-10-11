using Obsidian.API.Commands;

namespace Obsidian.API;

/// <summary>
/// Supplies suggestions for a command argument while a player types it. Implement it on a
/// <see cref="BaseSuggestionProviderAttribute"/> for one parameter, or on an argument parser for every parameter of its
/// type; a parameter's attribute takes precedence over its parser.
/// </summary>
/// <remarks>
/// Arguments with a provider ask the server for suggestions instead of using the client's own for their argument type.
/// The command handler keeps the suggestions that start with what has been typed, so a provider may return every
/// candidate. Suggestions are offered only for commands the sender may run.
/// </remarks>
public interface ISuggestionProvider
{
    public ValueTask<IEnumerable<CommandSuggestion>> GetSuggestionsAsync(CommandContext context);
}
