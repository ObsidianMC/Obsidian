using Obsidian.API;
using Obsidian.API.Events;
using Obsidian.API.Inventory;
using Obsidian.API.Inventory.DataComponents;
using Obsidian.API.Loot;
using Obsidian.API.Registries;
using Obsidian.Entities;
using Obsidian.Events;
using Obsidian.Nbt;
using Obsidian.Registries;
using Obsidian.Tests.Fakes;
using Obsidian.WorldData;
using Obsidian.WorldData.Fluids;
using Obsidian.API.Utilities;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Obsidian.Tests;

/// <summary>
/// Destruction through the real break handler and explosion drops: tool wear, block entity components, unsupported block
/// entity data, and explosions sharing the drop service.
/// </summary>
public class BlockDestructionIntegration
{
    private static readonly Vector Spot = BlockBreakIntegration.Spot;

    [Fact]
    public async Task BreakingWithAPickaxeWearsIt()
    {
        await using var context = TestServerFactory.CreateServer("wear-break");
        var player = TestServerFactory.CreateConnectedPlayer(context.Server, context.World, "Wear");
        player.Inventory.SetItem(player.CurrentHeldItemSlot, new ItemStack(ItemsRegistry.DiamondPickaxe, 1));

        await BreakAsync(context, player, "minecraft:stone");

        Assert.Equal(1, player.Inventory.GetItem(player.CurrentHeldItemSlot).Damage);
    }

    [Fact]
    public async Task CreativeBreakingDoesNotWearTheTool()
    {
        await using var context = TestServerFactory.CreateServer("wear-creative");
        var player = TestServerFactory.CreateConnectedPlayer(context.Server, context.World, "Creative");
        player.GameMode = GameMode.Creative;
        player.Inventory.SetItem(player.CurrentHeldItemSlot, new ItemStack(ItemsRegistry.DiamondPickaxe, 1));

        await BreakAsync(context, player, "minecraft:stone");

        Assert.Equal(0, player.Inventory.GetItem(player.CurrentHeldItemSlot).Damage);
    }

    [Fact]
    public async Task InstantBlocksDoNotWearTheTool()
    {
        await using var context = TestServerFactory.CreateServer("wear-instant");
        var player = TestServerFactory.CreateConnectedPlayer(context.Server, context.World, "Instant");
        player.Inventory.SetItem(player.CurrentHeldItemSlot, new ItemStack(ItemsRegistry.DiamondPickaxe, 1));

        await BreakAsync(context, player, "minecraft:dandelion");

        Assert.Equal(0, player.Inventory.GetItem(player.CurrentHeldItemSlot).Damage);
    }

    [Fact]
    public async Task NamedChestKeepsItsNameOnTheDrop()
    {
        await using var context = TestServerFactory.CreateServer("chest-name");
        var player = TestServerFactory.CreateConnectedPlayer(context.Server, context.World, "Chest");
        player.Inventory.SetItem(player.CurrentHeldItemSlot, new ItemStack(ItemsRegistry.DiamondPickaxe, 1));

        var chest = new DataBlockEntity { Id = "minecraft:chest", BlockPosition = Spot };
        chest.Set(new NbtTag<string>("CustomName", "Vault"));

        var drop = await BreakForDrop(context, player, "minecraft:chest", chest);

        Assert.Equal("Vault", drop.GetComponent<SimpleDataComponent<ChatMessage>>(DataComponentType.CustomName)?.Value.Text);
    }

    [Fact]
    public async Task ShulkerBoxKeepsItsContentsOnTheDrop()
    {
        await using var context = TestServerFactory.CreateServer("shulker-contents");
        var player = TestServerFactory.CreateConnectedPlayer(context.Server, context.World, "Shulker");
        player.Inventory.SetItem(player.CurrentHeldItemSlot, new ItemStack(ItemsRegistry.DiamondPickaxe, 1));

        var shulker = new DataBlockEntity { Id = "minecraft:shulker_box", BlockPosition = Spot };
        shulker.Set(new NbtTag<string>("CustomName", "Vault"));
        var contents = new NbtList(NbtTagType.Compound, "Items");
        var diamonds = new ItemStack(ItemsRegistry.Diamond, 2).ToNbt();
        diamonds.Add(new NbtTag<byte>("Slot", 0));
        contents.Add(diamonds);
        shulker.Set(contents);

        var drop = await BreakForDrop(context, player, "minecraft:shulker_box", shulker);

        Assert.Equal("Vault", drop.GetComponent<SimpleDataComponent<ChatMessage>>(DataComponentType.CustomName)?.Value.Text);
        var container = drop.GetComponent<SimpleDataComponent<ItemStack[]>>(DataComponentType.Container)?.Value;
        Assert.NotNull(container);
        Assert.Equal(["minecraft:diamond*2"], container.Where(stack => !stack.IsAir).Select(stack => $"{stack.Holder.UnlocalizedName}*{stack.Count}"));
    }

    [Fact]
    public async Task UnopenedLootShulkerBoxCopiesItsLootTable()
    {
        await using var context = TestServerFactory.CreateServer("shulker-loot");
        var player = TestServerFactory.CreateConnectedPlayer(context.Server, context.World, "Loot");
        player.Inventory.SetItem(player.CurrentHeldItemSlot, new ItemStack(ItemsRegistry.DiamondPickaxe, 1));

        var shulker = new DataBlockEntity { Id = "minecraft:shulker_box", BlockPosition = Spot };
        shulker.Set(new NbtTag<string>("LootTable", "minecraft:chests/simple_dungeon"));
        shulker.Set(new NbtTag<long>("LootTableSeed", 7));

        var drop = await BreakForDrop(context, player, "minecraft:shulker_box", shulker);

        var loot = drop.GetComponent<SimpleDataComponent<NbtCompound>>(DataComponentType.ContainerLoot)?.Value;
        Assert.NotNull(loot);
        Assert.True(loot.TryGetTag<NbtTag<string>>("loot_table", out var table));
        Assert.Equal("minecraft:chests/simple_dungeon", table.Value);
        Assert.True(loot.TryGetTag<NbtTag<long>>("seed", out var seed));
        Assert.Equal(7L, seed.Value);
    }

    [Fact]
    public async Task BeehiveKeepsItsBeesOnTheDrop()
    {
        await using var context = TestServerFactory.CreateServer("hive-bees");
        var player = TestServerFactory.CreateConnectedPlayer(context.Server, context.World, "Hive");

        var hive = new DataBlockEntity { Id = "minecraft:beehive", BlockPosition = Spot };
        var bee = new NbtCompound("entity_data") { new NbtTag<string>("id", "minecraft:bee"), new NbtTag<float>("Health", 10f) };
        var occupants = new NbtList(NbtTagType.Compound, "bees")
        {
            new NbtCompound { bee, new NbtTag<int>("ticks_in_hive", 120), new NbtTag<int>("min_ticks_in_hive", 600) }
        };
        hive.Set(occupants);

        var silk = new ItemStack(ItemsRegistry.DiamondPickaxe, 1);
        EnchantmentHelper.Enchant(silk, EnchantmentsRegistry.SilkTouch, 1);
        player.Inventory.SetItem(player.CurrentHeldItemSlot, silk);

        var drop = await BreakForDrop(context, player, "minecraft:beehive", hive);

        var bees = drop.GetComponent<BeesDataComponent>(DataComponentType.Bees)!;
        var occupant = Assert.Single(bees.Occupants);
        Assert.Equal((int)EntityType.Bee, occupant.Entity.RegistryId);
        Assert.Equal(120, occupant.TicksInHive);
        Assert.Equal(600, occupant.MinTicksInHive);
        // The type is in the registry ID, as vanilla's TypedEntityData keeps it, so the saved data drops the "id" key.
        Assert.False(occupant.Entity.Data.HasTag("id"));
        Assert.Equal(10f, occupant.Entity.Data.GetFloat("Health"));
    }

    [Fact]
    public async Task CrackedPotDropsItsFourSherdsInSideOrder()
    {
        await using var context = TestServerFactory.CreateServer("pot-cracked");
        var player = TestServerFactory.CreateConnectedPlayer(context.Server, context.World, "Pot");
        player.Inventory.SetItem(player.CurrentHeldItemSlot, new ItemStack(ItemsRegistry.DiamondPickaxe, 1));

        var pot = new DataBlockEntity { Id = "minecraft:decorated_pot", BlockPosition = Spot };
        pot.Set(Sherds("minecraft:arms_up_pottery_sherd", "minecraft:brewer_pottery_sherd", "minecraft:archer_pottery_sherd", "minecraft:brick"));

        var cracked = BlockStateProperties.GetState("minecraft:decorated_pot", new Dictionary<string, string> { ["cracked"] = "true" });
        await context.World.SetBlockAsync(Spot, cracked, true);
        await context.World.SetBlockEntity(Spot, pot);

        // The service returns the loot in order; the spawned entities aren't kept in spawn order, so they're compared as a set.
        var ordered = await BlockDrops.ForPlayerBreakAsync(context.World, cracked, Spot, new ItemStack(ItemsRegistry.DiamondPickaxe, 1), player);
        Assert.Equal(
            new[] { ItemsRegistry.ArmsUpPotterySherd.Id, ItemsRegistry.BrewerPotterySherd.Id, ItemsRegistry.ArcherPotterySherd.Id, ItemsRegistry.Brick.Id },
            ordered.Select(drop => drop.Holder.Id));
        Assert.All(ordered, drop => Assert.Equal(1, drop.Count));

        var spawned = await BreakForDrops(context, player, cracked, pot);
        Assert.Equal(ordered.Select(drop => drop.Holder.Id).OrderBy(id => id), spawned.Select(drop => drop.Holder.Id).OrderBy(id => id));
    }

    [Fact]
    public async Task CrackedPotWithNoSavedSidesDropsFourBricks()
    {
        await using var context = TestServerFactory.CreateServer("pot-bare");
        var player = TestServerFactory.CreateConnectedPlayer(context.Server, context.World, "Pot");
        player.Inventory.SetItem(player.CurrentHeldItemSlot, new ItemStack(ItemsRegistry.DiamondPickaxe, 1));

        var pot = new DataBlockEntity { Id = "minecraft:decorated_pot", BlockPosition = Spot };
        var cracked = BlockStateProperties.GetState("minecraft:decorated_pot", new Dictionary<string, string> { ["cracked"] = "true" });
        var drops = await BreakForDrops(context, player, cracked, pot);

        Assert.Equal(new[] { ItemsRegistry.Brick.Id, ItemsRegistry.Brick.Id, ItemsRegistry.Brick.Id, ItemsRegistry.Brick.Id }, drops.Select(drop => drop.Holder.Id));
    }

    [Fact]
    public async Task IntactPotCopiesItsSidesAsAComponent()
    {
        await using var context = TestServerFactory.CreateServer("pot-intact");
        var player = TestServerFactory.CreateConnectedPlayer(context.Server, context.World, "Pot");
        player.Inventory.SetItem(player.CurrentHeldItemSlot, new ItemStack(ItemsRegistry.DiamondPickaxe, 1));

        var pot = new DataBlockEntity { Id = "minecraft:decorated_pot", BlockPosition = Spot };
        pot.Set(Sherds("minecraft:arms_up_pottery_sherd", "minecraft:brewer_pottery_sherd", "minecraft:archer_pottery_sherd", "minecraft:brick"));

        var intact = BlockStateProperties.GetState("minecraft:decorated_pot", new Dictionary<string, string> { ["cracked"] = "false" });
        var drop = await BreakForDrop(context, player, intact, pot);

        Assert.Equal(ItemsRegistry.DecoratedPot.Id, drop.Holder.Id);
        var sides = drop.GetComponent<SimpleDataComponent<Item[]>>(DataComponentType.PotDecorations)!.Value;
        Assert.Equal(new[] { ItemsRegistry.ArmsUpPotterySherd.Id, ItemsRegistry.BrewerPotterySherd.Id, ItemsRegistry.ArcherPotterySherd.Id, ItemsRegistry.Brick.Id },
            sides.Select(side => side.Id));
    }

    [Fact]
    public async Task LockedShulkerBoxCopiesItsLock()
    {
        await using var context = TestServerFactory.CreateServer("shulker-lock");
        var player = TestServerFactory.CreateConnectedPlayer(context.Server, context.World, "Lock");
        player.Inventory.SetItem(player.CurrentHeldItemSlot, new ItemStack(ItemsRegistry.DiamondPickaxe, 1));

        var shulker = new DataBlockEntity { Id = "minecraft:shulker_box", BlockPosition = Spot };
        shulker.Set(new NbtCompound("lock") { new NbtTag<string>("key", "gold") });
        var drop = await BreakForDrop(context, player, "minecraft:shulker_box", shulker);

        var lock_ = drop.GetComponent<SimpleDataComponent<NbtCompound>>(DataComponentType.Lock)!;
        Assert.Equal("gold", lock_.Value.GetString("key"));
    }

    [Fact]
    public async Task PlayerHeadCopiesItsProfileAndNoteSound()
    {
        await using var context = TestServerFactory.CreateServer("head-profile");
        var player = TestServerFactory.CreateConnectedPlayer(context.Server, context.World, "Head");
        player.Inventory.SetItem(player.CurrentHeldItemSlot, new ItemStack(ItemsRegistry.DiamondPickaxe, 1));

        var head = new DataBlockEntity { Id = "minecraft:skull", BlockPosition = Spot };
        head.Set(new NbtCompound("profile")
        {
            new NbtTag<string>("name", "Steve"),
            new NbtArray<int>("id", [0x12345678, unchecked((int)0x9abcdef0), 0x0fedcba9, unchecked((int)0x87654321)]),
            new NbtList(NbtTagType.Compound, "properties") { new NbtCompound { new NbtTag<string>("name", "textures"), new NbtTag<string>("value", "abc"), new NbtTag<string>("signature", "sig") } }
        });
        head.Set(new NbtTag<string>("note_block_sound", "minecraft:block.note_block.harp"));

        var drop = await BreakForDrop(context, player, "minecraft:player_head", head);

        var profile = drop.GetComponent<ProfileDataComponent>(DataComponentType.Profile)!;
        Assert.Equal("Steve", profile.Username);
        Assert.Equal(System.Guid.Parse("12345678-9abc-def0-0fed-cba987654321"), profile.Id);
        Assert.True(profile.IsComplete);
        var property = Assert.Single(profile.Properties);
        Assert.Equal(("textures", "abc", "sig"), (property.Name, property.Value, property.Signature));
        Assert.Equal("minecraft:block.note_block.harp", drop.GetComponent<SimpleDataComponent<string>>(DataComponentType.NoteBlockSound)!.Value);
    }

    [Fact]
    public async Task BannerKeepsItsPatternsOnTheDrop()
    {
        await using var context = TestServerFactory.CreateServer("banner-patterns");
        var player = TestServerFactory.CreateConnectedPlayer(context.Server, context.World, "Banner");
        player.Inventory.SetItem(player.CurrentHeldItemSlot, new ItemStack(ItemsRegistry.DiamondPickaxe, 1));

        var banner = new DataBlockEntity { Id = "minecraft:banner", BlockPosition = Spot };
        banner.Set(new NbtList(NbtTagType.Compound, "patterns")
        {
            new NbtCompound { new NbtTag<string>("pattern", "minecraft:stripe_top"), new NbtTag<string>("color", "light_blue") },
            new NbtCompound { new NbtTag<string>("pattern", "minecraft:creeper"), new NbtTag<string>("color", "black") },
        });

        var drop = await BreakForDrop(context, player, "minecraft:white_banner", banner);

        var layers = drop.GetComponent<BannerPatternsDataComponent>(DataComponentType.BannerPatterns)!.Value;
        Assert.Equal(2, layers.Length);
        Assert.Equal(("minecraft:stripe_top", "block.minecraft.banner.stripe_top", Dye.LightBlue), (layers[0].AssetId, layers[0].TranslationKey, layers[0].Color));
        Assert.Equal(Dye.Black, layers[1].Color);
    }

    [Fact]
    public async Task BannerWithAnUndefinedPatternDropsNothingAndStillBreaks()
    {
        await using var context = TestServerFactory.CreateServer("banner-unknown");
        var player = TestServerFactory.CreateConnectedPlayer(context.Server, context.World, "Banner");
        player.Inventory.SetItem(player.CurrentHeldItemSlot, new ItemStack(ItemsRegistry.DiamondPickaxe, 1));

        var banner = new DataBlockEntity { Id = "minecraft:banner", BlockPosition = Spot };
        banner.Set(new NbtList(NbtTagType.Compound, "patterns")
        {
            new NbtCompound { new NbtTag<string>("pattern", "minecraft:not_a_pattern"), new NbtTag<string>("color", "red") },
        });

        var drops = await BreakAsync(context, player, "minecraft:white_banner", banner);

        Assert.Empty(drops);
        Assert.True((await context.World.GetBlockAsync(Spot))!.IsAir);
    }

    private static DataBlockEntity SavedDiamonds(string id)
    {
        var entity = new DataBlockEntity { Id = id, BlockPosition = Spot };
        var contents = new NbtList(NbtTagType.Compound, "Items");
        var diamonds = new ItemStack(ItemsRegistry.Diamond, 2).ToNbt();
        diamonds.Add(new NbtTag<byte>("Slot", 0));
        contents.Add(diamonds);
        entity.Set(contents);
        return entity;
    }

    private static NbtList Sherds(params string[] items)
    {
        var list = new NbtList(NbtTagType.String, "sherds");
        foreach (var item in items)
            list.Add(new NbtTag<string>(null, item));
        return list;
    }

    private static async Task<List<ItemStack>> BreakForDrops(RealServerContext context, Obsidian.Entities.Player player, IBlock block, DataBlockEntity blockEntity)
    {
        await context.World.SetBlockAsync(Spot, block, true);
        await context.World.SetBlockEntity(Spot, blockEntity);
        await new MainEventHandler().OnBlockBreak(BlockBreakIntegration.CreateArgs(context, player, block, Spot));
        return context.World.GetEntitiesInRange(new VectorD(Spot.X + 0.5, Spot.Y + 0.5, Spot.Z + 0.5), 4f)
            .OfType<ItemEntity>()
            .Select(entity => entity.Item)
            .ToList();
    }

    [Fact]
    public async Task InfestedStoneSpawnsASilverfishUnlessSilkTouched()
    {
        await using var context = TestServerFactory.CreateServer("infested");
        var player = TestServerFactory.CreateConnectedPlayer(context.Server, context.World, "Infested");
        player.Inventory.SetItem(player.CurrentHeldItemSlot, new ItemStack(ItemsRegistry.DiamondPickaxe, 1));

        var drops = await BreakAsync(context, player, "minecraft:infested_stone");
        await ((AbstractLevel)context.World).DoWorldTickAsync();

        Assert.Empty(drops);
        Assert.Single(context.World.GetEntitiesInRange(new VectorD(Spot.X + 0.5, Spot.Y + 0.5, Spot.Z + 0.5), 4f).OfType<Silverfish>());
    }

    [Fact]
    public async Task SilkTouchedInfestedStoneSpawnsNoSilverfish()
    {
        await using var context = TestServerFactory.CreateServer("infested-silk");
        var player = TestServerFactory.CreateConnectedPlayer(context.Server, context.World, "Silk");
        var pickaxe = new ItemStack(ItemsRegistry.DiamondPickaxe, 1);
        EnchantmentHelper.Enchant(pickaxe, EnchantmentsRegistry.SilkTouch, 1);
        player.Inventory.SetItem(player.CurrentHeldItemSlot, pickaxe);

        await BreakAsync(context, player, "minecraft:infested_stone");
        await ((AbstractLevel)context.World).DoWorldTickAsync();

        Assert.Empty(context.World.GetEntitiesInRange(new VectorD(Spot.X + 0.5, Spot.Y + 0.5, Spot.Z + 0.5), 4f).OfType<Silverfish>());
    }

    [Fact]
    public void WaterFlowingIntoAFlowerDestroysItWithItsDrops()
    {
        // The scenario world is 32 blocks tall from Y 0, so the flower goes inside that range.
        var at = new Vector(4, 10, 4);
        var world = new VanillaFluids.ScenarioWorld(new FluidRules(FastLava: false, WaterSourceConversion: false));
        var flower = BlockStateProperties.GetState("minecraft:red_tulip");
        // The flower needs solid ground, or its own shape update destroys it (and reports the drop) before the water arrives.
        world.SetBlock(at + Vector.Down, BlockStateProperties.GetState("minecraft:dirt"));
        world.SetBlock(at, flower);
        world.Fluids.SetBlock(at + Vector.Up, BlocksRegistry.Get(Material.Water), Obsidian.WorldData.Fluids.BlockUpdateFlags.All);

        // Water spreads every 5 ticks and lava every 30, so this runs long enough for either to reach the flower.
        for (var tick = 0; tick < 40; tick++)
            world.Scheduler.Tick(_ => true, world.Fluids.RunScheduledTick);

        Assert.Equal(("minecraft:red_tulip", at), Assert.Single(world.Destroyed) is var (position, block) ? (block.UnlocalizedName, position) : default);
        Assert.True(world.GetBlock(at).IsLiquidBlock());
    }

    [Fact]
    public void LavaFlowingIntoAFlowerDestroysItWithoutDrops()
    {
        // The scenario world is 32 blocks tall from Y 0, so the flower goes inside that range.
        var at = new Vector(4, 10, 4);
        var world = new VanillaFluids.ScenarioWorld(new FluidRules(FastLava: false, WaterSourceConversion: false));
        world.SetBlock(at + Vector.Down, BlockStateProperties.GetState("minecraft:dirt"));
        world.SetBlock(at, BlockStateProperties.GetState("minecraft:red_tulip"));
        world.Fluids.SetBlock(at + Vector.Up, BlocksRegistry.Get(Material.Lava), Obsidian.WorldData.Fluids.BlockUpdateFlags.All);

        // Water spreads every 5 ticks and lava every 30, so this runs long enough for either to reach the flower.
        for (var tick = 0; tick < 40; tick++)
            world.Scheduler.Tick(_ => true, world.Fluids.RunScheduledTick);

        Assert.True(world.GetBlock(at).IsLiquidBlock());
        Assert.Empty(world.Destroyed);
    }

    [Fact]
    public async Task FluidDestroyedBlockDropsItsLootWithoutATool()
    {
        await using var context = TestServerFactory.CreateServer("fluid-destroyed");
        var world = (AbstractLevel)context.World;

        var drops = await BlockDrops.ForDestroyedAsync(world, BlockStateProperties.GetState("minecraft:red_tulip"), Spot, null);

        Assert.Equal(ItemsRegistry.RedTulip.Id, Assert.Single(drops).Holder.Id);
    }

    [Fact]
    public async Task TorchLosingItsFloorDropsItself()
    {
        await using var context = TestServerFactory.CreateServer("torch-support");
        var world = (AbstractLevel)context.World;
        await world.SetBlockAsync(Spot + Vector.Down, BlockStateProperties.GetState("minecraft:stone"), true);
        await world.SetBlockAsync(Spot, BlockStateProperties.GetState("minecraft:torch"), true);

        // Removing the floor is a live block change: the torch's shape update finds no support and destroys it.
        await world.SetBlockAsync(Spot + Vector.Down, BlocksRegistry.Air, true);

        Assert.True((await world.GetBlockAsync(Spot))!.IsAir);
        Assert.Equal(ItemsRegistry.Torch.Id, Assert.Single(SpawnedItems(context)).Holder.Id);
    }

    [Fact]
    public async Task TorchKeepsItsStateWhenASideNeighborChanges()
    {
        await using var context = TestServerFactory.CreateServer("torch-side");
        var world = (AbstractLevel)context.World;
        await world.SetBlockAsync(Spot + Vector.Down, BlockStateProperties.GetState("minecraft:stone"), true);
        await world.SetBlockAsync(Spot, BlockStateProperties.GetState("minecraft:torch"), true);

        await world.SetBlockAsync(Spot + Vector.East, BlockStateProperties.GetState("minecraft:stone"), true);

        Assert.Equal("minecraft:torch", (await world.GetBlockAsync(Spot))!.UnlocalizedName);
        Assert.Empty(SpawnedItems(context));
    }

    [Fact]
    public async Task DestroyBlockAsyncDropsTheLootAndLeavesAir()
    {
        await using var context = TestServerFactory.CreateServer("destroy-drop");
        var world = (AbstractLevel)context.World;
        await world.SetBlockAsync(Spot, BlockStateProperties.GetState("minecraft:red_tulip"), true);

        var destroyed = await world.DestroyBlockAsync(Spot, cause: null, drop: true);

        Assert.True(destroyed);
        Assert.True((await world.GetBlockAsync(Spot))!.IsAir);
        Assert.Equal(ItemsRegistry.RedTulip.Id, Assert.Single(SpawnedItems(context)).Holder.Id);
    }

    [Fact]
    public async Task DestroyBlockAsyncWithoutDropRemovesTheBlockSilently()
    {
        await using var context = TestServerFactory.CreateServer("destroy-silent");
        var world = (AbstractLevel)context.World;
        await world.SetBlockAsync(Spot, BlockStateProperties.GetState("minecraft:red_tulip"), true);

        Assert.True(await world.DestroyBlockAsync(Spot, cause: null, drop: false));

        Assert.True((await world.GetBlockAsync(Spot))!.IsAir);
        Assert.Empty(SpawnedItems(context));
    }

    [Fact]
    public async Task DestroyBlockAsyncOfAirDoesNothing()
    {
        await using var context = TestServerFactory.CreateServer("destroy-air");
        var world = (AbstractLevel)context.World;

        Assert.False(await world.DestroyBlockAsync(Spot, cause: null, drop: true));
    }

    [Fact]
    public async Task FluidDestroyedBlocksSpawnTheirDropsAfterTheFluidTick()
    {
        await using var context = TestServerFactory.CreateServer("fluid-queue");
        var world = (AbstractLevel)context.World;
        var tulip = BlockStateProperties.GetState("minecraft:red_tulip");
        var nextEntityId = 100_000;

        // The fluid tick queues the destroyed block; the level's tick then spawns its drops.
        world.Fluids.OnBlockDestroyed(Spot, tulip);
        await BlockDrops.SpawnDestroyedByFluidsAsync(world, () => ++nextEntityId);

        Assert.Equal(ItemsRegistry.RedTulip.Id, Assert.Single(SpawnedItems(context)).Holder.Id);
    }

    [Fact]
    public async Task RemovedChestSpillsItsContents()
    {
        await using var context = TestServerFactory.CreateServer("chest-spill");
        var world = (AbstractLevel)context.World;
        await world.SetBlockAsync(Spot, BlockStateProperties.GetState("minecraft:chest"), true);
        await world.SetBlockEntity(Spot, SavedDiamonds("minecraft:chest"));

        await world.SetBlockAsync(Spot, BlocksRegistry.Air, true);

        var spilled = SpawnedItems(context);
        Assert.Equal(["minecraft:diamond*2"], spilled.Select(stack => $"{stack.Holder.UnlocalizedName}*{stack.Count}"));
    }

    [Fact]
    public async Task RemovedShulkerBoxSpillsNothingBecauseItsContentsGoWithTheDrop()
    {
        await using var context = TestServerFactory.CreateServer("shulker-no-spill");
        var world = (AbstractLevel)context.World;
        await world.SetBlockAsync(Spot, BlockStateProperties.GetState("minecraft:shulker_box"), true);
        await world.SetBlockEntity(Spot, SavedDiamonds("minecraft:shulker_box"));

        await world.SetBlockAsync(Spot, BlocksRegistry.Air, true);

        Assert.Empty(SpawnedItems(context));
    }

    [Fact]
    public async Task UnmodelledBlockEntityBreaksWithNoDropsInsteadOfThrowing()
    {
        await using var context = TestServerFactory.CreateServer("unmodelled-block-entity");
        var player = TestServerFactory.CreateConnectedPlayer(context.Server, context.World, "Unmodelled");
        // Dirt needs no tool, so the loot is built (stone would return early for bare hands).
        var block = BlocksRegistry.Get("minecraft:dirt");
        await context.World.SetBlockAsync(Spot, block, true);
        await context.World.SetBlockEntity(Spot, new UnmodelledBlockEntity { BlockPosition = Spot });

        // The block entity has no component mapping, so the loot can't be built; the break still removes the block.
        await new MainEventHandler().OnBlockBreak(BlockBreakIntegration.CreateArgs(context, player, block, Spot));

        Assert.True((await context.World.GetBlockAsync(Spot))!.IsAir);
        Assert.Empty(SpawnedItems(context));
    }

    private sealed class UnmodelledBlockEntity : IBlockEntity
    {
        public string Id => "minecraft:unmodelled";

        public Vector BlockPosition { get; init; }

        public void ToNbt() { }

        public void FromNbt() { }

        public IBlockEntity Clone() => new UnmodelledBlockEntity { BlockPosition = BlockPosition };
    }

    private static List<ItemStack> SpawnedItems(RealServerContext context) =>
        context.World.GetEntitiesInRange(new VectorD(Spot.X + 0.5, Spot.Y + 0.5, Spot.Z + 0.5), 4f)
            .OfType<ItemEntity>()
            .Select(entity => entity.Item)
            .ToList();

    [Fact]
    public async Task ExplosionDropsComeFromTheLootTableNotTheBlockMaterial()
    {
        await using var context = TestServerFactory.CreateServer("explosion-drops");
        var level = (AbstractLevel)context.World;
        var at = new VectorD(4, 64, 4);

        // Stone drops cobblestone; with decay at radius 1 every survival check passes.
        var stone = BlocksRegistry.Get("minecraft:stone");
        var stoneDrops = await BlockDrops.ForExplosionAsync(level, stone, at, radius: 1, decays: true);
        Assert.Equal(["minecraft:cobblestone*1"], stoneDrops.Select(stack => $"{stack.Holder.UnlocalizedName}*{stack.Count}"));

        // Bedrock has no loot table; the old material fallback would have dropped it.
        var bedrock = BlocksRegistry.Get("minecraft:bedrock");
        Assert.Empty(await BlockDrops.ForExplosionAsync(level, bedrock, at, radius: 1, decays: true));
    }

    [Fact]
    public async Task NonDecayingExplosionKeepsEveryDropAndSkipsSurvivalChecks()
    {
        await using var context = TestServerFactory.CreateServer("explosion-nodecay");
        var level = (AbstractLevel)context.World;

        // Without decay the radius isn't passed, so a radius of 4 keeps the stone's drop every time.
        var stone = BlocksRegistry.Get("minecraft:stone");
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var drops = await BlockDrops.ForExplosionAsync(level, stone, new VectorD(4, 64, 4), radius: 4, decays: false);
            Assert.Single(drops);
        }
    }

    private static async Task<string[]> BreakAsync(RealServerContext context, Obsidian.Entities.Player player, string blockName, DataBlockEntity blockEntity = null)
    {
        var block = BlocksRegistry.Get(blockName);
        await context.World.SetBlockAsync(Spot, block, true);
        if (blockEntity is not null)
            await context.World.SetBlockEntity(Spot, blockEntity);
        await new MainEventHandler().OnBlockBreak(BlockBreakIntegration.CreateArgs(context, player, block, Spot));
        return context.World.GetEntitiesInRange(new VectorD(Spot.X + 0.5, Spot.Y + 0.5, Spot.Z + 0.5), 4f)
            .OfType<ItemEntity>()
            .Select(entity => $"{entity.Item.Holder.UnlocalizedName}*{entity.Item.Count}")
            .ToArray();
    }

    private static Task<ItemStack> BreakForDrop(RealServerContext context, Obsidian.Entities.Player player, string blockName, DataBlockEntity blockEntity) =>
        BreakForDrop(context, player, BlocksRegistry.Get(blockName), blockEntity);

    private static async Task<ItemStack> BreakForDrop(RealServerContext context, Obsidian.Entities.Player player, IBlock block, DataBlockEntity blockEntity)
    {
        await context.World.SetBlockAsync(Spot, block, true);
        await context.World.SetBlockEntity(Spot, blockEntity);
        await new MainEventHandler().OnBlockBreak(BlockBreakIntegration.CreateArgs(context, player, block, Spot));
        var drop = context.World.GetEntitiesInRange(new VectorD(Spot.X + 0.5, Spot.Y + 0.5, Spot.Z + 0.5), 4f).OfType<ItemEntity>().Single();
        return drop.Item;
    }
}
