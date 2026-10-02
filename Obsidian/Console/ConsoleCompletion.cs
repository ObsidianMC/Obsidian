namespace Obsidian.Console;

/// <summary>A replacement range in the original line and escaped candidate tokens for that range.</summary>
public sealed record ConsoleCompletion(int Start, int Length, IReadOnlyList<string> Candidates,
    IReadOnlyList<bool>? Continuations = null);
