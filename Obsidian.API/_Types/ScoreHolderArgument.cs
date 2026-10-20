using Obsidian.API.Commands;

namespace Obsidian.API;

/// <summary>
/// The score holders a command names: those a selector selects, every tracked holder for <c>*</c>, or one by name.
/// </summary>
/// <param name="Text">What was typed.</param>
/// <param name="Selector">The selector, when one was typed.</param>
public sealed record ScoreHolderArgument(string Text, EntitySelector? Selector)
{
    /// <summary>Whether it names every holder the scoreboard tracks.</summary>
    public bool IsWildcard => this.Text == "*";

    /// <summary>
    /// The names of the holders it names: players by username, other entities by UUID, as vanilla's scoreboard names
    /// them.
    /// </summary>
    /// <param name="tracked">Every holder the scoreboard tracks, which <c>*</c> names.</param>
    public IReadOnlyList<string> Resolve(CommandContext context, IEnumerable<string> tracked)
    {
        if (this.IsWildcard)
            return [.. tracked];

        if (this.Selector is null)
            return [this.Text];

        return [.. this.Selector.Select(context).Select(NameOf)];
    }

    /// <summary>The name an entity's scores are kept under.</summary>
    public static string NameOf(IEntity entity) => entity is IPlayer player ? player.Username : entity.Uuid.ToString();
}
