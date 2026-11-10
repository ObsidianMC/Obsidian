using Obsidian.API.Blocks;
using Obsidian.API.Inventory;
using Obsidian.WorldData.Features;

namespace Obsidian.WorldData;

internal static partial class UseBehaviors
{
    internal static IBlockUseBehavior CakeBlock { get; } = new CakeUse();

    internal static IBlockUseBehavior SweetBerryBushBlock { get; } = new BerryUse(false);

    internal static IBlockUseBehavior CaveVinesBlock { get; } = new BerryUse(true);

    internal static IBlockUseBehavior CaveVinesPlantBlock { get; } = new BerryUse(true);

    internal static IBlockUseBehavior PumpkinBlock { get; } = new PumpkinUse();

    internal static IBlockUseBehavior BeehiveBlock { get; } = new HiveUse();

    private sealed class CakeUse : IBlockUseBehavior
    {
        public ValueTask<UseResult> UseItemOnAsync(BlockUse use)
        {
            if (use.Block.GetProperty("bites") != "0" || !use.HasItem
                || !TagsRegistry.Item.Candles.Entries.Contains(use.Item!.Holder.Id))
            {
                return ValueTask.FromResult(UseResult.TryWithEmptyHand);
            }

            var name = use.Item.Holder.UnlocalizedName + "_cake";
            var cake = BlockStateProperties.GetState(name);
            if (!use.TryCommit(
                () => use.Level.BlockUpdates.SetBlock(use.Position, cake, BlockUpdateFlags.All),
                consumeItem: true))
            {
                return ValueTask.FromResult(UseResult.Fail);
            }

            BlockLighting.Play(use.Level, use.Position, "minecraft:block.cake.add_candle");
            use.Level.EmitGameEvent(MobGameEvent.BlockChange, use.Center, use.Player);
            use.Player.Statistics.Increment(use.UsedItem!.Holder.UnlocalizedName, type: "minecraft:used");
            return ValueTask.FromResult(UseResult.Success);
        }

        public ValueTask<UseResult> UseWithoutItemAsync(BlockUse use) => EatCakeAsync(use, candle: false);
    }

    internal static ValueTask<UseResult> EatCakeAsync(BlockUse use, bool candle)
    {
        var player = use.Player;
        if (player.FoodLevel >= 20 && player.GameMode != GameMode.Creative)
            return ValueTask.FromResult(UseResult.Pass);

        var bites = candle ? 0 : int.Parse(use.Block.GetProperty("bites")!);
        var next = bites == 6 ? BlocksRegistry.Air : BlocksRegistry.Cake.WithProperty("bites", bites + 1);
        if (!use.TryCommit(() =>
        {
            if (player.FoodLevel >= 20 && player.GameMode != GameMode.Creative)
                return false;

            if (!use.Level.BlockUpdates.SetBlock(use.Position, next, BlockUpdateFlags.All))
                return false;

            player.FoodLevel = Math.Min(20, player.FoodLevel + 2);
            player.FoodSaturationLevel = Math.Min(player.FoodLevel, player.FoodSaturationLevel + 0.4f);
            return true;
        }))
        {
            return ValueTask.FromResult(UseResult.Fail);
        }

        use.Award("minecraft:eat_cake_slice");
        use.Level.EmitGameEvent(MobGameEvent.Eat, use.Center, player);
        if (bites == 6)
            use.Level.EmitGameEvent(MobGameEvent.BlockDestroy, use.Center, player);

        if (candle)
            FoodHarvest.DropCandle(use);

        return ValueTask.FromResult(UseResult.Success);
    }

    private sealed class BerryUse(bool cave) : IBlockUseBehavior
    {
        public ValueTask<UseResult> UseItemOnAsync(BlockUse use)
        {
            var growing = !cave && use.Block.GetProperty("age") != "3";
            var result = growing && use.Item?.Holder.Id == ItemIds.BoneMeal
                ? UseResult.Pass : UseResult.TryWithEmptyHand;
            return ValueTask.FromResult(result);
        }

        public ValueTask<UseResult> UseWithoutItemAsync(BlockUse use)
        {
            var ripe = cave ? use.Block.GetProperty("berries") == "true"
                : int.Parse(use.Block.GetProperty("age")!) > 1;
            if (!ripe)
                return ValueTask.FromResult(UseResult.Pass);

            var next = cave ? use.Block.WithProperty("berries", false) : use.Block.WithProperty("age", 1);
            if (!use.TryCommit(() =>
            {
                FoodHarvest.QueueLoot(use, cave ? "harvest/cave_vine" : "harvest/sweet_berry_bush");
                return use.Level.BlockUpdates.SetBlock(use.Position, next, BlockUpdateFlags.Clients);
            }))
            {
                return ValueTask.FromResult(UseResult.Fail);
            }

            var sound = cave ? "minecraft:block.cave_vines.pick_berries"
                : "minecraft:block.sweet_berry_bush.pick_berries";
            // CaveVines uses randomBetween; the subtraction's float rounding differs from the bush's literal.
            var pitchRange = cave ? 1.2f - 0.8f : 0.4f;
            var pitch = 0.8f + use.Level.GrowthRandom.NextFloat() * pitchRange;
            BlockLighting.Play(use.Level, use.Position, sound, pitch: pitch);
            use.Level.EmitGameEvent(MobGameEvent.BlockChange, use.Center, use.Player, affectedBlock: next);
            return ValueTask.FromResult(UseResult.Success);
        }
    }

    private sealed class PumpkinUse : IBlockUseBehavior
    {
        public ValueTask<UseResult> UseItemOnAsync(BlockUse use)
        {
            if (use.Item?.Holder.Id != ItemIds.Shears)
                return ValueTask.FromResult(UseResult.TryWithEmptyHand);

            var face = use.Face is BlockFace.Up or BlockFace.Down
                ? PlacementDirections.Horizontal(use.Player.Yaw.Degrees).Opposite() : use.Face;
            var carved = BlocksRegistry.CarvedPumpkin.WithProperty("facing", face.ToString().ToLowerInvariant());
            if (!use.TryCommit(() =>
            {
                FoodHarvest.QueueLoot(use, "carve/pumpkin", face);
                use.Level.BlockUpdates.SetBlock(use.Position, carved, BlockUpdateFlags.AllImmediate);
                FoodHarvest.WearShears(use);
                return true;
            }))
            {
                return ValueTask.FromResult(UseResult.Fail);
            }

            BlockLighting.Play(use.Level, use.Position, "minecraft:block.pumpkin.carve");
            use.Level.EmitGameEvent(MobGameEvent.Shear, use.Center, use.Player);
            use.Player.Statistics.Increment(use.UsedItem!.Holder.UnlocalizedName, type: "minecraft:used");
            return ValueTask.FromResult(UseResult.Success);
        }
    }

    private sealed class HiveUse : IBlockUseBehavior
    {
        public ValueTask<UseResult> UseItemOnAsync(BlockUse use)
        {
            var bottle = use.Item?.Holder.Id == ItemIds.GlassBottle;
            if (use.Block.GetProperty("honey_level") != "5"
                || !bottle && use.Item?.Holder.Id != ItemIds.Shears)
            {
                return ValueTask.FromResult(UseResult.TryWithEmptyHand);
            }

            if (!use.TryCommit(() =>
            {
                if (bottle)
                {
                    // A hive consumes a glass bottle in every game mode, unlike a bucket or washed item.
                    BlockUseReturn.Exchange(use, new ItemStack(ItemsRegistry.HoneyBottle, 1), consumeInCreative: true);
                }
                else
                {
                    FoodHarvest.QueueLoot(use, "harvest/beehive");
                    FoodHarvest.WearShears(use);
                }

                var level = use.Level;
                var position = use.Player.Position;
                var sound = bottle ? "minecraft:item.bottle.fill" : "minecraft:block.beehive.shear";
                var excluded = bottle ? use.Player.EntityId : -1;
                level.ActiveBlockUse!.After(() => FoodHarvest.PlayAtPlayer(use, sound, position, excluded));
                level.EmitGameEvent(bottle ? MobGameEvent.FluidPickup : MobGameEvent.Shear, use.Center, use.Player);
                var smoky = CampfireSmoke.Reaches(level, use.Position);
                level.BlockUpdates.SetBlock(use.Position, use.Block.WithProperty("honey_level", 0), BlockUpdateFlags.All);
                if (!smoky)
                    FoodHarvest.ReleaseBees(use);

                return true;
            }))
            {
                return ValueTask.FromResult(UseResult.Fail);
            }

            use.Player.Statistics.Increment(use.UsedItem!.Holder.UnlocalizedName, type: "minecraft:used");
            return ValueTask.FromResult(UseResult.Success);
        }
    }
}
