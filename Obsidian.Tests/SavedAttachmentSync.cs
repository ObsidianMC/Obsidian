using Obsidian.API;
using Obsidian.API.Data;
using Obsidian.API.Inventory.DataComponents;
using Obsidian.API.Plugins;
using Obsidian.API.Utilities;
using Obsidian.Entities;
using Obsidian.Nbt;
using Obsidian.Net;
using Obsidian.Net.Packets.Common;
using Obsidian.Net.Payloads;
using Obsidian.Plugins;
using Obsidian.Plugins.Archives;
using Obsidian.Tests.Fakes;
using Obsidian.Tests.Utilities;
using Obsidian.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Obsidian.Tests;

public sealed class SavedAttachmentSync
{
    [Fact]
    public async Task AttachmentsSyncOnlyToNegotiatedClientsAndApplyDeathPolicy()
    {
        await using var context = TestServerFactory.CreateServer($"attachment-sync-{Guid.NewGuid():N}");
        var world = context.World;
        var chunk = WorldBlockEntities.AddEmptyChunk(world);
        var modded = TestServerFactory.CreateConnectedPlayer(context.Server, world, "Modded");
        var vanilla = TestServerFactory.CreateConnectedPlayer(context.Server, world, "Vanilla");
        var client = (TestClient)modded.Client;
        client.SetState(ClientState.Configuration);
        var channels = new ClientChannels(client, new PayloadChannelRegistry());
        client.Channels = channels;
        using (var query = new NetworkBuffer())
        {
            ChannelPayloads.WriteQuery(query, new Dictionary<NetworkProtocol, List<NetworkComponent>>
            {
                [NetworkProtocol.Play] = [new(AttachmentPayload.Channel, "1", PacketFlow.Clientbound, true)]
            });
            await channels.HandlePayloadAsync(ChannelPayloads.NeoForgeQuery, query.AsSpan(0, query.Size).ToArray());
        }

        Assert.True(await channels.FinishHandshakeAsync(0));
        client.SetState(ClientState.Play);
        Assert.True(client.HasChannel(AttachmentPayload.Channel));

        // A vanilla client cannot enable the engine channel just by registering its name.
        var vanillaClient = (TestClient)vanilla.Client;
        vanillaClient.Channels = new ClientChannels(vanillaClient, new PayloadChannelRegistry());
        await vanillaClient.Channels.HandlePayloadAsync(
            ChannelPayloads.Register, ChannelPayloads.WriteChannelList([AttachmentPayload.Channel]));
        Assert.False(vanillaClient.HasChannel(AttachmentPayload.Channel));

        var owner = new PluginContainer
        {
            Info = new PluginInfo("attachmentsync"),
            Archive = PluginArchive.Open("attachmentsync.obby", PluginArchives.Zip(new Dictionary<string, byte[]>
            {
                [PluginMetadata.FileName] = PluginArchives.Metadata("attachmentsync", "1.0.0", "Attachments")
            }))
        };
        var calls = 0;
        var codec = new ComponentCodec<int[]>(
            values =>
            {
                calls++;
                return new NbtArray<int>("", values);
            },
            tag => ((NbtArray<int>)tag).GetArray().ToArray());
        var network = new ComponentNetworkCodec<int[]>(
            (writer, values) => writer.WriteInt(values[0]), reader => [reader.ReadInt()]);
        var replacement = new PluginContainer
        {
            Info = owner.Info,
            Archive = owner.Archive
        };
        try
        {
            var synced = PluginRegistry.RegisterAttachment(owner, "attachmentsync:kept", codec, network, true);
            var transient = PluginRegistry.RegisterAttachment(owner, "attachmentsync:transient", codec, null, false);
            foreach (var path in new[]
            {
                "../escape",
                "/rooted",
                "bad\\path",
                "bad?path",
                "foo.dat/bar",
                "foo.dat.tmp/bar"
            })
            {
                Assert.Throws<ArgumentException>(() =>
                    PluginRegistry.RegisterSavedData(owner, "attachmentsync:" + path, codec));
            }

            Assert.NotNull(PluginRegistry.RegisterSavedData(owner, "attachmentsync:foo/bar", codec));
            var entity = new Entity { Level = world, Type = EntityType.Pig };
            Assert.True(world.TryAddEntity(entity));
            ((IPlayer)modded).TrackEntity(entity);
            modded.LoadedChunks.Add(NumericsHelper.IntsToLong(0, 0));
            vanilla.LoadedChunks.Add(NumericsHelper.IntsToLong(0, 0));
            modded.SetAttachment(synced, [1]);
            modded.SetAttachment(transient, [9]);
            entity.SetAttachment(synced, [2]);
            chunk.SetAttachment(synced, [3]);
            vanilla.SetAttachment(synced, [4]);
            modded.visiblePlayers.Add(vanilla);
            client.ClearPackets();
            vanillaClient.ClearPackets();
            modded.SynchronizeAttachments();
            vanilla.SynchronizeAttachments();

            var packets = client.SentPackets.OfType<CustomPayloadPacket>().ToArray();
            Assert.Equal(4, packets.Length);
            var values = packets.Select(packet =>
            {
                Assert.Equal(AttachmentPayload.Channel, packet.Channel);
                using var buffer = new NetworkBuffer(packet.PluginData.ToArray());
                return AttachmentPayload.Decode(buffer, client: false);
            }).ToDictionary(pair => pair.Target, pair => pair.Data);
            Assert.Equal([1], ((NbtArray<int>)values[new(AttachmentTargetKind.Player)][synced.Identifier]).GetArray());
            Assert.Equal([2], ((NbtArray<int>)values[new(AttachmentTargetKind.Entity, entity.EntityId)]
                [synced.Identifier]).GetArray());
            Assert.Equal([3], ((NbtArray<int>)values[new(AttachmentTargetKind.Chunk, 0, 0)]
                [synced.Identifier]).GetArray());
            Assert.Equal([4], ((NbtArray<int>)values[new(AttachmentTargetKind.Entity, vanilla.EntityId)]
                [synced.Identifier]).GetArray());
            Assert.All(values.Values, saved => Assert.False(saved.HasTag(transient.Identifier)));
            Assert.Empty(vanillaClient.SentPackets.OfType<CustomPayloadPacket>());
            client.ClearPackets();
            modded.SynchronizeAttachments();
            Assert.Empty(client.SentPackets);

            modded.visiblePlayers.Remove(vanilla);
            modded.SynchronizeAttachments();
            modded.visiblePlayers.Add(vanilla);
            client.ClearPackets();
            modded.SynchronizeAttachments();
            var visibleAgain = Assert.Single(client.SentPackets.OfType<CustomPayloadPacket>());
            using (var buffer = new NetworkBuffer(visibleAgain.PluginData.ToArray()))
            {
                var (target, saved) = AttachmentPayload.Decode(buffer, client: false);
                Assert.Equal(new AttachmentTarget(AttachmentTargetKind.Entity, vanilla.EntityId), target);
                Assert.Equal([4], ((NbtArray<int>)saved[synced.Identifier]).GetArray());
            }

            Assert.True(entity.RemoveAttachment(synced));
            client.ClearPackets();
            modded.SynchronizeAttachments();
            Assert.Contains(client.SentPackets.OfType<CustomPayloadPacket>(), packet =>
            {
                using var buffer = new NetworkBuffer(packet.PluginData.ToArray());
                var (target, saved) = AttachmentPayload.Decode(buffer, client: false);
                return target == new AttachmentTarget(AttachmentTargetKind.Entity, entity.EntityId)
                    && saved.Count == 0;
            });

            modded.Health = 0;
            await modded.RespawnAsync();
            Assert.Equal([1], modded.GetAttachment(synced));
            Assert.False(modded.TryGetAttachment(transient, out _));

            // A worker encoding during the final chunk save must fail instead of publishing a lost write.
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var retirement = PluginRegistry.RegisterAttachment(owner, "attachmentsync:retirement",
                new ComponentCodec<int>(value =>
                {
                    if (value == 2)
                    {
                        started.SetResult();
                        finish.Task.GetAwaiter().GetResult();
                    }

                    return new NbtTag<int>("", value);
                }, tag => ((NbtTag<int>)tag).Value), null, false);
            chunk.SetAttachment(retirement, 1);
            var write = Task.Run(() => chunk.SetAttachment(retirement, 2));
            var region = world.GetRegionForChunk(0, 0)!;
            try
            {
                await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
                await region.UnloadChunk(0, 0);
            }
            finally
            {
                finish.TrySetResult();
            }

            await Assert.ThrowsAsync<InvalidOperationException>(() => write);
            var loadedChunk = (await region.GetChunkAsync(0, 0))!;
            Assert.Equal(1, loadedChunk.GetAttachment(retirement));
            Assert.Throws<InvalidOperationException>(() => chunk.RemoveAttachment(retirement));

            // An encode that was already running during reload cannot publish over the new load's attachment.
            AttachmentType<int[]> newRace = null;
            var raceCodec = new ComponentCodec<int[]>(values =>
            {
                owner.Close();
                owner.UndoHostAdditions();
                newRace = PluginRegistry.RegisterAttachment(
                    replacement, "attachmentsync:race", codec, network, true);
                modded.SetAttachment(newRace, [77]);
                return new NbtArray<int>("", values);
            }, tag => ((NbtArray<int>)tag).GetArray());
            var oldRace = PluginRegistry.RegisterAttachment(owner, "attachmentsync:race", raceCodec, null, true);
            Assert.Throws<InvalidOperationException>(() => modded.SetAttachment(oldRace, [10]));
            Assert.Equal([77], modded.GetAttachment(newRace));

            // No unloaded codec runs for saved NBT, death policy or sync; the player's kept NBT survives.
            owner.Close();
            owner.UndoHostAdditions();
            var before = calls;
            AttachmentStorage.For(modded).Respawn();
            Assert.Throws<InvalidOperationException>(() => modded.SetAttachment(synced, [100]));
            Assert.Equal(before, calls);
            Assert.True(AttachmentStorage.For(modded).Snapshot().HasTag(synced.Identifier));
        }
        finally
        {
            owner.Close();
            owner.UndoHostAdditions();
            await owner.DisposeAsync();
            replacement.Close();
            replacement.UndoHostAdditions();
            await replacement.DisposeAsync();
        }
    }
}
