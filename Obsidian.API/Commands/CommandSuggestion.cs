namespace Obsidian.API.Commands;

/// <summary>A value offered to a player while they type a command argument.</summary>
/// <param name="Text">The text that replaces the word being typed.</param>
/// <param name="Tooltip">Shown when the player hovers the suggestion.</param>
public readonly record struct CommandSuggestion(string Text, ChatMessage? Tooltip = null);

/// <summary>Suggestions for the word at the end of a partly typed command line.</summary>
/// <param name="Start">Index in the command line where the word being completed starts.</param>
/// <param name="Length">Number of characters a chosen suggestion replaces.</param>
/// <param name="Suggestions">Matching suggestions, sorted by text.</param>
public sealed record CommandCompletion(int Start, int Length, IReadOnlyList<CommandSuggestion> Suggestions);
