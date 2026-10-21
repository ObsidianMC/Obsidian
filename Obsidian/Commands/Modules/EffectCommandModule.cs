using Obsidian.API.Commands;
using Obsidian.Commands.Framework;
using Obsidian.Entities;
using static Obsidian.Commands.CommandFeedback;

namespace Obsidian.Commands.Modules;

/// <summary>
/// Vanilla's <c>/effect</c>: gives living entities mob effects, or clears them.
/// </summary>
[CommandGroup("effect")]
[CommandInfo("Adds or removes status effects.", "/effect <give|clear>")]
[RequirePermissionLevel(2)]
public sealed class EffectCommandModule : CommandModuleBase
{
    // Vanilla's default duration, 30 seconds, and the longest it accepts, a million.
    private const int DefaultSeconds = 30;
    private const int MaxSeconds = 1_000_000;

    [Command("give")]
    [CommandInfo("Gives an effect.", "/effect give <targets> <effect> [<seconds>] [<amplifier>] [<hideParticles>]")]
    public Task GiveAsync(IEntity[] targets, PotionEffect effect) => this.ApplyAsync(targets, effect, DefaultSeconds, 0, false);

    [CommandOverload]
    public Task GiveAsync(IEntity[] targets, PotionEffect effect, int seconds) => this.ApplyAsync(targets, effect, seconds, 0, false);

    [CommandOverload]
    public Task GiveAsync(IEntity[] targets, PotionEffect effect, int seconds, int amplifier) =>
        this.ApplyAsync(targets, effect, seconds, amplifier, false);

    [CommandOverload]
    public Task GiveAsync(IEntity[] targets, PotionEffect effect, int seconds, int amplifier, bool hideParticles) =>
        this.ApplyAsync(targets, effect, seconds, amplifier, hideParticles);

    [Command("clear")]
    [CommandInfo("Clears effects.", "/effect clear [<targets>] [<effect>]")]
    public Task ClearAsync() => this.CommandContext.Executor is IEntity executor
        ? this.ClearAsync([executor])
        : this.CommandContext.FailAsync("An entity is required to run this command here");

    [CommandOverload]
    public async Task ClearAsync(IEntity[] targets)
    {
        if (await this.CommandContext.NoneFoundAsync(targets))
            return;

        var cleared = targets.OfType<ILiving>().Where(living => living.ActivePotionEffects.Count > 0).ToArray();
        foreach (var living in cleared)
            living.ClearPotionEffects();

        if (cleared.Length == 0)
        {
            await this.CommandContext.FailAsync(targets.Length == 1
                ? "Target has no effects to remove"
                : "No targets had effects to remove");
            return;
        }

        await this.CommandContext.SuccessAsync($"Removed every effect from {Describe(cleared)}");
    }

    [CommandOverload]
    public async Task ClearAsync(IEntity[] targets, PotionEffect effect)
    {
        if (await this.CommandContext.NoneFoundAsync(targets))
            return;

        var id = (int)effect - 1;
        var cleared = targets.OfType<ILiving>().Where(living => living.HasPotionEffect(id)).ToArray();
        foreach (var living in cleared)
            living.RemovePotionEffect(id);

        if (cleared.Length == 0)
        {
            await this.CommandContext.FailAsync(targets.Length == 1
                ? "Target doesn't have the requested effect"
                : "No targets had the requested effect");
            return;
        }

        await this.CommandContext.SuccessAsync($"Removed effect {Name(effect)} from {Describe(cleared)}");
    }

    private async Task ApplyAsync(IEntity[] targets, PotionEffect effect, int seconds, int amplifier, bool hideParticles)
    {
        if (await this.CommandContext.NoneFoundAsync(targets))
            return;

        if (seconds is < 1 or > MaxSeconds || amplifier is < 0 or > 255)
        {
            await this.CommandContext.FailAsync($"Effects last 1 to {MaxSeconds} seconds, with an amplifier of 0 to 255");
            return;
        }

        var id = (int)effect - 1;
        var flags = EntityEffectFlags.ShowIcon | (hideParticles ? EntityEffectFlags.None : EntityEffectFlags.ShowParticles);
        var applied = new List<IEntity>();

        foreach (var target in targets)
        {
            if (target is not Living living || !living.Alive)
                continue;

            if (effect is PotionEffect.InstantHealth or PotionEffect.InstantDamage)
                await ApplyInstantAsync(living, effect, amplifier, this.CommandContext.Executor);
            else
                living.AddPotionEffect(id, seconds * 20, amplifier, flags);

            applied.Add(target);
        }

        if (applied.Count == 0)
        {
            await this.CommandContext.FailAsync("Unable to apply this effect (target is either immune to effects, or has something stronger)");
            return;
        }

        await this.CommandContext.SuccessAsync($"Applied effect {Name(effect)} to {Describe(applied)}");
    }

    // Instant health heals and instant damage hurts, the other way around for undead mobs, as a potion would.
    private static async Task ApplyInstantAsync(Living target, PotionEffect effect, int amplifier, IEntity? source)
    {
        var harms = (effect == PotionEffect.InstantDamage) != CombatEffects.IsUndead(target.Type);

        if (harms)
        {
            await target.DamageCombatAsync(source ?? target, 6 << Math.Min(amplifier, 29), CombatDamageKind.Magic);
            return;
        }

        target.Health = Math.Min(target.GetAttributeValue("minecraft:max_health"), target.Health + (4 << Math.Min(amplifier, 29)));

        if (target is IPlayer player)
            await SyncHealthAsync(player);
    }

    private static string Name(PotionEffect effect) => $"minecraft:{effect.ToString().ToSnakeCase()}";
}
