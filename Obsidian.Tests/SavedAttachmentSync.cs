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
            Assert.Throws<ArgumentException>(() =>
                PluginRegistry.RegisterSavedData(owner, "attachmentsync:../escape", codec));
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
            client.ClearPackets();
            vanillaClient.ClearPackets();
            modded.SynchronizeAttachments();
            vanilla.SynchronizeAttachments();

            var packets = client.SentPackets.OfType<CustomPayloadPacket>().ToArray();
            Assert.Equal(3, packets.Length);
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
            Assert.All(values.Values, saved => Assert.False(saved.HasTag(transient.Identifier)));
            Assert.Empty(vanillaClient.SentPackets.OfType<CustomPayloadPacket>());
            client.ClearPackets();
            modded.SynchronizeAttachments();
            Assert.Empty(client.SentPackets);

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
