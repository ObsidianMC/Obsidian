using Obsidian.API;
using Obsidian.API.Inventory;
using Obsidian.API.Inventory.DataComponents;
using Obsidian.API.Registries;
using Obsidian.API.World.Features;
using Obsidian.Entities;
using Obsidian.Nbt;
using Obsidian.Registries;
using Obsidian.WorldData;
using Obsidian.WorldData.Generators.Mojang;
using Obsidian.WorldData.Structures;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Obsidian.Tests;

/// <summary>
/// What a complete chunk keeps across a restart: its entities, its opened containers, and the state of the structure
/// starting in it. The fixture saves the chunk through a region, then reads it back with a fresh region and structure
/// manager, like a server starting again.
/// </summary>
public sealed class WorldPersistence(WorldPersistence.RestartedChunk restarted) : IClassFixture<WorldPersistence.RestartedChunk>
{
    [Fact]
    public void EntitiesAndContainersSurviveRestart()
    {
        var chunk = restarted.Chunk;

        // The complete chunk's entities come back from the entity region file, waiting for the level to spawn them.
        Assert.Same(chunk, Assert.Single(restarted.ChunksWithEntities));
        var entities = chunk.PendingEntities.Select(pending => EntityNbt.Load(EntityNbt.ToNbt(pending), null!)!).ToList();
        Assert.Equal(5, entities.Count);

        var witch = Assert.IsType<Living>(Assert.Single(entities, entity => entity.Type == EntityType.Witch));
        Assert.Equal(RestartedChunk.WitchUuid, witch.Uuid);
        Assert.Equal(RestartedChunk.WitchPosition, witch.Position);
        Assert.Equal(17f, witch.Health);
        Assert.True(witch.PersistenceRequired);
        Assert.Equal("Agatha", witch.CustomName!.Text);
        // A field Obsidian doesn't model survives as it was.
        Assert.True(witch.UnmodeledData.TryGetTag<NbtTag<int>>("PortalCooldown", out var cooldown) && cooldown.Value == 40);

        var frame = Assert.IsType<ItemFrame>(Assert.Single(entities, entity => entity.Type == EntityType.ItemFrame));
        Assert.Equal(BlockFace.West, frame.Facing);
        Assert.Equal(3, frame.Rotation);
        Assert.Equal(Material.Elytra, frame.Item!.Type);

        var minecart = Assert.IsType<ChestMinecart>(Assert.Single(entities, entity => entity.Type == EntityType.ChestMinecart));
        Assert.Equal("minecraft:chests/abandoned_mineshaft", minecart.LootTable);
        Assert.Equal(-77L, minecart.LootTableSeed);

        var item = Assert.IsType<ItemEntity>(Assert.Single(entities, entity => entity.Type == EntityType.Item));
        Assert.Equal(Material.Apple, item.Item.Type);
        Assert.Equal(7, item.Item.Count);

        // Generated before the chunk was saved, but not spawned yet.
        Assert.Single(entities, entity => entity.Type == EntityType.EndCrystal);

        var chest = Assert.IsType<Container>(chunk.GetBlockEntity(RestartedChunk.Chest.X, RestartedChunk.Chest.Y, RestartedChunk.Chest.Z));
        Assert.Equal("Loot", chest.CustomName!.Text);
        Assert.Equal("Loot", chest.Title.Text);

        var sword = chest[0]!;
        Assert.Equal(Material.DiamondSword, sword.Type);
        Assert.Equal(12, sword.Damage);
        Assert.Equal("Edge", sword.CustomName!.Text);

        var book = chest[4]!;
        var enchantment = Assert.Single(book.GetComponent<SimpleDataComponent<Enchantment[]>>(DataComponentType.StoredEnchantments)!.Value!);
        Assert.Equal(new Enchantment { Id = EnchantmentsRegistry.Sharpness.Id, Level = 3 }, enchantment);

        Assert.Equal(Potion.Swiftness, chest[13]!.GetComponent<PotionContentsDataComponent>(DataComponentType.PotionContents)!.Potion);
        Assert.Equal(23, chest[26]!.Count);
        Assert.Equal(4, chest.Count(slot => slot is not null));

        // A container that wasn't opened stays waiting for its loot.
        var unopened = Assert.IsType<DataBlockEntity>(chunk.GetBlockEntity(RestartedChunk.UnopenedChest.X, RestartedChunk.UnopenedChest.Y,
            RestartedChunk.UnopenedChest.Z));
        Assert.Equal("minecraft:chests/simple_dungeon", unopened.Data.GetString("LootTable"));
        Assert.Equal(99L, unopened.Data.GetLong("LootTableSeed"));
    }

    [Fact]
    public void StructureStateSurvivesRestart()
    {
        var hut = restarted.Hut;

        // The hut settled on the ground and spawned its witch before the restart, so it's placed at the same height and the
        // witch isn't spawned again.
        Assert.Equal(restarted.PlacedHutBox, hut.Pieces[0].BoundingBox);

        Assert.True(hut.SaveState().TryGetTag<NbtList>("Children", out var children));
        var state = Assert.IsType<NbtCompound>(Assert.Single(children));
        Assert.Equal(RestartedChunk.PlacedHeight, state.GetInt("HPos"));
        Assert.True(state.TryGetTag<NbtTag<bool>>("Witch", out var witch) && witch.Value);
        Assert.True(state.TryGetTag<NbtTag<bool>>("Cat", out var cat) && !cat.Value);
    }

    /// <summary>
    /// The chunk where a swamp hut starts (seed 12345), saved with entities, containers and the hut's state, then loaded by
    /// a fresh region and structure manager.
    /// </summary>
    public sealed class RestartedChunk : IAsyncLifetime
    {
        public const int PlacedHeight = 63;

        private const long Seed = 12345L;
        private const int ChunkX = -149;
        private const int ChunkZ = -87;
        private const string SwampHut = "minecraft:swamp_hut";

        public static readonly Vector Chest = new((ChunkX << 4) + 3, 70, (ChunkZ << 4) + 5);
        public static readonly Vector UnopenedChest = new((ChunkX << 4) + 9, 40, (ChunkZ << 4) + 12);
        public static readonly VectorF WitchPosition = new((ChunkX << 4) + 4.5f, 71f, (ChunkZ << 4) + 6.25f);
        public static readonly Guid WitchUuid = Guid.Parse("5a8e0ea6-11a4-4f4a-9a2c-2b6bd2a1c0de");

        private readonly string folder = Path.Join(Path.GetTempPath(), $"obsidian-persistence-{Guid.NewGuid():N}");

        public Chunk Chunk { get; private set; } = default!;

        public List<Chunk> ChunksWithEntities { get; } = [];

        /// <summary>
        /// The hut as a fresh structure manager has it, restored from the saved chunk.
        /// </summary>
        public StructureStart Hut { get; private set; } = default!;

        public BlockBox PlacedHutBox { get; private set; }

        public async Task InitializeAsync()
        {
            var (regionX, regionZ) = (ChunkX >> Region.CubicRegionSizeShift, ChunkZ >> Region.CubicRegionSizeShift);

            var structures = new ChunkBuilder(Seed).Structures!;
            var hut = structures.GetStarts(ChunkX, ChunkZ).Single(start => start.Structure.Identifier == SwampHut);
            this.PlacedHutBox = hut.Pieces[0].BoundingBox.Move(0, PlacedHeight - hut.Pieces[0].BoundingBox.MinY, 0);
            hut.RestoreState(PlacedHutState(hut));

            await using (var region = new Region(regionX, regionZ, this.folder) { SaveStructureStarts = structures.SaveStarts })
            {
                await region.InitAsync();
                region.SetChunk(CreateChunk(region));
                await region.FlushAsync();
            }

            await using (var region = new Region(regionX, regionZ, this.folder) { EntitiesLoaded = this.ChunksWithEntities.Add })
            {
                await region.InitAsync();
                this.Chunk = (Chunk)await region.GetChunkAsync(ChunkX - (regionX << Region.CubicRegionSizeShift), ChunkZ - (regionZ << Region.CubicRegionSizeShift));
            }

            this.Hut = new ChunkBuilder(Seed).Structures!.GetStarts(ChunkX, ChunkZ).Single(start => start.Structure.Identifier == SwampHut);
            this.Hut.RestoreState(this.Chunk.StructureStarts!.TryGetTag<NbtCompound>(SwampHut, out var saved) ? saved : null);
        }

        public Task DisposeAsync()
        {
            Directory.Delete(this.folder, recursive: true);
            return Task.CompletedTask;
        }

        // What placing the hut in its first chunk leaves: settled at the ground height, with its witch spawned.
        private NbtCompound PlacedHutState(StructureStart hut)
        {
            var box = this.PlacedHutBox;
            return new NbtCompound(hut.Structure.Identifier)
            {
                new NbtList(NbtTagType.Compound, "Children")
                {
                    new NbtCompound
                    {
                        new NbtArray<int>("BB", [box.MinX, box.MinY, box.MinZ, box.MaxX, box.MaxY, box.MaxZ]),
                        new NbtTag<int>("HPos", PlacedHeight),
                        new NbtTag<bool>("Witch", true),
                        new NbtTag<bool>("Cat", false)
                    }
                }
            };
        }

        private static Chunk CreateChunk(Region region)
        {
            var chunk = new Chunk(ChunkX, ChunkZ);
            chunk.SetChunkStatus(ChunkGenStage.full);

            // A chest a player opened (its loot generated) and filled.
            chunk.SetBlock(Chest.X, Chest.Y, Chest.Z, BlocksRegistry.Get(Material.Chest));
            var chest = new Container { Id = "chest", Title = "Loot", CustomName = "Loot", BlockPosition = Chest };
            chest[0] = new ItemStack(ItemsRegistry.Get(Material.DiamondSword), 1, ComponentBuilder.Damage with { Value = 12 },
                ComponentBuilder.CustomName with { Value = "Edge" });
            chest[4] = new ItemStack(ItemsRegistry.Get(Material.EnchantedBook), 1,
                ComponentBuilder.StoredEnchantments with { Value = [new Enchantment { Id = EnchantmentsRegistry.Sharpness.Id, Level = 3 }] });
            chest[13] = new ItemStack(ItemsRegistry.Get(Material.Potion), 1, new PotionContentsDataComponent { Potion = Potion.Swiftness });
            chest[26] = new ItemStack(ItemsRegistry.Get(Material.Apple), 23);
            chunk.SetBlockEntity(Chest.X, Chest.Y, Chest.Z, chest);

            chunk.SetBlock(UnopenedChest.X, UnopenedChest.Y, UnopenedChest.Z, BlocksRegistry.Get(Material.Chest));
            var unopened = new DataBlockEntity { Id = "minecraft:chest", BlockPosition = UnopenedChest };
            unopened.Set("LootTable", "minecraft:chests/simple_dungeon");
            unopened.Set("LootTableSeed", 99L);
            chunk.SetBlockEntity(UnopenedChest.X, UnopenedChest.Y, UnopenedChest.Z, unopened);

            // Entities of the level standing in the chunk.
            var witch = new Living
            {
                Level = null!,
                Type = EntityType.Witch,
                EntityId = 1,
                Uuid = WitchUuid,
                Position = WitchPosition,
                Health = 17f,
                PersistenceRequired = true,
                CustomName = "Agatha",
                UnmodeledData = new NbtCompound { new NbtTag<int>("PortalCooldown", 40) }
            };
            var frame = new ItemFrame
            {
                Level = null!,
                Type = EntityType.ItemFrame,
                EntityId = 2,
                Position = new VectorF((ChunkX << 4) + 0.96875f, 72.5f, (ChunkZ << 4) + 8.5f),
                Facing = BlockFace.West,
                Rotation = 3,
                Item = new ItemStack(ItemsRegistry.Get(Material.Elytra))
            };
            var minecart = new ChestMinecart
            {
                Level = null!,
                EntityId = 3,
                Position = new VectorF((ChunkX << 4) + 8.5f, 30.5f, (ChunkZ << 4) + 1.5f),
                LootTable = "minecraft:chests/abandoned_mineshaft",
                LootTableSeed = -77L
            };
            var item = new ItemEntity
            {
                Level = null!,
                EntityId = 4,
                Position = new VectorF((ChunkX << 4) + 12.25f, 65f, (ChunkZ << 4) + 15.75f),
                Item = new ItemStack(ItemsRegistry.Get(Material.Apple), 7)
            };
            foreach (var entity in new Entity[] { witch, frame, minecart, item })
                region.Entities.TryAdd(entity.EntityId, entity);

            // One placed by world generation that hasn't spawned yet.
            chunk.PendingEntities.Add(new GeneratedEntity("minecraft:end_crystal", new VectorF((ChunkX << 4) + 2.5f, 80f, (ChunkZ << 4) + 2.5f)));

            return chunk;
        }
    }
}
