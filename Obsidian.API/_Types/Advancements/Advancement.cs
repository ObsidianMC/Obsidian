namespace Obsidian.API.Advancements;

public sealed class Advancement
{
    public Advancement(string identifier, string parent, AdvancementDisplay? display, AdvancementReward? reward, List<Criteria> criteria)
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

    public string Parent { get; init; }

    public AdvancementDisplay? Display { get; init; }

    public AdvancementReward? Reward { get; init; }

    public List<Criteria> Criteria { get; init; }
}
