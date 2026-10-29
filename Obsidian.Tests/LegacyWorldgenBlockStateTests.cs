using Obsidian.Registries;
using System;
using System.Collections.Generic;
using Xunit;

namespace Obsidian.Tests;

public class LegacyWorldgenBlockStateTests
{
    [Fact]
    public void MapsUnchangedAndShiftedIdsWithExactProperties()
    {
        Assert.Equal((ushort)1, LegacyWorldgenBlockStates.StateId(BlocksRegistry.Stone));
        Assert.Equal((ushort)27, LegacyWorldgenBlockStates.StateId(BlocksRegistry.BambooPlanks));
        Assert.NotEqual(27, BlocksRegistry.BambooPlanks.GetHashCode());

        var pillar = BlockStateProperties.GetState("minecraft:purpur_pillar", new Dictionary<string, string> { ["axis"] = "x" });
        Assert.Equal((ushort)14511, LegacyWorldgenBlockStates.StateId(pillar));
        Assert.Equal((ushort)14513, LegacyWorldgenBlockStates.StateId(pillar.WithProperty("axis", "z")));
    }

    [Fact]
    public void RejectsStatesWithoutLegacyEquivalent()
    {
        var block = BlockStateProperties.GetState("minecraft:poplar_planks");
        var error = Assert.Throws<InvalidOperationException>(() => LegacyWorldgenBlockStates.StateId(block));
        Assert.Contains("No 1.21.11 block state equivalent for minecraft:poplar_planks", error.Message);
    }
}
