using Obsidian.API.Inventory;
using Obsidian.Net.Packets.Play.Clientbound;

namespace Obsidian.WorldData;

public abstract partial class AbstractLevel
{
    // The power of the explosion of a bed or respawn anchor where it doesn't work, which sets fires (vanilla's
    // BedBlock and RespawnAnchorBlock).
    private const float BadRespawnPointPower = 5;

    private const int MaxAnchorCharges = 4;

    /// <summary>
    /// A player using a bed, like vanilla's <c>BedBlock.useWithoutItem</c>: where the bed rule there says beds
    /// explode (the nether's and the end's), the bed is removed and explodes, with the rule's message if it has one;
    /// otherwise a player is told the bed is occupied, or that they can't sleep now when the rule says so.
    /// </summary>
    /// <remarks>
    /// Players can't sleep yet, nor set their spawn with a bed: a bed that lets them does nothing more.
    /// </remarks>
    /// <returns>Whether <paramref name="block"/> is a bed, which the use is then done with.</returns>
    internal async ValueTask<bool> UseBedAsync(IPlayer player, Vector position, IBlock block)
    {
        if (!TagsRegistry.Block.Beds.Entries.Contains(block.RegistryId))
            return false;

        // The facing property points from the bed's foot to its head, whose position the rule is read at.
        var towardsHead = BedFacing(block.GetProperty("facing"));
        var head = position;

        if (block.GetProperty("part") != "head")
        {
            head = position + towardsHead;
            if (GameEventBlock(head) is not IBlock headBlock || headBlock.RegistryId != block.RegistryId)
                return true;

            block = headBlock;
        }

        var rule = this.BedRuleAt(head);

        if (rule.Explode)
        {
            if (rule.ErrorMessage is ChatMessage reason)
                await player.QueuePacketAsync(new SystemChatPacket(reason, overlay: true));

            await SetBlockAsync(head, BlocksRegistry.Air, true);

            var foot = head - towardsHead;
            if (GameEventBlock(foot)?.RegistryId == block.RegistryId)
                await SetBlockAsync(foot, BlocksRegistry.Air, true);

            await ExplodeAsync((VectorD)head + new VectorD(0.5, 0.5, 0.5), BadRespawnPointPower, fire: true);
            return true;
        }

        if (block.GetProperty("occupied") == "true")
        {
            var occupied = new ChatMessage { Translate = "block.minecraft.bed.occupied" };
            await player.QueuePacketAsync(new SystemChatPacket(occupied, overlay: true));
        }
        else if (!rule.AllowsSleep(this) && rule.ErrorMessage is ChatMessage reason)
        {
            await player.QueuePacketAsync(new SystemChatPacket(reason, overlay: true));
        }

        return true;
    }

    /// <summary>
    /// A player using a respawn anchor, like vanilla's <c>RespawnAnchorBlock</c>: glowstone charges it up to four
    /// times; a charged anchor where <c>respawn_anchor_works</c> is false (everywhere but the nether) is removed and
    /// explodes.
    /// </summary>
    /// <remarks>
    /// Players can't set their spawn with a working anchor yet: using one does nothing more. Unlike vanilla, an anchor
    /// next to water explodes like any other.
    /// </remarks>
    /// <returns>Whether the use is done with; an empty anchor lets the held item be used instead.</returns>
    internal async ValueTask<bool> UseRespawnAnchorAsync(IPlayer player, Vector position, IBlock block, int handSlot)
    {
        if (!block.Is(BlockIds.RespawnAnchor))
            return false;

        var charges = int.TryParse(block.GetProperty("charges"), out var parsed) ? parsed : 0;
        var held = player.Inventory.GetItem(handSlot);

        if (held is { Count: > 0, Holder.Id: ItemIds.Glowstone } && charges < MaxAnchorCharges)
        {
            await SetBlockAsync(position, block.WithProperty("charges", charges + 1), true);

            var center = (VectorD)position + new VectorD(0.5, 0.5, 0.5);
            PacketBroadcaster.QueuePacketToLevelInRange(this, center, new SoundPacket
            {
                SoundLocation = "minecraft:block.respawn_anchor.charge",
                SoundPosition = new SoundPosition(center.X, center.Y, center.Z),
                Category = SoundCategory.Blocks,
                Volume = 1,
                Pitch = 1,
                Seed = Random.Shared.NextInt64()
            });

            if (player.GameMode != GameMode.Creative)
            {
                player.Inventory.RemoveItem(handSlot, 1);
                await player.QueuePacketAsync(new ContainerSetSlotPacket
                {
                    ContainerId = 0,
                    Slot = (short)handSlot,
                    SlotData = player.Inventory.GetItem(handSlot)
                });
            }

            return true;
        }

        if (charges == 0)
            return false;

        if (!this.Gameplay(LevelEnvironment.RespawnAnchorWorks, position))
        {
            await SetBlockAsync(position, BlocksRegistry.Air, true);
            await ExplodeAsync((VectorD)position + new VectorD(0.5, 0.5, 0.5), BadRespawnPointPower, fire: true);
        }

        return true;
    }

    /// <summary>
    /// Whether water poured at <paramref name="position"/> evaporates, like vanilla's <c>BucketItem.emptyContents</c>
    /// where <c>water_evaporates</c> is true (the nether): it isn't placed, and the players around hear it hiss.
    /// </summary>
    internal bool Evaporates(IPlayer player, Vector position)
    {
        if (!this.Gameplay(LevelEnvironment.WaterEvaporates, position))
            return false;

        // Vanilla's client plays the sound for the player who poured it, so the server sends it to the others.
        var center = (VectorD)position + new VectorD(0.5, 0.5, 0.5);
        PacketBroadcaster.QueuePacketToLevelInRange(this, center, new SoundPacket
        {
            SoundLocation = "minecraft:block.fire.extinguish",
            SoundPosition = new SoundPosition(center.X, center.Y, center.Z),
            Category = SoundCategory.Blocks,
            Volume = 0.5f,
            Pitch = 2.6f + (Random.Shared.NextSingle() - Random.Shared.NextSingle()) * 0.8f,
            Seed = Random.Shared.NextInt64()
        }, player.EntityId);

        return true;
    }

    private static Vector BedFacing(string? facing) => facing switch
    {
        "north" => new Vector(0, 0, -1),
        "south" => Vector.South,
        "west" => new Vector(-1, 0, 0),
        _ => Vector.East
    };
}
