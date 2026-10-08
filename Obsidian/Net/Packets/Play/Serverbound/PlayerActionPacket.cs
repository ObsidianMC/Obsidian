using Obsidian.API.Events;
using Obsidian.API.Inventory;
using Obsidian.Entities;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Registries;
using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Serverbound;

public partial class PlayerActionPacket
{
    [Field(0), ActualType(typeof(int)), VarLength]
    public PlayerActionStatus Status { get; private set; }

    [Field(1)]
    public Vector Position { get; private set; }

    [Field(2), ActualType(typeof(sbyte))]
    public BlockFace Face { get; private set; } // This is an enum of what face of the block is being hit

    [Field(3), VarLength]
    public int Sequence { get; private set; }

    public override void Populate(INetStreamReader reader)
    {
        this.Status = reader.ReadVarInt<PlayerActionStatus>();
        this.Position = reader.ReadPosition();
        this.Face = reader.ReadUnsignedByte<BlockFace>();
        this.Sequence = reader.ReadVarInt();
    }

    public async override ValueTask HandleAsync(IServer server, IPlayer player)
    {
        if (Status == PlayerActionStatus.ReleaseUseItem)
        {
            if (player is Player concrete) await concrete.ReleaseUsingItemAsync();
            return;
        }
        if (Status == PlayerActionStatus.Stab)
        {
            if (player is Player concrete) await concrete.StabAsync();
            return;
        }
        if (await player.Level.GetBlockAsync(Position) is not IBlock block)
            return;

        if (Status == PlayerActionStatus.StartedDigging && block.Material == Material.NoteBlock && player.Level is Obsidian.WorldData.AbstractLevel notes)
            notes.PlayNoteBlock(Position, player);

        if (Status == PlayerActionStatus.FinishedDigging || (Status == PlayerActionStatus.StartedDigging && player.GameMode == GameMode.Creative))
        {
            var args = new BlockBreakEventArgs(server, player, block, Position, player.Level)
            {
                Sequence = this.Sequence
            };

            await server.EventDispatcher.ExecuteEventAsync(args);

            return;
        }

        this.BroadcastPlayerAction(player);
    }

    private void BroadcastPlayerAction(IPlayer player)
    {
        switch (this.Status)
        {
            case PlayerActionStatus.DropItem:
                {
                    DropItem(player, 1);
                    break;
                }
            case PlayerActionStatus.DropItemStack:
                {
                    DropItem(player, int.MaxValue);
                    break;
                }
            case PlayerActionStatus.StartedDigging:
            case PlayerActionStatus.CancelledDigging:
                player.Client.SendPacket(new BlockChangedAckPacket
                {
                    SequenceID = this.Sequence
                });
                break;
            case PlayerActionStatus.FinishedDigging:
                {


                    break;
                }
        }
    }

    private static void DropItem(IPlayer player, int amountToRemove)
    {
        var droppedItem = player.GetHeldItem();

        if (droppedItem is null or { Type: Material.Air } or { Count: <= 0 })
            return;

        var count = Math.Min(amountToRemove, droppedItem.Count);
        var stack = new ItemStack(droppedItem, count);

        if (!ItemEntity.Drop(player, stack))
            return;

        player.Inventory.RemoveItem(player.CurrentHeldItemSlot, count);

        player.Client.SendPacket(new ContainerSetSlotPacket
        {
            Slot = player.CurrentHeldItemSlot,

            ContainerId = 0,

            SlotData = player.GetHeldItem(),

            StateId = player.Inventory.StateId++
        });

    }
}

public readonly record struct PlayerActionStore
{
    public required Guid Player { get; init; }
    public required PlayerActionPacket Packet { get; init; }
}

public enum PlayerActionStatus : int
{
    StartedDigging,
    CancelledDigging,
    FinishedDigging,

    DropItemStack,
    DropItem,

    ReleaseUseItem,

    SwapItemInHand,

    Stab
}
