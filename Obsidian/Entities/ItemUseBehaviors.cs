using Obsidian.API.Inventory.DataComponents;
using Obsidian.API.Inventory;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.WorldData;
using System.Collections.Frozen;
using System.Reflection;
using System.Text.Json;

namespace Obsidian.Entities;

/// <summary>
/// A vanilla item class's <c>use</c> override, as Obsidian runs it when a player right-clicks with the item.
/// </summary>
internal interface IItemUseBehavior
{
    /// <summary>Vanilla's <c>Item.use</c> for the item, which for a throwable item throws it.</summary>
    ValueTask UseAsync(PlayerBase player, InteractionHand hand, ItemStack stack);
}

/// <summary>
/// Obsidian implementations of vanilla item classes that override <c>Item.use</c>. Which item an implementation
/// serves comes from <c>Assets/item_use_classes.json</c>, the Mojang class that declares each item's override;
/// <see cref="Counterparts"/> maps those classes to implementations, by full class name. Items whose owner has no
/// counterpart are handled elsewhere, or not at all.
/// </summary>
internal static class ItemUseBehaviors
{
    /// <summary>The implemented owners, by Mojang class name.</summary>
    internal static readonly FrozenDictionary<string, IItemUseBehavior> Counterparts = new Dictionary<string, IItemUseBehavior>
    {
        ["net.minecraft.world.item.EggItem"] = new EggUse(),
        ["net.minecraft.world.item.SnowballItem"] = new SnowballUse(),
        ["net.minecraft.world.item.EnderpearlItem"] = new EnderPearlUse(),
        ["net.minecraft.world.item.SplashPotionItem"] = new SplashPotionUse(),
        ["net.minecraft.world.item.EnderEyeItem"] = new EnderEyeUse(),
        ["net.minecraft.world.item.ExperienceBottleItem"] = new ExperienceBottleUse(),
    }.ToFrozenDictionary();

    /// <summary>The Mojang class declaring each item's <c>use</c>, by item identifier.</summary>
    internal static readonly FrozenDictionary<string, string> Owners = LoadOwners();

    /// <returns>The behaviour of the vanilla class that owns this item's <c>use</c> override, or null.</returns>
    internal static IItemUseBehavior? For(ItemStack stack) =>
        Owners.TryGetValue(stack.Holder.UnlocalizedName, out var owner) && Counterparts.TryGetValue(owner, out var behavior)
            ? behavior
            : null;

    private static FrozenDictionary<string, string> LoadOwners()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Obsidian.Assets.item_use_classes.json")
            ?? throw new InvalidOperationException("Missing item_use_classes.json asset.");
        using var document = JsonDocument.Parse(stream);
        return document.RootElement.EnumerateObject().ToFrozenDictionary(item => item.Name, item => item.Value.GetString()!);
    }

    /// <summary>
    /// Vanilla's <c>EnderpearlItem.use</c>: throws an ender pearl, subject to its own cooldown.
    /// </summary>
    private sealed class EnderPearlUse : IItemUseBehavior
    {
        public async ValueTask UseAsync(PlayerBase player, InteractionHand hand, ItemStack stack)
        {
            await EnderPearl.ThrowAsync(player, hand, stack);
        }
    }

    /// <summary>
    /// Vanilla's <c>EnderEyeItem.use</c>: throws an eye of ender toward the nearest structure.
    /// </summary>
    private sealed class EnderEyeUse : IItemUseBehavior
    {
        public async ValueTask UseAsync(PlayerBase player, InteractionHand hand, ItemStack stack)
        {
            await EyeOfEnder.ThrowAsync(player, hand, stack);
        }
    }

    /// <summary>
    /// Vanilla's <c>ExperienceBottleItem.use</c>: throws a bottle o' enchanting.
    /// </summary>
    private sealed class ExperienceBottleUse : IItemUseBehavior
    {
        public async ValueTask UseAsync(PlayerBase player, InteractionHand hand, ItemStack stack)
        {
            await ThrownExperienceBottle.ThrowAsync(player, hand, stack);
        }
    }

    /// <summary>
    /// Vanilla's <c>ThrowablePotionItem.use</c> for splash potions: throws the potion at pitch -20 and speed 0.5, and
    /// the splash it makes is the stack's potion effects.
    /// </summary>
    private sealed class SplashPotionUse : IItemUseBehavior
    {
        // Vanilla's ThrowablePotionItem: shot upward a little, at half the throwable speed.
        private const float PitchOffset = -20;
        private const float Speed = 0.5f;

        public async ValueTask UseAsync(PlayerBase player, InteractionHand hand, ItemStack stack)
        {
            if (!ThrowableItemUse.CanThrow(player) || player.Level is not AbstractLevel level)
                return;

            var potion = new MobProjectile(
                player,
                EntityType.SplashPotion,
                new VectorD(player.Position.X, player.HeadY - 0.1, player.Position.Z),
                ThrownExperienceBottle.Direction(player.Yaw.Degrees, player.Pitch.Degrees + PitchOffset),
                Speed)
            {
                SplashEffects = [.. ArrowPotionEffects.Get(stack.GetComponent<PotionContentsDataComponent>(DataComponentType.PotionContents))],
                SplashItem = new ItemStack(stack, 1)
            };
            potion.Motion += player.CombatMotion;

            level.PacketBroadcaster.BroadcastToLevelInRange(level, (Vector)player.Position.Floor(), new SoundPacket
            {
                SoundLocation = "minecraft:entity.splash_potion.throw",
                SoundPosition = new SoundPosition(player.Position.X, player.Position.Y, player.Position.Z),
                Category = SoundCategory.Neutral,
                Volume = 0.5f,
                Pitch = 0.4f / (Globals.Random.NextSingle() * 0.4f + 0.8f),
                Seed = Globals.Random.NextInt64()
            });

            level.SpawnEntity(potion);
            await ThrowableItemUse.ConsumeAsync(player, hand, stack);
        }
    }

    /// <summary>
    /// Vanilla's <c>EggItem.use</c>: throws an egg.
    /// </summary>
    private sealed class EggUse : IItemUseBehavior
    {
        public ValueTask UseAsync(PlayerBase player, InteractionHand hand, ItemStack stack) =>
            ThrowableItemUse.CanThrow(player) ? Egg.ThrowAsync(player, hand, stack) : ValueTask.CompletedTask;
    }

    /// <summary>
    /// Vanilla's <c>SnowballItem.use</c>: throws a snowball, the player's own, which a snow golem's shots share.
    /// </summary>
    private sealed class SnowballUse : IItemUseBehavior
    {
        public async ValueTask UseAsync(PlayerBase player, InteractionHand hand, ItemStack stack)
        {
            if (!ThrowableItemUse.CanThrow(player) || player.Level is not AbstractLevel level)
                return;

            // ThrowableItemProjectile.shootFromRotation at speed 1.5, from just below the eyes.
            var snowball = new MobProjectile(
                player,
                EntityType.Snowball,
                new VectorD(player.Position.X, player.HeadY - 0.1, player.Position.Z),
                player.ShotDirection(0),
                1.5f);
            snowball.Motion += player.CombatMotion;

            level.PacketBroadcaster.BroadcastToLevelInRange(level, (Vector)player.Position.Floor(), new SoundPacket
            {
                SoundLocation = "minecraft:entity.snowball.throw",
                SoundPosition = new SoundPosition(player.Position.X, player.Position.Y, player.Position.Z),
                Category = SoundCategory.Neutral,
                Volume = 0.5f,
                Pitch = 0.4f / (Globals.Random.NextSingle() * 0.4f + 0.8f),
                Seed = Globals.Random.NextInt64()
            });

            level.SpawnEntity(snowball);
            await ThrowableItemUse.ConsumeAsync(player, hand, stack);
        }
    }
}

/// <summary>
/// The parts vanilla's throwable items share: who may throw, and the item used up and counted after the throw.
/// </summary>
internal static class ThrowableItemUse
{
    /// <summary>Whether a player can throw: alive, not respawning, and not a spectator, in a level Obsidian runs.</summary>
    internal static bool CanThrow(PlayerBase player) =>
        player.Alive
        && !player.Respawning
        && player.GameMode != GameMode.Spectator
        && player.Level is AbstractLevel;

    /// <summary>
    /// Vanilla's <c>ItemStack.consume(1)</c> and the item's <c>Stats.ITEM_USED</c>: one item is used up unless the
    /// player is in creative mode.
    /// </summary>
    internal static async ValueTask ConsumeAsync(PlayerBase player, InteractionHand hand, ItemStack stack)
    {
        player.Statistics.Increment(stack.Holder.UnlocalizedName, type: "minecraft:used");

        if (player.GameMode == GameMode.Creative)
            return;

        var slot = hand == InteractionHand.OffHand ? 45 : player.CurrentHeldItemSlot;
        player.Inventory.RemoveItem(slot, 1);
        await player.SyncInventorySlotAsync(slot);
    }
}
