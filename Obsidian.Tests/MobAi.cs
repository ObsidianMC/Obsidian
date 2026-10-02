using Obsidian.API;
using Obsidian.Entities;
using Obsidian.Entities.AI;
using Obsidian.Registries;
using Obsidian.Utilities;
using System.Threading.Tasks;
using Xunit;

namespace Obsidian.Tests;

public sealed class MobAi
{
    [Fact]
    public async Task InterruptedEmptyRegionHeadersCanBeCompleted()
    {
        var path = System.IO.Path.GetTempFileName();
        try
        {
            await System.IO.File.WriteAllBytesAsync(path, new byte[4096]);
            await using var region = new Obsidian.WorldData.RegionFile(path, Obsidian.Nbt.NbtCompression.ZLib);
            Assert.True(await region.InitializeAsync());
            Assert.Equal(8192L, region.EndOfFile);
            Assert.Null(await region.GetChunkBytesAsync(0, 0));
        }
        finally
        {
            System.IO.File.Delete(path);
        }
    }

    [Theory]
    [InlineData("00000000-0000-0000-0000-000000000001")]
    [InlineData("ffffffff-ffff-ffff-ffff-ffffffffffff")]
    public void SpawnUuidsAlwaysOccupySixteenBytes(string text)
    {
        var uuid = System.Guid.Parse(text);
        var buffer = new Obsidian.Net.NetworkBuffer();
        buffer.WriteUuid(uuid);
        Assert.Equal(16, buffer.Size);
        Assert.Equal(uuid.ToByteArray(true), buffer.AsSpan(0, buffer.Size).ToArray());
    }

    [Fact]
    public void SavedEquipmentComponentsUseTheirProtocolIds()
    {
        Assert.IsType<Obsidian.API.Inventory.DataComponents.CanBreakDataComponent>(
            Obsidian.API.Inventory.DataComponents.ComponentBuilder.ComponentsMap[DataComponentType.CanBreak]());
        var item = Obsidian.API.Registries.ItemsRegistry.GetSingleItem(Material.Bow);
        var buffer = new Obsidian.Net.NetworkBuffer();
        buffer.WriteItemStack(item);
        var reader = new Obsidian.Net.NetworkBuffer(buffer.AsSpan(0, buffer.Size).ToArray());
        var restored = reader.ReadItemStack();
        Assert.NotNull(restored);
        Assert.Equal(item.Type, restored.Type);
        Assert.Equal(item.Count, restored.Count);
        Assert.Equal(item.TotalComponents, restored.TotalComponents);
        Assert.Equal(0, restored.GetComponent<Obsidian.API.Inventory.DataComponents.SimpleDataComponent<int>>(DataComponentType.RepairCost).Value);
        Assert.Equal(reader.Size, reader.Offset);
    }

    [Fact]
    public void WorldsWithoutSavedDifficultyAllowHostileMobs()
    {
        Assert.Equal(Difficulty.Normal, new LevelData().Difficulty);
    }

    [Fact]
    public void FractionalNegativePositionsBelongToThePreviousChunk()
    {
        Assert.Equal((-1, -2), new VectorF(-0.1f, 0, -16.1f).ToChunkCoord());
        Assert.Equal((0, 1), new VectorF(0.1f, 0, 16.1f).ToChunkCoord());
    }

    [Fact]
    public async Task HigherPriorityMovementInterruptsWanderingButPreservesLooking()
    {
        var selector = new GoalSelector();
        var wander = new ProbeGoal(GoalFlags.Move) { Enabled = true };
        var look = new ProbeGoal(GoalFlags.Look) { Enabled = true };
        var panic = new ProbeGoal(GoalFlags.Move);
        selector.AddGoal(6, wander);
        selector.AddGoal(7, look);
        selector.AddGoal(1, panic);

        await selector.TickAsync();
        panic.Enabled = true;
        await selector.TickAsync();

        Assert.Equal(1, wander.Starts);
        Assert.Equal(1, wander.Stops);
        Assert.Equal(1, panic.Starts);
        Assert.Equal(1, look.Starts);
        Assert.Equal(0, look.Stops);
        selector.Pause();
        await selector.TickAsync();
        Assert.False(selector.IsExecuting);
        Assert.Equal(1, panic.Stops);
        Assert.Equal(1, look.Stops);
    }

    [Fact]
    public void CollisionClipsAtWallAndFloorWithoutBlockingSliding()
    {
        var bounds = new BoundingBox(new VectorF(0.1f, 1, 0.1f), new VectorF(0.9f, 2, 0.9f));
        BoundingBox[] obstacles =
        [new(new VectorF(1, 0, 0), new VectorF(2, 3, 1)), new(new VectorF(0, 0, 0), new VectorF(1, 1, 1))];

        var displacement = EntityMovement.Clip(bounds, new VectorF(0.5f, -0.2f, 0.3f), obstacles);

        Assert.Equal(0.1f, displacement.X, 4);
        Assert.Equal(0, displacement.Y);
        Assert.Equal(0.3f, displacement.Z, 4);
    }

    [Fact]
    public void BabiesUseHalfSizedBoundingBoxesAndZombieSpeedBonus()
    {
        var pig = new Pig { Level = null! };
        var zombie = new Zombie { Level = null! };
        var adultPigWidth = pig.Dimension.Width;
        var adultZombieHeight = zombie.Dimension.Height;
        var adultZombieSpeed = zombie.MovementSpeed;
        pig.IsBaby = true;
        zombie.IsBaby = true;

        Assert.Equal(adultPigWidth / 2, pig.Dimension.Width);
        Assert.Equal(adultZombieHeight / 2, zombie.Dimension.Height);
        Assert.Equal(adultZombieSpeed * 1.5f, zombie.MovementSpeed);
        Assert.Equal(-24000, pig.Age);
    }

    [Fact]
    public void ImportedShapesUseDefaultStatesForAirAndFullBlocks()
    {
        Assert.Null(BlocksRegistry.Air.State);
        Assert.Null(BlocksRegistry.Stone.State);
        Assert.Empty(BlockCollisionShapes.Get(BlocksRegistry.Air));
        var stone = Assert.Single(BlockCollisionShapes.Get(BlocksRegistry.Stone));
        Assert.Equal(VectorF.Zero, stone.Min);
        Assert.Equal(new VectorF(1), stone.Max);
    }

    [Theory]
    [InlineData(EntityType.Cow)]
    [InlineData(EntityType.Mooshroom)]
    [InlineData(EntityType.Chicken)]
    [InlineData(EntityType.Sheep)]
    [InlineData(EntityType.Husk)]
    [InlineData(EntityType.Skeleton)]
    [InlineData(EntityType.Stray)]
    [InlineData(EntityType.Bogged)]
    [InlineData(EntityType.Parched)]
    [InlineData(EntityType.Creeper)]
    [InlineData(EntityType.Slime)]
    public void MobSavesRoundTripIdentityPositionHealthAndNoAi(EntityType type)
    {
        var mob = Obsidian.Entities.Factories.EntitySpawner.CreateMob(null!, type)!;
        mob.Position = new VectorF(-17.5f, 64, 31.5f);
        mob.InitializeAi(false);
        mob.Health = 3;
        mob.PersistenceRequired = true;
        mob.MobBitMask = MobBitmask.NoAi;
        using var writer = new Obsidian.Nbt.RawNbtWriter("");
        writer.WriteListStart("Entities", Obsidian.Nbt.NbtTagType.Compound, 1);
        mob.WriteSave(writer);
        writer.EndList();
        writer.EndCompound();
        using var stream = new System.IO.MemoryStream(writer.Data.ToArray());
        var root = (Obsidian.Nbt.NbtCompound)new Obsidian.Nbt.NbtReader(stream).ReadNextTag()!;
        var saved = Assert.IsType<Obsidian.Nbt.NbtList>(root["Entities"]);
        var restored = Obsidian.Entities.Factories.EntitySpawner.CreateMob(null!, type)!;
        restored.ReadSave(Assert.IsType<Obsidian.Nbt.NbtCompound>(Assert.Single(saved)));
        Assert.Equal(mob.Uuid, restored.Uuid);
        Assert.Equal(mob.Position, restored.Position);
        Assert.Equal(3, restored.Health);
        Assert.True(restored.PersistenceRequired);
        Assert.Equal(MobBitmask.NoAi, restored.MobBitMask);
    }

    private sealed class ProbeGoal(GoalFlags flags) : Goal
    {
        public bool Enabled { get; set; }
        public int Starts { get; private set; }
        public int Stops { get; private set; }
        public override GoalFlags Flags => flags;
        public override bool CanUse() => Enabled;
        public override void Start() => Starts++;
        public override void Stop() => Stops++;
    }
}
