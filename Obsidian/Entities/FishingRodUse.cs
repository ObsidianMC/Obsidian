using System.IO;
using System.Reflection;
using System.Text.Json;
using Obsidian.API.Inventory;
using Obsidian.API.Loot;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.WorldData;

namespace Obsidian.Entities;

/// <summary>
/// Vanilla's <c>FishingRodItem.use</c>: casts a bobber from the rod when the player has none out, or reels the
/// player's bobber in, which costs durability and may yield loot.
/// </summary>
internal sealed class FishingRodUse : IItemUseBehavior
{
    // The offhand slot in the player's inventory.
    private const int OffHandSlot = 45;

    public async ValueTask UseAsync(PlayerBase player, InteractionHand hand, ItemStack stack)
    {
        if (player.Level is not AbstractLevel level)
            return;

        if (player.Fishing is { } hook)
        {
            var damage = await hook.RetrieveAsync(stack);
            if (damage > 0)
                CombatItems.HurtItem(stack, damage);

            var slot = hand == InteractionHand.MainHand ? player.CurrentHeldItemSlot : OffHandSlot;
            await player.SendInventorySlotAsync(slot);
            PlaySound(level, player, "minecraft:entity.fishing_bobber.retrieve", 1f);
            return;
        }

        if (FishingHook.Cast(player, level, FishingData.LuckBonus(stack), FishingData.LureTicks(stack)) is null)
            return;

        PlaySound(level, player, "minecraft:entity.fishing_bobber.throw", 0.5f);
        player.Statistics.Increment(stack.Holder.UnlocalizedName, type: "minecraft:used");
    }

    private static void PlaySound(AbstractLevel level, PlayerBase player, string sound, float volume) =>
        level.PacketBroadcaster.BroadcastToLevelInRange(level, (Vector)player.Position.Floor(), new SoundPacket
        {
            SoundLocation = sound,
            SoundPosition = new SoundPosition(player.Position.X, player.Position.Y, player.Position.Z),
            Category = SoundCategory.Neutral,
            Volume = volume,
            Pitch = 0.4f / (Globals.Random.NextSingle() * 0.4f + 0.8f),
            Seed = Globals.Random.NextInt64()
        });
}

/// <summary>
/// Vanilla's fishing data from the asset dump: the fish that count for the fish-caught statistic, and the
/// enchantment effects on a rod (Luck of the Sea's luck, Lure's time reduction).
/// </summary>
internal static class FishingData
{
    private const string LuckEffect = "minecraft:fishing_luck_bonus";
    private const string TimeEffect = "minecraft:fishing_time_reduction";

    private static readonly HashSet<string> Fish = LoadFish();
    private static readonly Dictionary<string, Dictionary<string, (double Base, double PerLevel)>> Effects = LoadEffects();

    internal static bool IsFish(ItemStack stack) => Fish.Contains(stack.Holder.UnlocalizedName);

    /// <summary>Vanilla's <c>getFishingLuckBonus</c>: the rod's Luck of the Sea, as the whole luck it adds.</summary>
    internal static int LuckBonus(ItemStack stack) => (int)Sum(stack, LuckEffect);

    /// <summary>Vanilla's <c>getFishingTimeReduction</c> in ticks: seconds of Lure, times 20.</summary>
    internal static int LureTicks(ItemStack stack) => (int)(Sum(stack, TimeEffect) * 20);

    private static double Sum(ItemStack stack, string effect)
    {
        var total = 0d;
        foreach (var enchantment in EnchantmentHelper.GetEnchantments(stack))
        {
            var identifier = EnchantmentsRegistry.All.FirstOrDefault(definition => definition.Id == enchantment.Id)?.Identifier;
            if (identifier is null || !Effects.TryGetValue(identifier, out var effects) || !effects.TryGetValue(effect, out var value))
                continue;

            total += value.Base + value.PerLevel * (enchantment.Level - 1);
        }

        return total;
    }

    private static HashSet<string> LoadFish()
    {
        using var document = Read("Obsidian.Assets.tags.json");
        var values = document.RootElement.GetProperty("item/fishes").GetProperty("values");
        return values.EnumerateArray().Select(value => value.GetString()!).ToHashSet();
    }

    private static Dictionary<string, Dictionary<string, (double Base, double PerLevel)>> LoadEffects()
    {
        using var document = Read("Obsidian.Assets.enchantments.json");
        var result = new Dictionary<string, Dictionary<string, (double Base, double PerLevel)>>();
        foreach (var enchantment in document.RootElement.EnumerateObject())
        {
            if (!enchantment.Value.TryGetProperty("effects", out var effects))
                continue;

            foreach (var effect in new[] { LuckEffect, TimeEffect })
            {
                if (!effects.TryGetProperty(effect, out var entries))
                    continue;

                var total = (Base: 0d, PerLevel: 0d);
                foreach (var entry in entries.EnumerateArray())
                {
                    var definition = entry.GetProperty("effect");
                    var value = definition.GetProperty("value");
                    if (definition.GetProperty("type").GetString() != "minecraft:add"
                        || value.GetProperty("type").GetString() != "minecraft:linear")
                        throw new InvalidDataException($"Unsupported {effect} effect on {enchantment.Name}.");

                    total.Base += value.GetProperty("base").GetDouble();
                    if (value.TryGetProperty("per_level_above_first", out var perLevel))
                        total.PerLevel += perLevel.GetDouble();
                }

                if (!result.TryGetValue(enchantment.Name, out var byEffect))
                    result[enchantment.Name] = byEffect = [];
                byEffect[effect] = total;
            }
        }

        return result;
    }

    private static JsonDocument Read(string resource)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"Missing {resource} asset.");
        return JsonDocument.Parse(stream);
    }
}
