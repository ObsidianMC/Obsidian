using Obsidian.API;
using Obsidian.API.BlockEntities;
using Obsidian.API.Containers;
using Obsidian.ChunkData;
using Obsidian.Registries;
using Obsidian.Net;
using Obsidian.WorldData;
using Obsidian.WorldData.Portals;
using System;
using System.Linq;
using Xunit;

namespace Obsidian.Tests;

public sealed class TerrainWrites
{
    [Fact]
    public void ExclusiveWritesKeepTheOrdinaryWriteResults()
    {
        var ordinary = new Chunk(-3, 2);
        var generation = new Chunk(-3, 2);
        var portal = new Vector(-48, 70, 32);
        var kept = new ContainerBlockEntity(new Container(9)) { Id = "probe:crate", BlockPosition = portal };
        foreach (var chunk in new[] { ordinary, generation })
        {
            chunk.SetBlock(0, 70, 0, BlocksRegistry.NetherPortal);
            chunk.PortalOwners[portal] = new PortalOwner("test:portal", "minecraft:nether_portal");
            chunk.SetBlockEntity(0, 70, 0, kept);
            chunk.TakeUnsaved();
        }

        // Includes replacing a claimed portal and putting it back, removing a column's top, leaves, fluids,
        // every build-height boundary and random-ticking blocks. The ordinary writer supplies the reference.
        (int X, int Y, int Z, IBlock Block)[] writes =
        [
            (0, 70, 0, BlocksRegistry.Stone), (0, 70, 0, BlocksRegistry.NetherPortal),
            (15, -64, 15, BlocksRegistry.Stone), (15, 319, 15, BlocksRegistry.OakLeaves),
            (15, 318, 15, BlocksRegistry.Water), (15, 319, 15, BlocksRegistry.Air),
            (15, 318, 15, BlocksRegistry.Air), (1, 80, 1, BlocksRegistry.OakLeaves),
            (2, 85, 2, BlocksRegistry.Lava), (0, -65, 0, BlocksRegistry.Stone),
            (0, 320, 0, BlocksRegistry.Stone)
        ];

        using (generation.BeginTerrainWrites())
        {
            foreach (var (x, y, z, block) in writes)
            {
                ordinary.SetBlock(x, y, z, block);
                generation.SetBlock(x, y, z, block);
                foreach (var type in new[] { HeightmapType.WorldSurfaceWG, HeightmapType.OceanFloorWG })
                    Assert.Equal(ordinary.Heightmaps[type].GetDataArray(), generation.Heightmaps[type].GetDataArray());
            }
        }

        foreach (var (type, map) in ordinary.Heightmaps)
            Assert.Equal(map.GetDataArray(), generation.Heightmaps[type].GetDataArray());

        for (var index = 0; index < ordinary.Sections.Length; index++)
        {
            var expected = (ChunkSection)ordinary.Sections[index];
            var actual = (ChunkSection)generation.Sections[index];
            Assert.Equal(expected.IsEmpty, actual.IsEmpty);
            Assert.Equal(expected.IsRandomlyTicking, actual.IsRandomlyTicking);
            Assert.Equal(expected.HasSkyLight, actual.HasSkyLight);
            Assert.Equal(expected.HasBlockLight, actual.HasBlockLight);
            Assert.Equal(expected.BlockStateContainer.DataArray?.storage, actual.BlockStateContainer.DataArray?.storage);
            using var expectedData = new NetworkBuffer();
            using var actualData = new NetworkBuffer();
            expected.BlockStateContainer.WriteTo(expectedData);
            actual.BlockStateContainer.WriteTo(actualData);
            Assert.Equal(expectedData.AsSpan(0, expectedData.Size).ToArray(),
                actualData.AsSpan(0, actualData.Size).ToArray());
        }

        Assert.Empty(generation.PortalOwners);
        Assert.Same(kept, Assert.Single(generation.BlockEntities).Value);
        Assert.Equal(ordinary.BlockEntities.ToArray(), generation.BlockEntities.ToArray());
        Assert.True(generation.IsUnsaved);
        Assert.True(ordinary.TakeUnsaved());
        Assert.True(generation.TakeUnsaved());
        Assert.False(ordinary.TakeUnsaved());
        Assert.False(generation.TakeUnsaved());
        Assert.Equal(ordinary.IsUnsaved, generation.IsUnsaved);
        generation.SetBlock(1, 100, 1, BlocksRegistry.Stone);
        Assert.Equal(101, generation.Heightmaps[HeightmapType.WorldSurface].GetHeight(1, 1));
    }

    [Fact]
    public void TerrainScopeRejectsPublishedChunksAndReleasesAfterExceptions()
    {
        var chunk = new Chunk(0, 0);
        Assert.Throws<InvalidOperationException>(() =>
        {
            using var writes = chunk.BeginTerrainWrites();
            chunk.SetBlock(0, 90, 0, BlocksRegistry.Stone);
            using var nested = chunk.BeginTerrainWrites();
        });

        Assert.Equal(91, chunk.Heightmaps[HeightmapType.WorldSurface].GetHeight(0, 0));
        using (chunk.BeginTerrainWrites())
            chunk.SetBlock(0, 90, 0, BlocksRegistry.Air);

        chunk.SetChunkStatus(ChunkGenStage.features);
        Assert.Throws<InvalidOperationException>(() => chunk.BeginTerrainWrites());
        chunk.SetBlock(0, 95, 0, BlocksRegistry.Stone);
        Assert.Equal(96, chunk.Heightmaps[HeightmapType.WorldSurface].GetHeight(0, 0));
    }
}
