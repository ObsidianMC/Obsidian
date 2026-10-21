using Microsoft.Extensions.Logging;
using Obsidian.API.Blocks;
using Obsidian.API.Inventory;
using Obsidian.API.Inventory.DataComponents;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Registries;
using Obsidian.Utilities;
using System.Collections.Frozen;
using System.Reflection;
using System.Text.Json;

namespace Obsidian.WorldData;

/// <summary>
/// A vanilla item class's <c>useOn</c> override, as Obsidian runs it when a player uses an item it owns on a block.
/// </summary>
internal interface IItemUseOnBehavior
{
    /// <returns>Whether the item did something with the block (vanilla's <c>InteractionResult.SUCCESS</c>).</returns>
    ValueTask<bool> UseOnAsync(AbstractLevel level, IPlayer player, ItemStack stack, int slot, Vector position, IBlock block, BlockFace face);
}

/// <summary>
/// Obsidian implementations of vanilla item classes that override <c>Item.useOn</c>. Which item an implementation
/// serves comes from <c>Assets/item_use_on_classes.json</c>, the Mojang class that declares each item's override;
/// <see cref="Counterparts"/> maps those classes to implementations, by full class name. Items whose owner has no
/// counterpart (yet) are handled elsewhere, or not at all.
/// </summary>
internal static partial class ItemUseOnBehaviors
{
    private const string HoeItem = "net.minecraft.world.item.HoeItem";

    /// <summary>The implemented owners, by Mojang class name.</summary>
    internal static readonly FrozenDictionary<string, IItemUseOnBehavior> Counterparts = new Dictionary<string, IItemUseOnBehavior>
    {
        [HoeItem] = new HoeUseOn()
    }.ToFrozenDictionary();

    /// <summary>The Mojang class declaring each item's <c>useOn</c>, by item identifier.</summary>
    internal static readonly FrozenDictionary<string, string> Owners = LoadOwners();

    /// <returns>The behaviour of the vanilla class that owns this item's <c>useOn</c> override, or null.</returns>
    public static IItemUseOnBehavior? For(Item item) =>
        Owners.TryGetValue(item.UnlocalizedName, out var owner) && Counterparts.TryGetValue(owner, out var behavior)
            ? behavior
            : null;

    private static JsonDocument Load(string name)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"Obsidian.Assets.{name}")
            ?? throw new InvalidOperationException($"Missing {name} asset.");
        return JsonDocument.Parse(stream);
    }

    private static FrozenDictionary<string, string> LoadOwners()
    {
        using var document = Load("item_use_on_classes.json");
        return document.RootElement.EnumerateObject().ToFrozenDictionary(item => item.Name, item => item.Value.GetString()!);
    }

    /// <summary>
    /// Vanilla's <c>HoeItem.useOn</c>: a hoe turns a block of <c>HoeItem.TILLABLES</c> into what the dump says (dirt
    /// into farmland, say), when the block above is air or the entry doesn't ask for that, and never from below. It
    /// plays the till sound, drops the entry's item, and costs the hoe 1 durability outside creative mode.
    /// </summary>
    private sealed partial class HoeUseOn : IItemUseOnBehavior
    {
        private readonly FrozenDictionary<string, Tillable> tillables = LoadTillables();

        private sealed record Tillable(string To, string? Drop, bool RequiresAirAbove);

        public async ValueTask<bool> UseOnAsync(AbstractLevel level, IPlayer player, ItemStack stack, int slot, Vector position, IBlock block, BlockFace face)
        {
            if (!this.tillables.TryGetValue(block.UnlocalizedName, out var tillable))
                return false;

            // HoeItem.onlyIfAirAbove: the click wasn't on the underside, and the block above is air.
            if (tillable.RequiresAirAbove
                && (face == BlockFace.Down || level.GetLoadedChunk(position.X >> 4, position.Z >> 4)?.GetBlock(position + Vector.Up) is not { IsAir: true }))
                return false;

            if (!BlocksRegistry.TryGetDefault(tillable.To, out var target))
                return false;

            var sound = new VectorD(position.X + 0.5, position.Y + 0.5, position.Z + 0.5);
            level.PacketBroadcaster.BroadcastToLevelInRange(level, position, new SoundPacket
            {
                SoundLocation = "minecraft:item.hoe.till",
                SoundPosition = new SoundPosition(sound.X, sound.Y, sound.Z),
                Category = SoundCategory.Blocks,
                Volume = 1,
                Pitch = 1,
                Seed = Random.Shared.NextInt64()
            });

            await level.SetBlockAsync(position, target, doBlockUpdate: true);

            // HoeItem.changeIntoStateAndDropItem: Block.popResourceFromFace.
            if (tillable.Drop is not null && ItemsRegistry.TryGet(tillable.Drop, out var drop))
            {
                var direction = face.ToVector();
                level.SpawnItem(
                    new VectorD(position.X + 0.5 + direction.X * 0.625, position.Y + 0.5 + direction.Y * 0.625 - 0.125, position.Z + 0.5 + direction.Z * 0.625),
                    ItemsRegistry.GetSingleItem(drop));
            }

            Log.Tilled(level.Logger, position, block.UnlocalizedName, target.UnlocalizedName);

            if (player.GameMode != GameMode.Creative)
                await HurtAndBreakAsync(player, stack, slot);

            return true;
        }

        // ItemStack.hurtAndBreak(1): unbreakable stacks don't wear, and a worn-out one is used up.
        private static async ValueTask HurtAndBreakAsync(IPlayer player, ItemStack stack, int slot)
        {
            if (stack.Unbreakable || stack.Holder.MaxDamage <= 0)
                return;

            // Unbreaking can spare the use, as it does for flint and steel.
            if (!Net.Packets.Play.Serverbound.UseItemOnPacket.ShouldDamageTool(stack))
                return;

            var maxDamage = stack.GetComponent<SimpleDataComponent<int>>(DataComponentType.MaxDamage)?.Value ?? stack.Holder.MaxDamage;
            var damage = stack.Damage + 1;
            if (damage >= maxDamage)
                player.Inventory.RemoveItem(slot, stack.Count);
            else
                stack[DataComponentType.Damage] = ComponentBuilder.Damage with { Value = damage };

            await player.SendInventorySlotAsync(slot);
        }

        private static FrozenDictionary<string, Tillable> LoadTillables()
        {
            using var document = Load("item_use_on_transforms.json");
            return document.RootElement.GetProperty(HoeItem).EnumerateObject().ToFrozenDictionary(
                block => block.Name,
                block => new Tillable(
                    block.Value.GetProperty("to").GetString()!,
                    block.Value.GetProperty("drop").GetString(),
                    block.Value.GetProperty("requiresAirAbove").GetBoolean()));
        }

        private static partial class Log
        {
            [LoggerMessage(Level = LogLevel.Debug, Message = "A hoe tilled {From} at {Position} into {To}.")]
            public static partial void Tilled(ILogger logger, Vector position, string from, string to);
        }
    }
}
