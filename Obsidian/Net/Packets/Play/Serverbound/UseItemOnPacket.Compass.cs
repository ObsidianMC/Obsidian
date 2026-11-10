using Obsidian.API.Inventory;
using Obsidian.API.Inventory.DataComponents;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.WorldData;

namespace Obsidian.Net.Packets.Play.Serverbound;

public partial class UseItemOnPacket
{
    /// <summary>
    /// Vanilla's <c>CompassItem.useOn</c>: a compass used on a lodestone tracks it. A single compass is changed in the
    /// hand; from a stack, or in creative mode, one is taken and a tracking compass with its components goes to the
    /// inventory, or drops in front of the player when it doesn't fit.
    /// </summary>
    internal static async ValueTask<UseResult> UseCompassAsync(BlockUse use)
    {
        if (!use.Block.Is(BlockIds.Lodestone))
            return UseResult.Pass;

        var compass = use.Item!;
        var tracker = new LodestoneTrackerDataComponent
        {
            Target = new GlobalPos
            {
                Dimension = use.Level.DimensionName,
                Pos = use.Position
            },
            Tracked = true
        };
        var bound = new ItemStack(compass, 1)
        {
            [DataComponentType.LodestoneTracker] = tracker
        };

        // Vanilla replaces the stack only for a single compass of a player without infinite materials.
        var creative = use.Player.GameMode == GameMode.Creative;
        var inPlace = !creative && compass.Count == 1;
        var committed = inPlace
            ? use.TryCommit(() => true, consumeItem: true, replacement: bound)
            : use.TryCommit(() =>
            {
                // Taken before the new compass is added, as vanilla does, so it may stack back into the hand.
                if (!creative)
                {
                    use.Player.Inventory.RemoveItem(use.HandSlot, 1);
                    use.ChangedInventorySlots.Add(use.HandSlot);
                }

                BlockUseReturn.Give(use, bound);
                return true;
            });

        if (!committed)
            return UseResult.Fail;

        // Vanilla plays it for every player, the user too, as a player sound.
        var center = use.Center;
        use.Level.PacketBroadcaster.BroadcastToLevelInRange(use.Level, center, new SoundPacket
        {
            SoundLocation = "minecraft:item.lodestone_compass.lock",
            SoundPosition = new SoundPosition(center.X, center.Y, center.Z),
            Category = SoundCategory.Players,
            Volume = 1,
            Pitch = 1,
            Seed = Random.Shared.NextInt64()
        });

        await BlockUseReturn.SendChangedSlotsAsync(use);

        return UseResult.Success;
    }
}
