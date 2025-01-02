using Obsidian.API.Events;
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

    public async override ValueTask HandleAsync(Server server, Player player)
    {
        if (await player.world.GetBlockAsync(Position) is not IBlock block)
            return;

        if (Status == PlayerActionStatus.FinishedDigging || (Status == PlayerActionStatus.StartedDigging && player.Gamemode == Gamemode.Creative))
        {
            await player.world.SetBlockAsync(Position, BlocksRegistry.Air, true);
            player.client.SendPacket(new BlockChangedAckPacket
            {
                SequenceID = Sequence
            });

            var args = new BlockBreakEventArgs(server, player, block, Position);
            await server.EventDispatcher.ExecuteEventAsync(args);
            if (args.Handled)
                return;
        }

        this.BroadcastPlayerAction(player, block);
    }

    private void BroadcastPlayerAction(Player player, IBlock block)
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
                    DropItem(player, 64);
                    break;
                }
            case PlayerActionStatus.StartedDigging:
            case PlayerActionStatus.CancelledDigging:
                break;
            case PlayerActionStatus.FinishedDigging:
                {
                    player.PacketBroadcaster.QueuePacketToWorld(player.world, 0, new BlockDestructionPacket
                    {
                        EntityId = player,
                        Position = this.Position,
                        DestroyStage = -1
                    }, player.EntityId);

                    var droppedItem = ItemsRegistry.GetSingleItem(block.Material);

                    if (droppedItem.Type == Material.Air) { break; }

                    var item = new ItemEntity
                    {
                        EntityId = Server.GetNextEntityId(),
                        Item = droppedItem,
                        World = player.world,
                        Position = (VectorF)this.Position + 0.5f,
                        PacketBroadcaster = player.PacketBroadcaster,
                    };

                    player.world.TryAddEntity(item);

                    item.SpawnEntity(Velocity.FromBlockPerTick(GetRandDropVelocity(), GetRandDropVelocity(), GetRandDropVelocity()));

                    break;
                }
        }
    }

    private static float GetRandDropVelocity()
    {
        var f = Globals.Random.NextFloat();

        return f * 0.5f;
    }

    private static void DropItem(Player player, sbyte amountToRemove)
    {
        var droppedItem = player.GetHeldItem();

        if (droppedItem is null or { Type: Material.Air })
            return;

        var loc = new VectorF(player.Position.X, (float)player.HeadY - 0.3f, player.Position.Z);

        var item = new ItemEntity
        {
            EntityId = Server.GetNextEntityId(),
            Item = droppedItem,
            World = player.world,
            PacketBroadcaster = player.PacketBroadcaster,
            Position = loc
        };

        player.world.TryAddEntity(item);

        var lookDir = player.GetLookDirection();

        var vel = Velocity.FromDirection(loc, lookDir);//TODO properly shoot the item towards the direction the players looking at

        item.SpawnEntity(vel);

        player.Inventory.RemoveItem(player.inventorySlot, amountToRemove);

        player.client.SendPacket(new ContainerSetSlotPacket
        {
            Slot = player.inventorySlot,

            ContainerId = 0,

            SlotData = player.GetHeldItem(),

            StateId = player.Inventory.StateId++
        });

    }
}

public readonly struct PlayerActionStore
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

    ShootArrowOrFinishEating,

    SwapItemInHand
}
