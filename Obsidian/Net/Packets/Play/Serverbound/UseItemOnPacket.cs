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
    public InteractionHand Hand { get; private set; } // Hand it was placed from. 0 = Main, 1 = Off

    [Field(1)]
    public Vector Position { get; private set; }

    [Field(2), ActualType(typeof(int)), VarLength]
    public BlockFace Face { get; private set; }

    [Field(3), DataFormat(typeof(float))]
    public VectorD Cursor { get; private set; }

    [Field(6)]
    public bool InsideBlock { get; private set; }

    [Field(7)]
    public bool IsWorldBorderHit { get; private set; }

    [Field(8), VarLength]
    public int Sequence { get; private set; }

    public override void Populate(INetStreamReader reader)
    {
        this.Hand = reader.ReadVarInt<InteractionHand>();
        this.Position = reader.ReadPosition();
        this.Face = reader.ReadVarInt<BlockFace>();
        this.Cursor = reader.ReadAbsoluteFloatPositionF();
        this.InsideBlock = reader.ReadBoolean();
        this.IsWorldBorderHit = reader.ReadBoolean();
        this.Sequence = reader.ReadVarInt();
    }

    public async override ValueTask HandleAsync(IServer server, IPlayer player)
    {
        var handSlot = Hand == InteractionHand.OffHand ? 45 : player.CurrentHeldItemSlot;
        var bucket = player.Inventory.GetItem(handSlot);
        if (bucket is { Count: > 0, Type: Material.AxolotlBucket or Material.TadpoleBucket } && player.Health > 0 && player.GameMode != GameMode.Spectator)
        {
            var destination = Position + Face.ToVector();
            if ((player.Position - (VectorD)destination).MagnitudeSquared() > 36 || player.Level is not Obsidian.WorldData.AbstractLevel level || level.IsOutsideBuildHeight(destination.Y)) return;
            var terrain = new Obsidian.Entities.AI.MobTerrain(level);
            var existing = terrain.GetBlock(destination);
            if (existing == null || !existing.IsAir && existing.Material != Material.Water) return;
            var type = bucket.Type == Material.AxolotlBucket ? EntityType.Axolotl : EntityType.Tadpole;
            var mob = Obsidian.Entities.Factories.EntitySpawner.CreateMob(level, type)!;
            mob.EntityId = Server.GetNextEntityId();
            mob.Position = (VectorD)destination + new VectorD(0.5f, 0, 0.5f);
            if (!terrain.IsFree(mob.Dimension.CreateBBFromPosition(mob.Position))) return;
            await level.SetBlockAsync(destination, BlocksRegistry.Water, true);
            mob.InitializeAi();
            var saved = Obsidian.Entities.EntityNbt.Save(mob)!;
            if (bucket.GetComponent<Obsidian.API.Inventory.DataComponents.BucketEntityDataComponent>(DataComponentType.BucketEntityData) is { } component)
                foreach (var (name, value) in component.Value)
                    if (name is not ("id" or "UUID" or "Pos" or "Motion" or "Rotation")) { saved.Remove(name); saved.Add(name, value); }
            saved.Remove("FromBucket");
            saved.Add(new Obsidian.Nbt.NbtTag<bool>("FromBucket", true));
            mob.ReadSave(saved);
            mob.PersistenceRequired = true;
            level.SpawnEntity(mob);
            if (player.GameMode != GameMode.Creative)
            {
                player.Inventory.SetItem(handSlot, ItemsRegistry.GetSingleItem(Material.Bucket));
                await player.Client.QueuePacketAsync(new ContainerSetSlotPacket { ContainerId = 0, Slot = (short)handSlot, SlotData = player.Inventory.GetItem(handSlot) });
            }
            player.Client.SendPacket(new BlockChangedAckPacket { SequenceID = Sequence });
            return;
        }
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
        if (player is Obsidian.Entities.Player eatingPlayer)
            eatingPlayer.StartEating(Hand);

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

        if (player.GameMode != GameMode.Creative)
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
