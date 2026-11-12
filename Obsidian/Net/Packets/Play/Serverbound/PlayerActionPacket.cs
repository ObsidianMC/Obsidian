using Obsidian.API.Events;
using Obsidian.API.Inventory;
using Obsidian.Entities;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Registries;
using Obsidian.Serialization.Attributes;
using Obsidian.WorldData;

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

    public async override ValueTask HandleAsync(IServer server, IClientPlayer player)
    {
        // The acknowledgement follows the block changes the action makes, which go out with the next world tick.
        var acknowledging = player as PlayerBase;
        acknowledging?.BeginBlockAcknowledgementHold();
        try
        {
            await this.ActAsync(server, player);
        }
        finally
        {
            acknowledging?.EndBlockAcknowledgementHold();
        }
    }

    private async ValueTask ActAsync(IServer server, IClientPlayer player)
    {
        if (Status == PlayerActionStatus.ReleaseUseItem)
        {
            await player.ReleaseUsingItemAsync();
            return;
        }
        if (Status == PlayerActionStatus.Stab)
        {
            await player.StabAsync();
            return;
        }
        if (player is PlayerBase actor
            && Status is PlayerActionStatus.StartedDigging or PlayerActionStatus.FinishedDigging
                or PlayerActionStatus.ChangeDestroyDirection or PlayerActionStatus.CancelledDigging)
        {
            actor.QueueBlockAcknowledgement(this.Sequence);
        }

        if (await player.Level.GetBlockAsync(Position) is not IBlock block)
            return;

        // Like vanilla's ServerPlayerGameMode, a survival player starting to hit a block attacks it (a note block
        // plays), through the behaviour of the vanilla class that declares the block's attack.
        if (Status == PlayerActionStatus.StartedDigging
            && player.GameMode == GameMode.Survival
            && player is PlayerBase attacker
            && attacker.Level is Obsidian.WorldData.AbstractLevel attacked
            && BlocksRegistry.GetAttackBehavior(block.RegistryId) is IBlockAttackBehavior attack)
        {
            await attack.AttackAsync(attacked, Position, block, attacker);
        }

        if (Status == PlayerActionStatus.FinishedDigging || await this.BreaksAtOnceAsync(player, block))
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

    // Like vanilla's ServerPlayerGameMode: a creative player breaks a block as they start, and so does a survival player
    // whose first tick of mining breaks it all (grass by hand, leaves with shears, stone with a fast enough pickaxe).
    // Their client breaks those at once and only ever sends the start, never a finish. The progress reads the held
    // tool and its components, so it's taken while the inventory can't change under it; the water at the player's
    // eyes is read first, as the level's tick lock goes before the inventory's.
    private async ValueTask<bool> BreaksAtOnceAsync(IClientPlayer player, IBlock block)
    {
        // Air has nothing to break, in any game mode.
        if (this.Status != PlayerActionStatus.StartedDigging || player.GameMode == GameMode.Spectator || block.IsAir)
            return false;

        if (player.GameMode == GameMode.Creative)
            return true;

        if (player is not PlayerBase miner)
            return false;

        var eyeInWater = miner.IsEyeInWater();
        using (await player.Inventory.LockAsync())
            return miner.DestroyProgress(block, eyeInWater) >= 1;
    }

    private void BroadcastPlayerAction(IClientPlayer player)
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
            case PlayerActionStatus.ChangeDestroyDirection:
            case PlayerActionStatus.CancelledDigging:
                if (player is not PlayerBase)
                    player.Client.SendPacket(new BlockChangedAckPacket { SequenceID = this.Sequence });
                break;
            case PlayerActionStatus.FinishedDigging:
                {


                    break;
                }
        }
    }

    private static void DropItem(IClientPlayer player, int amountToRemove)
    {
        var droppedItem = player.GetHeldItem();

        if (droppedItem is null or { IsAir: true } or { Count: <= 0 })
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
    ChangeDestroyDirection,
    CancelledDigging,
    FinishedDigging,

    DropItemStack,
    DropItem,

    ReleaseUseItem,

    SwapItemInHand,

    Stab
}
