using Obsidian.API.Events;
using Obsidian.Entities;
using Obsidian.Net.Packets.Play.Clientbound;

namespace Obsidian.Events;

public partial class MainEventHandler
{
    [EventPriority(Priority = Priority.Internal)]
    public async ValueTask OnBlockBreak(BlockBreakEventArgs args)
    {
        var player = args.Player;
        var sequence = args.Sequence;
        var block = args.Block;
        var world = player.Level;
        var location = args.Location;

        player.Client.SendPacket(new BlockChangedAckPacket
        {
            SequenceID = sequence
        });

        if (args.IsCancelled)
        {
            player.Client.SendPacket(new BlockUpdatePacket(location, block.GetHashCode()));
            return;
        }

        await world.SetBlockAsync(location, BlocksRegistry.Air, true);

        player.Client.SendPacket(new BlockUpdatePacket(location, BlocksRegistry.Air.GetHashCode()));

        world.PacketBroadcaster.QueuePacketToLevel(world, 0, new BlockDestructionPacket
        {
            EntityId = player.EntityId,
            Position = location,
            DestroyStage = -1
        }, player.EntityId);

        var droppedMaterial = block.Material;
        if (block.UnlocalizedName.StartsWith("minecraft:infested_", StringComparison.Ordinal))
        {
            if (player.Gamemode == Gamemode.Creative)
                return;
            var silkTouch = player.GetHeldItem() is { } tool && Obsidian.API.Loot.EnchantmentHelper.GetEnchantments(tool).Any(enchantment =>
                enchantment.Id == Obsidian.API.Registries.EnchantmentsRegistry.SilkTouch.Id && enchantment.Level > 0);
            if (!silkTouch)
            {
                if (world is Obsidian.WorldData.AbstractLevel level)
                    level.EnqueueEntityAction(() => { Silverfish.SpawnFromBlock(world, location); return default; });
                else
                    Silverfish.SpawnFromBlock(world, location);
                return;
            }
            if (Obsidian.WorldData.Structures.BlockStateParser.TryParse(block.UnlocalizedName.Replace("infested_", "", StringComparison.Ordinal)) is { } normal)
                droppedMaterial = normal.Material;
        }
        var droppedItem = ItemsRegistry.GetSingleItem(droppedMaterial);

        if (droppedItem.Type == Material.Air)
            return;

        var item = new ItemEntity
        {
            EntityId = Server.GetNextEntityId(),
            Item = droppedItem,
            Level = player.Level,
            Position = (VectorF)location + 0.5f,
        };

        player.Level.TryAddEntity(item);

        var power = GetRandDropVelocity();
        var direction = Globals.Random.NextSingle() * 6.2f;

        item.SpawnEntity(new Velocity(-Math.Sin(direction) * power, 0.2f, Math.Cos(direction) * power));
    }

    private static float GetRandDropVelocity()
    {
        var f = Globals.Random.NextSingle();

        return f * 0.5f;
    }
}
