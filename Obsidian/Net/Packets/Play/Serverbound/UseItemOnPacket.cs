using Obsidian.API.Events;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Serverbound;

public partial class UseItemOnPacket
{
    // Blocks whose screen opens through the interact event when used without sneaking. Vanilla has no tag for these.
    private static readonly HashSet<Material> interactableBlocks =
    [
        Material.Chest, Material.EnderChest, Material.TrappedChest, Material.Anvil, Material.ChippedAnvil,
        Material.DamagedAnvil, Material.Hopper, Material.Smoker, Material.Furnace, Material.CraftingTable,
        Material.Barrel, Material.BlastFurnace, Material.Grindstone, Material.BrewingStand
    ];

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

    public async override ValueTask HandleAsync(IServer server, IPlayer player)
    {
        //Get main hand first return offhand if null
        var currentItem = player.GetHeldItem() ?? player.GetOffHandItem();
        var position = this.Position;

        var b = await player.Level.GetBlockAsync(position);

        if (b is null)
            return;

        if (interactableBlocks.Contains(b.Material) && !player.Sneaking)
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
            player.Inventory.RemoveItem(player.CurrentHeldItemSlot, 1);

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

        if (block.IsGravityAffected())
        {
            if (await player.Level.GetBlockAsync(position + Vector.Down) is IBlock below &&
                below.IsFreeForFallingBlock())
            {
                await player.Level.SetBlockAsync(position, BlocksRegistry.Air, true);
                player.Client.SendPacket(new BlockChangedAckPacket
                {
                    SequenceID = Sequence
                });
                player.Level.SpawnFallingBlock(position, block.Material);
                return;
            }
        }

        await player.Level.SetBlockAsync(position, block, doBlockUpdate: true);
        player.Client.SendPacket(new BlockChangedAckPacket
        {
            SequenceID = Sequence
        });
    }
}
