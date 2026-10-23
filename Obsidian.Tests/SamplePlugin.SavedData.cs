using Obsidian.API;
using Obsidian.API.Data;
using Obsidian.Entities;
using Obsidian.Nbt;
using Obsidian.Plugins;
using Obsidian.Tests.Fakes;
using Obsidian.WorldData;
using Obsidian.WorldData.Generators;
using Obsidian.API.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Dimension = Obsidian.WorldData.Dimension;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace Obsidian.Tests;

public sealed partial class SamplePlugin
{
    [Fact]
    public async Task SavedDataAndAttachmentsSurviveSaveAndPluginReload()
    {
        await using var context = TestServerFactory.CreateServer($"saved-{Guid.NewGuid():N}");
        var world = context.World;
        var chunk = WorldBlockEntities.AddEmptyChunk(world);
        var player = TestServerFactory.CreateConnectedPlayer(context.Server, world, "Saver");
        await player.LoadAsync(loadFromPersistentWorld: false);
        Assert.True(world.TryAddPlayer(player));
        var entity = new Entity { Level = world, Type = EntityType.Pig };
        Assert.True(world.TryAddEntity(entity));
        await using var manager = BlockUpdates.CreatePluginManager(context);
        var path = await this.PackSampleAsync();
        var plugin = (await manager.LoadPluginAsync(path))!;
        var (networks, membership) = SavedTypes(plugin);

        var level = world.GetSavedData(networks, () => new List<int> { 1 });
        var global = ((IServer)context.Server).GetSavedData(networks, () => new List<int> { 2 });
        await using var dimension = new Dimension(
            NullLogger<Dimension>.Instance,
            world.PacketBroadcaster,
            new TestOptionsMonitor<ServerConfiguration>(context.Server.Configuration),
            context.EventDispatcher,
            new EmptyWorldGenerator(),
            "sample:saved_dimension",
            world);
        dimension.Initialize(world.DimensionType);
        world.RegisterDimension(dimension);
        Assert.Same(global, dimension.GetSavedData(networks, () => [], SavedDataScope.World));
        var dimensionData = dimension.GetSavedData(networks, () => new List<int> { 10 });
        Assert.NotSame(level, dimensionData);
        await dimension.SaveAsync();
        Assert.Equal([10], ReadValues(Path.Combine(dimension.FolderPath, "data", "sample", "networks.dat")));
        Assert.NotSame(level, global);
        Assert.Same(level, world.GetSavedData(networks, () => throw new Exception("Should use cached data")));
        level.Value.Add(3);
        level.MarkDirty();
        global.Value.Add(4);
        global.MarkDirty();
        player.SetAttachment(membership, [5]);
        entity.SetAttachment(membership, [6]);
        chunk.SetAttachment(membership, [7]);
        player.GetAttachment(membership)!.Add(99);
        Assert.Equal([5], player.GetAttachment(membership));

        await world.SaveAsync();
        await player.SaveAsync();
        await world.FlushRegionsAsync();
        var levelPath = Path.Combine(world.FolderPath, "data", "sample", "networks.dat");
        var globalPath = Path.Combine(world.FolderPath, "data", "obsidian", "world", "sample", "networks.dat");
        Assert.Equal([2, 4], ReadValues(globalPath));
        using (var data = File.OpenRead(levelPath))
        {
            var root = Assert.IsType<NbtCompound>(new NbtReader(data, NbtCompression.GZip).ReadNextTag());
            Assert.Equal(VanillaLevelData.DataVersion, root.GetInt("DataVersion"));
            Assert.Equal([1, 3], Assert.IsType<NbtArray<int>>(root["data"]).GetArray());
        }

        var entityNbt = EntityNbt.Save(entity)!;
        level.Value.Add(9);
        level.MarkDirty();
        await manager.UnloadPluginAsync(plugin);
        Assert.Equal([1, 3, 9], ReadValues(levelPath));
        var before = File.ReadAllBytes(levelPath);
        Assert.Throws<InvalidOperationException>(() => level.MarkDirty());
        Assert.Throws<InvalidOperationException>(() => player.GetAttachment(membership));
        await world.SaveAsync();
        Assert.Equal(before, File.ReadAllBytes(levelPath));
        await player.SaveAsync();
        var loadedPlayer = TestServerFactory.CreateConnectedPlayer(context.Server, world, "Loaded", uuid: player.Uuid);
        await loadedPlayer.LoadAsync(loadFromPersistentWorld: false);
        var loadedEntity = EntityNbt.Load(entityNbt, world)!;
        var region = world.GetRegionForChunk(0, 0)!;
        await region.UnloadChunk(0, 0);
        var loadedChunk = (await region.GetChunkAsync(0, 0))!;
        Assert.NotSame(chunk, loadedChunk);

        // Unknown NBT can be loaded and saved while no codec exists; a new load decodes it again.
        await loadedPlayer.SaveAsync();
        var reloaded = (await manager.LoadPluginAsync(path))!;
        var (newNetworks, newMembership) = SavedTypes(reloaded);
        var newLevel = world.GetSavedData(newNetworks, () => throw new Exception("Should read disk"));
        Assert.NotSame(level, newLevel);
        Assert.Equal([1, 3, 9], newLevel.Value);
        Assert.Equal([2, 4], world.GetSavedData(newNetworks, () => [], SavedDataScope.World).Value);
        Assert.Equal([5], loadedPlayer.GetAttachment(newMembership));
        Assert.Equal([6], loadedEntity.GetAttachment(newMembership));
        Assert.Equal([7], loadedChunk.GetAttachment(newMembership));
        Assert.Throws<InvalidOperationException>(() => chunk.SetAttachment(newMembership, [99]));
        Assert.Throws<InvalidOperationException>(() => chunk.RemoveAttachment(newMembership));
        Assert.Throws<InvalidOperationException>(() => entity.SetAttachment(newMembership, [99]));
        Assert.Throws<InvalidOperationException>(() => entity.RemoveAttachment(newMembership));
        newLevel.Value.Add(8);
        newLevel.MarkDirty();
        Assert.Throws<InvalidOperationException>(() => level.MarkDirty());
        await world.SaveAsync();
        Assert.Equal([1, 3, 9, 8], ReadValues(levelPath));
        var newDimensionData = dimension.GetSavedData(newNetworks, () => []);
        newDimensionData.Value.Add(11);
        newDimensionData.MarkDirty();
        await world.FlushAsync();
        Assert.Equal([10, 11], ReadValues(Path.Combine(dimension.FolderPath, "data", "sample", "networks.dat")));
        Assert.True(loadedChunk.RemoveAttachment(newMembership));
        Assert.False(loadedChunk.TryGetAttachment(newMembership, out _));
        await world.UnloadPlayerAsync(player.Uuid);
        Assert.Throws<InvalidOperationException>(() => player.SetAttachment(newMembership, [99]));
        Assert.Throws<InvalidOperationException>(() => player.RemoveAttachment(newMembership));
        Assert.Equal([5], player.GetAttachment(newMembership));
    }

    private static (SavedDataType<List<int>>, AttachmentType<List<int>>) SavedTypes(PluginContainer plugin)
    {
        var instance = plugin.Plugin!;
        var state = instance.GetType().GetProperty("SavedState")!.GetValue(instance)!;
        var type = state.GetType();
        return (
            (SavedDataType<List<int>>)type.GetProperty("Networks")!.GetValue(state)!,
            (AttachmentType<List<int>>)type.GetProperty("Membership")!.GetValue(state)!);
    }

    private static int[] ReadValues(string path)
    {
        using var stream = File.OpenRead(path);
        var root = (NbtCompound)new NbtReader(stream, NbtCompression.GZip).ReadNextTag();
        return ((NbtArray<int>)root["data"]).GetArray();
    }
}
