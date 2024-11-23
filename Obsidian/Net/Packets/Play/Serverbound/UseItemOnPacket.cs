using Obsidian.API.Events;
using Obsidian.Entities;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Registries;
using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Serverbound;

public partial class UseItemOnPacket
{
    [Field(0), ActualType(typeof(int)), VarLength]
    public Hand Hand { get; private set; } // Hand it was placed from. 0 = Main, 1 = Off

    [Field(1)]
    public Vector Position { get; private set; }

    [Field(2), ActualType(typeof(int)), VarLength]
    public BlockFace Face { get; private set; }

    [Field(3), DataFormat(typeof(float))]
    public VectorF Cursor { get; private set; }

    [Field(6)]
    public bool InsideBlock { get; private set; }

    [Field(7)]
    public bool IsWorldBorderHit { get; private set; }

    [Field(8), VarLength]
    public int Sequence { get; private set; }

    public override void Populate(INetStreamReader reader)
    {
        this.Hand = reader.ReadVarInt<Hand>();
        this.Position = reader.ReadPosition();
        this.Face = reader.ReadVarInt<BlockFace>();
        this.Cursor = reader.ReadAbsoluteFloatPositionF();
        this.InsideBlock = reader.ReadBoolean();
        this.IsWorldBorderHit = reader.ReadBoolean();
        this.Sequence = reader.ReadVarInt();
    }

    public async override ValueTask HandleAsync(Server server, Player player)
    {
        //Get main hand first return offhand if null
        var currentItem = player.GetHeldItem() ?? player.GetOffHandItem();
        var position = this.Position;

        var b = await player.world.GetBlockAsync(position);

        if (b is null)
            return;

        if (TagsRegistry.Block.PlayersCanInteract.Entries.Contains(b.RegistryId) && !player.Sneaking)
        {
            await server.EventDispatcher.ExecuteEventAsync(new PlayerInteractEventArgs(player, server)
            {
                Item = currentItem,
                Block = b,
                BlockLocation = this.Position,
            });

            return;
        }

        var itemType = currentItem != null ? currentItem.Type : Material.Air;

        switch (itemType)
        {
            case Material.WaterBucket:
                itemType = Material.Water;
                break;
            case Material.LavaBucket:
                itemType = Material.Lava;
                break;
            case Material.Air:
                return;
            default:
                break;
        }

        IBlock block;
        try
        {
            block = BlocksRegistry.Get(itemType);
        }
        catch //item is not a block so just return
        {
            return;
        }

        if (player.Gamemode != Gamemode.Creative)
            player.Inventory.RemoveItem(player.inventorySlot, 1);

        switch (Face) // TODO fix this for logs
        {
            case BlockFace.Down:
                position.Y -= 1;
                break;

            case BlockFace.Up:
                position.Y += 1;
                break;

            case BlockFace.North:
                position.Z -= 1;
                break;

            case BlockFace.South:
                position.Z += 1;
                break;

            case BlockFace.West:
                position.X -= 1;
                break;

            case BlockFace.East:
                position.X += 1;
                break;

            default:
                break;
        }

        if (TagsRegistry.Block.GravityAffected.Entries.Contains(block.RegistryId))
        {
            if (await player.World.GetBlockAsync(position + Vector.Down) is IBlock below &&
                (TagsRegistry.Block.ReplaceableByLiquid.Entries.Contains(below.RegistryId) || below.IsLiquid))
            {
                await player.world.SetBlockAsync(position, BlocksRegistry.Air, true);
                player.client.SendPacket(new BlockChangedAckPacket
                {
                    SequenceID = Sequence
                });
                player.world.SpawnFallingBlock(position, block.Material);
                return;
            }
        }

        await player.world.SetBlockAsync(position, block, doBlockUpdate: true);
        player.client.SendPacket(new BlockChangedAckPacket
        {
            SequenceID = Sequence
        });
    }
}
