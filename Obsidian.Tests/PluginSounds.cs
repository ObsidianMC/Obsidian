using Obsidian.API;
using Obsidian.API.Builders;
using Obsidian.API.Plugins;
using Obsidian.Net;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Plugins;
using Obsidian.Plugins.Archives;
using Obsidian.Registries;
using Obsidian.Tests.Fakes;
using Obsidian.Tests.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Obsidian.Tests;

public sealed class PluginSounds
{
    [Fact]
    public async Task RegisteredSoundsUseInlineHoldersForPositionAndEntity()
    {
        await using var context = TestServerFactory.CreateServer($"sounds-{Guid.NewGuid():N}");
        var player = TestServerFactory.CreateConnectedPlayer(context.Server, context.World, "Listener");
        var client = (TestClient)player.Client;
        var owner = new PluginContainer
        {
            Info = new PluginInfo("soundprobe"),
            Archive = PluginArchive.Open("soundprobe.obby", PluginArchives.Zip(new Dictionary<string, byte[]>
            {
                [PluginMetadata.FileName] = PluginArchives.Metadata("soundprobe", "1.0.0", "Sound probe")
            }))
        };

        try
        {
            Assert.Throws<ArgumentException>(() => PluginRegistry.RegisterSound(owner, "another:chime", null));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                PluginRegistry.RegisterSound(owner, "soundprobe:chime", float.NaN));

            foreach (float? range in new float?[] { null, 24 })
            {
                var sound = PluginRegistry.RegisterSound(owner, "soundprobe:chime", range);
                var positioned = SoundEffectBuilder.Create(sound, SoundCategory.Blocks)
                    .WithSoundPosition(new SoundPosition(1.5, -2.5, 3.5))
                    .WithVolume(0.7f)
                    .WithPitch(1.2f)
                    .WithSeed(42)
                    .Build();
                await player.SendSoundAsync(positioned);
                await player.SendSoundAsync(SoundEffectBuilder.Create(sound, SoundCategory.Blocks)
                    .WithEntityId(player.EntityId)
                    .WithVolume(0.7f)
                    .WithPitch(1.2f)
                    .WithSeed(42)
                    .Build());

                var packets = client.SentPackets.TakeLast(2).ToArray();
                Assert.IsType<SoundPacket>(packets[0]);
                Assert.IsType<SoundEntityPacket>(packets[1]);
                for (var index = 0; index < packets.Length; index++)
                {
                    using var bytes = new NetworkBuffer();
                    packets[index].Serialize(bytes);
                    using var reader = new NetworkBuffer(bytes.AsSpan(0, bytes.Size).ToArray());
                    // Vanilla 1.21.11 Holder<SoundEvent>: zero + identifier + optional float, never a mod id.
                    Assert.Equal(0, reader.ReadVarInt());
                    Assert.Equal("soundprobe:chime", reader.ReadString());
                    Assert.Equal(range.HasValue, reader.ReadBoolean());
                    if (range.HasValue)
                        Assert.Equal(range.Value, reader.ReadSingle());

                    Assert.Equal((int)SoundCategory.Blocks, reader.ReadVarInt());
                    if (index == 1)
                        Assert.Equal(player.EntityId, reader.ReadVarInt());
                    else
                    {
                        Assert.Equal(12, reader.ReadInt());
                        Assert.Equal(-20, reader.ReadInt());
                        Assert.Equal(28, reader.ReadInt());
                    }

                    Assert.Equal(0.7f, reader.ReadSingle());
                    Assert.Equal(1.2f, reader.ReadSingle());
                    Assert.Equal(42, reader.ReadLong());
                }
            }

            SoundEventsRegistry.ReleasePlugin(owner);
            Assert.False(SoundEventsRegistry.TryGet("soundprobe:chime", out _));
            var reloaded = PluginRegistry.RegisterSound(owner, "soundprobe:chime", 12);
            Assert.True(SoundEventsRegistry.TryGet(reloaded.ResourceLocation, out var current));
            Assert.Equal(12, current.FixedRange);
        }
        finally
        {
            SoundEventsRegistry.ReleasePlugin(owner);
        }
    }
}
