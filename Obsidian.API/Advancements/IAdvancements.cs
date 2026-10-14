using System.Text.Json;

namespace Obsidian.API.Advancements;

/// <summary>Persistent criteria progress. Granting the last requirement awards the advancement's rewards.</summary>
public interface IAdvancements
{
    bool IsDone(string identifier);
    bool HasCriterion(string identifier, string criterion);
    ValueTask<bool> GrantAsync(string identifier, string? criterion = null);
    bool Revoke(string identifier, string? criterion = null);
}

/// <summary>A plugin condition decoder. Throw for unsupported conditions; those criteria never fire.</summary>
public sealed record AdvancementConditionsCodec<TInstance>(Func<JsonElement, TInstance> Decode);

/// <summary>A registered trigger. Fire with a predicate comparing decoded conditions with your event.</summary>
public sealed class AdvancementTrigger<TInstance>
{
    private readonly Func<IPlayer, Func<TInstance, bool>, ValueTask> fire;

    public AdvancementTrigger(string identifier, Func<IPlayer, Func<TInstance, bool>, ValueTask> fire)
    {
        this.Identifier = identifier;
        this.fire = fire;
    }

    public string Identifier { get; }

    /// <summary>Tests matching criteria for this player. An unloaded registration is inert.</summary>
    public ValueTask FireAsync(IPlayer player, Func<TInstance, bool> matches) => this.fire(player, matches);
}
