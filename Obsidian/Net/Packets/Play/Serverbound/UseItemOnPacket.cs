using Obsidian.API.Events;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.API.Inventory;
using Obsidian.API.Inventory.DataComponents;
using Obsidian.WorldData;
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
    public VectorF Cursor { get; private set; }

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
        var currentItem = this.Hand == InteractionHand.MainHand ? player.GetHeldItem() : player.GetOffHandItem();
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

        if (player.Level is AbstractLevel level && currentItem is { Count: > 0 } &&
            currentItem.Type is Material.EnderEye or Material.FlintAndSteel or Material.FireCharge)
        {
            var used = false;
            if (player.GameMode != GameMode.Spectator && (uint)this.Face <= (uint)BlockFace.East)
            {
                if (currentItem.Type == Material.EnderEye)
                    used = await level.Portals.TryInsertEyeAsync(position);
                else if (player.GameMode != GameMode.Adventure)
                {
                    var firePosition = position + this.Face.ToVector();
                    var existing = await level.GetBlockAsync(firePosition);
                    if (existing is { IsAir: true } && !level.IsOutsideBuildHeight(firePosition.Y))
                    {
                        used = await level.Portals.TryIgniteAsync(firePosition);
                        if (!used && await level.GetBlockAsync(firePosition + Vector.Down) is { } below && below.HasFullTopCollisionFace())
                        {
                            await level.SetBlockAsync(firePosition, BlocksRegistry.Get(
                                below.Material is Material.SoulSand or Material.SoulSoil ? Material.SoulFire : Material.Fire), true);
                            used = true;
                        }
                    }
                }
            }
            if (used)
                await this.ConsumePortalItemAsync(player, currentItem);
            player.Client.SendPacket(new BlockChangedAckPacket { SequenceID = this.Sequence });
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

        if (player.GameMode != GameMode.Creative)
            player.Inventory.RemoveItem(this.Hand == InteractionHand.MainHand ? player.CurrentHeldItemSlot : (short)45, 1);

        if (block.Material == Material.EndPortalFrame)
        {
            string[] facings = ["north", "east", "south", "west"];
            block = block.WithProperty("facing", facings[(int)Math.Floor((player.Yaw.Degrees + 45) / 90) % 4]);
        }

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
    private async ValueTask ConsumePortalItemAsync(IPlayer player, ItemStack item)
    {
        var slot = this.Hand == InteractionHand.MainHand ? player.CurrentHeldItemSlot : (short)45;
        if (item.Type == Material.EnderEye || player.GameMode != GameMode.Creative)
        {
            if (item.Type != Material.FlintAndSteel)
                player.Inventory.RemoveItem(slot, 1);
            else if (!item.Unbreakable && ShouldDamageTool(item))
            {
                var damage = ComponentBuilder.Damage;
                damage.Value = item.Damage + 1;
                item[DataComponentType.Damage] = damage;
                var maxDamage = item.GetComponent<SimpleDataComponent<int>>(DataComponentType.MaxDamage)?.Value ?? item.Holder.MaxDamage;
                if (damage.Value >= maxDamage)
                    player.Inventory.RemoveItem(slot, 1);
            }
        }
        await player.Client.QueuePacketAsync(new ContainerSetSlotPacket
        {
            Slot = slot,
            StateId = player.Inventory.StateId++,
            SlotData = player.Inventory.GetItem(slot)
        });
    }

    private static bool ShouldDamageTool(ItemStack item)
    {
        var enchantments = item.GetComponent<SimpleDataComponent<Enchantment[]>>(DataComponentType.Enchantments)?.Value;
        var unbreaking = enchantments?.FirstOrDefault(enchantment => enchantment.Id == EnchantmentsRegistry.Unbreaking.Id).Level ?? 0;
        return Globals.Random.NextDouble() < 1.0 / (Math.Max(0, unbreaking) + 1.0);
    }
}
