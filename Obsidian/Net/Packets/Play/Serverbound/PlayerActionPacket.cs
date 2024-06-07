using Obsidian.API.Events;
using Obsidian.Entities;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Registries;
using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Serverbound;

public partial class PlayerActionPacket : IServerboundPacket
{
    [Field(0), ActualType(typeof(int)), VarLength]
    public PlayerActionStatus Status { get; private set; }

    [Field(1)]
    public Vector Position { get; private set; }

    [Field(2), ActualType(typeof(sbyte))]
    public BlockFace Face { get; private set; } // This is an enum of what face of the block is being hit

    [Field(3), VarLength]
    public int Sequence { get; private set; }

    public int Id => 0x24;

    public async ValueTask HandleAsync(Server server, Player player)
    {
        if (await player.world.GetBlockAsync(Position) is not IBlock block)
            return;

        if (Status == PlayerActionStatus.FinishedDigging || (Status == PlayerActionStatus.StartedDigging && player.Gamemode == Gamemode.Creative))
        {
            await player.world.SetBlockAsync(Position, BlocksRegistry.Air, true);
            player.client.SendPacket(new AcknowledgeBlockChangePacket
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
                player.PacketBroadcaster.QueuePacketToWorld(player.World, new SetBlockDestroyStagePacket
                {
                    EntityId = player,
                    Position = this.Position,
                    DestroyStage = -1
                });

                var droppedItem = ItemsRegistry.Get(block.Material);

                if (droppedItem.Id == 0) { break; }

                var item = new ItemEntity
                {
                    EntityId = player + player.world.GetTotalLoadedEntities() + 1,
                    Count = 1,
                    Id = droppedItem.Id,
                    Glowing = true,
                    World = player.world,
                    Position = this.Position,
                    PacketBroadcaster = player.PacketBroadcaster,
                };

                player.world.TryAddEntity(item);

                player.PacketBroadcaster.QueuePacketToWorld(player.World, new SpawnEntityPacket
                {
                    EntityId = item.EntityId,
                    Uuid = item.Uuid,
                    Type = EntityType.Item,
                    Position = item.Position,
                    Pitch = 0,
                    Yaw = 0,
                    Data = 1,
                    Velocity = Velocity.FromVector(new VectorF(
                        Globals.Random.NextFloat() * 0.5f,
                        Globals.Random.NextFloat() * 0.5f,
                        Globals.Random.NextFloat() * 0.5f))
                });

                player.PacketBroadcaster.QueuePacketToWorld(player.World, new SetEntityMetadataPacket
                {
                    EntityId = item.EntityId,
                    Entity = item
                });
                break;
            }
        }
    }

    private void DropItem(Player player, sbyte amountToRemove)
    {
        var droppedItem = player.GetHeldItem();

        if (droppedItem is null or { Type: Material.Air })
            return;

        var loc = new VectorF(player.Position.X, (float)player.HeadY - 0.3f, player.Position.Z);

        var item = new ItemEntity
        {
            EntityId = player + player.world.GetTotalLoadedEntities() + 1,
            Count = amountToRemove,
            Id = droppedItem.AsItem().Id,
            Glowing = true,
            World = player.world,
            PacketBroadcaster = player.PacketBroadcaster,
            Position = loc
        };

        player.world.TryAddEntity(item);

        var lookDir = player.GetLookDirection();

        var vel = Velocity.FromDirection(loc, lookDir);//TODO properly shoot the item towards the direction the players looking at

        player.PacketBroadcaster.QueuePacketToWorld(player.World, new SpawnEntityPacket
        {
            EntityId = item.EntityId,
            Uuid = item.Uuid,
            Type = EntityType.Item,
            Position = item.Position,
            Pitch = 0,
            Yaw = 0,
            Data = 1,
            Velocity = vel
        });
        player.PacketBroadcaster.QueuePacketToWorld(player.World, new SetEntityMetadataPacket
        {
            EntityId = item.EntityId,
            Entity = item
        });

        player.Inventory.RemoveItem(player.inventorySlot, amountToRemove);

        player.client.SendPacket(new SetContainerSlotPacket
        {
            Slot = player.inventorySlot,

            WindowId = 0,

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
