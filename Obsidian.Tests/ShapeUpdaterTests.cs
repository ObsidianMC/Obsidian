using Obsidian.API;
using Obsidian.API.Blocks;
using Obsidian.API.Registry.Codecs.Biomes;
using Obsidian.API.World;
using Obsidian.API.World.Features;
using Obsidian.API.World.Generator.RandomSources;
using Obsidian.Registries;
using Obsidian.WorldData.Features.Tree;
using System;
using Xunit;

namespace Obsidian.Tests;

public sealed class ShapeUpdaterTests
{
    [Fact]
    public void SharedPassSucceedsAfterLevelReadThrows()
    {
        var failure = new InvalidOperationException("Level read failed.");
        var failingLevel = new GenerationLevel(_ => throw failure);
        var level = new GenerationLevel(_ => BlocksRegistry.Air);
        var state = BlocksRegistry.OakStairs.WithProperty("shape", "outer_left");
        var expected = Update(level, state);

        var thrown = Assert.Throws<InvalidOperationException>(() => Update(failingLevel, state));
        var actual = Update(level, state);

        Assert.Same(failure, thrown);
        Assert.True(expected.IsSameState(actual));
        Assert.False(state.IsSameState(actual));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NestedSharedPassRestoresOuterLevel(bool nestedThrows)
    {
        var state = BlocksRegistry.OakStairs.WithProperty("shape", "outer_left");
        var level = new GenerationLevel(_ => BlocksRegistry.Air);
        var expected = Update(level, state);
        var failure = new InvalidOperationException("Nested level read failed.");
        var nestedLevel = new GenerationLevel(_ =>
        {
            if (nestedThrows)
                throw failure;

            return BlocksRegistry.Stone;
        });

        // The outer rule must keep reading its own level after a nested rule returns or throws.
        IBlock? nestedResult = null;
        var nested = false;
        var reentrantLevel = new GenerationLevel(position =>
        {
            if (!nested)
            {
                nested = true;
                if (nestedThrows)
                {
                    var thrown = Assert.Throws<InvalidOperationException>(() => Update(nestedLevel, state));
                    Assert.Same(failure, thrown);
                }
                else
                {
                    nestedResult = Update(nestedLevel, state);
                }
            }

            return level.GetBlock(position);
        });

        var actual = Update(reentrantLevel, state);

        Assert.True(nested);
        Assert.True(expected.IsSameState(actual));
        Assert.False(state.IsSameState(actual));
        if (!nestedThrows)
        {
            Assert.NotNull(nestedResult);
            Assert.True(expected.IsSameState(nestedResult));
        }
    }

    private static IBlock Update(IWorldGenLevel level, IBlock state) =>
        ShapeUpdater.UpdateShape(level, state, new Vector(4, 64, 4), BlockFace.West, BlocksRegistry.Air,
            scheduleFluidTick: false);

    private sealed class GenerationLevel(Func<Vector, IBlock> read) : IWorldGenLevel
    {
        public long Seed => 0;

        public int MinY => -64;

        public int Height => 384;

        public int SeaLevel => 63;

        public IRandomSource Random => throw new NotSupportedException();

        public IBlock GetBlock(Vector position) => read(position);

        public bool SetBlock(Vector position, IBlock block) => throw new NotSupportedException();

        public bool EnsureCanWrite(Vector position) => false;

        public int GetHeight(HeightmapType type, int x, int z) => throw new NotSupportedException();

        public BiomeCodec GetBiome(Vector position) => throw new NotSupportedException();

        public void SetBlockEntity(Vector position, IBlockEntity blockEntity) => throw new NotSupportedException();

        public IBlockEntity? GetBlockEntity(Vector position) => null;

        public void AddEntity(GeneratedEntity entity) => throw new NotSupportedException();

        public void MarkForPostProcessing(Vector position) => throw new NotSupportedException();

        public void ScheduleFluidTick(Vector position) => throw new NotSupportedException();
    }
}
