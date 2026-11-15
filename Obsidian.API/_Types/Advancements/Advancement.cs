namespace Obsidian.API.Advancements;

/// <summary>
/// An advancement: where it sits in the tree, how it is shown, its reward and the criteria that complete it.
/// </summary>
public sealed class Advancement
{
    /// <summary>Creates an advancement.</summary>
    /// <param name="identifier">The advancement's id.</param>
    /// <param name="parent">The id of the advancement it branches from, or an empty string for a root.</param>
    /// <param name="display">How the advancement is shown, or null to hide it from the advancement screen.</param>
    /// <param name="reward">What a player gets for completing it, or null for nothing.</param>
    /// <param name="criteria">The criteria that can complete it.</param>
    public Advancement(
        string identifier,
        string parent,
        AdvancementDisplay? display,
        AdvancementReward? reward,
        List<Criteria> criteria)
    {
        Identifier = identifier;
        Parent = parent;
        Display = display;
        Reward = reward;
        Criteria = criteria;
    }

    /// <summary>
    /// The identifier of the advancement.
    /// If a valid identifier is not detected this advancement will register with the obsidian namespace.
    /// </summary>
    public string Identifier { get; init; }

    /// <summary>Gets the id of the advancement it branches from, or null or empty for a root.</summary>
    public string Parent { get; init; }

    /// <summary>Gets how the advancement is shown, or null when it is not shown on the advancement screen.</summary>
    public AdvancementDisplay? Display { get; init; }

    /// <summary>Gets what a player gets for completing the advancement, or null for nothing.</summary>
    public AdvancementReward? Reward { get; init; }

    /// <summary>
    /// Gets the criteria that can complete the advancement. When <see cref="Requirements"/> is empty, every
    /// <see cref="Criteria.Required"/> criterion must be met.
    /// </summary>
    public List<Criteria> Criteria { get; init; }

    /// <summary>Every row must have at least one achieved criterion (AND of ORs), like vanilla.</summary>
    public List<List<string>> Requirements { get; init; } = [];

    /// <summary>Gets whether the client reports completing the advancement in its telemetry.</summary>
    public bool SendsTelemetryEvent { get; init; }
}
