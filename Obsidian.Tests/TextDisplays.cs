using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Obsidian.API;
using Obsidian.Entities;
using Obsidian.Entities.Factories;
using Obsidian.Nbt;
using Obsidian.Net;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Tests.Fakes;
using Xunit;

namespace Obsidian.Tests;

public class TextDisplays
{
    // Vanilla's metadata types for indexes 0 to 27, in order: the base entity's 0 to 7, then the Display fields and the
    // TextDisplay fields.
    private static readonly EntityMetadataType[] ExpectedTypes =
    [
        EntityMetadataType.Byte, EntityMetadataType.VarInt, EntityMetadataType.OptionalTextComponent, EntityMetadataType.Boolean,
        EntityMetadataType.Boolean, EntityMetadataType.Boolean, EntityMetadataType.Pose, EntityMetadataType.VarInt,
        EntityMetadataType.VarInt, EntityMetadataType.VarInt, EntityMetadataType.VarInt, EntityMetadataType.Vector3,
        EntityMetadataType.Vector3, EntityMetadataType.Quaternion, EntityMetadataType.Quaternion, EntityMetadataType.Byte,
        EntityMetadataType.VarInt, EntityMetadataType.Float, EntityMetadataType.Float, EntityMetadataType.Float,
        EntityMetadataType.Float, EntityMetadataType.Float, EntityMetadataType.VarInt, EntityMetadataType.TextComponent,
        EntityMetadataType.VarInt, EntityMetadataType.VarInt, EntityMetadataType.Byte, EntityMetadataType.Byte,
    ];

    [Fact(DisplayName = "A text display is created as a text display, not a living entity")]
    public void SpawnerCreatesTextDisplays()
    {
        var created = EntitySpawner.Create(EntityType.TextDisplay, null!);

        Assert.IsType<TextDisplay>(created);
        Assert.IsNotType<Living>(created);
        Assert.Equal(EntityType.TextDisplay, created.Type);
    }

    // Reads one metadata value as the given type, the way the client does.
    private static object ReadValue(NetworkBuffer reader, int index, EntityMetadataType type) => type switch
    {
        EntityMetadataType.Byte => reader.ReadByte(),
        EntityMetadataType.Boolean => reader.ReadBoolean(),
        EntityMetadataType.Float => reader.ReadSingle(),
        EntityMetadataType.TextComponent => reader.ReadChat(),
        EntityMetadataType.OptionalTextComponent => reader.ReadBoolean() ? reader.ReadChat() : null!,
        EntityMetadataType.Vector3 => new VectorF { X = reader.ReadSingle(), Y = reader.ReadSingle(), Z = reader.ReadSingle() },
        EntityMetadataType.Quaternion => new TextDisplay.Rotation(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()),
        _ => reader.ReadVarInt(),
    };

    [Fact(DisplayName = "A text display's metadata uses vanilla's indexes and types")]
    public void WritesMetadataAtVanillaIndexes()
    {
        var display = new TextDisplay
        {
            Level = null!,
            Text = ChatMessage.Simple("Hello"),
            Billboard = TextDisplayBillboard.Center,
            BackgroundColor = unchecked((int)0x80112233),
            LineWidth = 120,
            TextOpacity = 200,
            Shadow = true,
            SeeThrough = true,
            Alignment = TextDisplayAlignment.Right,
            ViewRange = 0.5f,
            Translation = new VectorF { X = 0, Y = 1.25f, Z = 0 },
            Scale = new VectorF { X = 2, Y = 2, Z = 2 },
            PositionInterpolationDuration = 3,
        };

        using var writer = new NetworkBuffer();
        display.Write(writer);
        using var reader = new NetworkBuffer(writer.AsSpan(0, writer.Size).ToArray());

        var values = new Dictionary<int, object>();
        for (var index = 0; index < ExpectedTypes.Length; index++)
        {
            Assert.Equal(index, reader.ReadByte());
            Assert.Equal((int)ExpectedTypes[index], reader.ReadVarInt());
            values[index] = ReadValue(reader, index, ExpectedTypes[index]);
        }

        Assert.Equal("Hello", ((ChatMessage)values[23]).Text);
        Assert.Equal(0, values[8]);
        Assert.Equal(3, values[10]);
        Assert.Equal(1.25f, ((VectorF)values[11]).Y);
        Assert.Equal(2f, ((VectorF)values[12]).X);
        Assert.Equal(new TextDisplay.Rotation(0, 0, 0, 1), values[13]);
        Assert.Equal((byte)TextDisplayBillboard.Center, values[15]);
        Assert.Equal(-1, values[16]);
        Assert.Equal(0.5f, values[17]);
        Assert.Equal(-1, values[22]);
        Assert.Equal(120, values[24]);
        Assert.Equal(unchecked((int)0x80112233), values[25]);
        Assert.Equal((byte)200, values[26]);
        // Shadow (1), see-through (2), right alignment (16).
        Assert.Equal((byte)(1 | 2 | 16), values[27]);
    }

    [Fact(DisplayName = "A text display keeps its text and display fields through saving")]
    public void SavesAndLoadsTextDisplays()
    {
        var display = new TextDisplay
        {
            Level = null!,
            Text = ChatMessage.Simple("Saved"),
            Billboard = TextDisplayBillboard.Vertical,
            BackgroundColor = 0x12345678,
            LineWidth = 80,
            TextOpacity = 128,
            Shadow = true,
            DefaultBackground = true,
            Alignment = TextDisplayAlignment.Left,
            ViewRange = 2.5f,
            Translation = new VectorF { X = 0.5f, Y = 1, Z = -0.25f },
            Scale = new VectorF { X = 1.5f, Y = 1.5f, Z = 1.5f },
            RightRotation = new TextDisplay.Rotation(0, 0.7071f, 0, 0.7071f),
            BrightnessOverride = (9 << 4) | (3 << 20),
            PositionInterpolationDuration = 3,
            TransformationInterpolationDuration = 2,
        };

        var tag = EntityNbt.Save(display);
        Assert.NotNull(tag);

        var loaded = Assert.IsType<TextDisplay>(EntityNbt.Load(tag, null!));

        Assert.Equal("Saved", loaded.Text.Text);
        Assert.Equal(TextDisplayBillboard.Vertical, loaded.Billboard);
        Assert.Equal(0x12345678, loaded.BackgroundColor);
        Assert.Equal(80, loaded.LineWidth);
        Assert.Equal(128, loaded.TextOpacity);
        Assert.True(loaded.Shadow);
        Assert.True(loaded.DefaultBackground);
        Assert.False(loaded.SeeThrough);
        Assert.Equal(TextDisplayAlignment.Left, loaded.Alignment);
        Assert.Equal(2.5f, loaded.ViewRange);
        Assert.Equal(0.5f, loaded.Translation.X);
        Assert.Equal(-0.25f, loaded.Translation.Z);
        Assert.Equal(1.5f, loaded.Scale.Y);
        Assert.Equal(display.RightRotation, loaded.RightRotation);
        Assert.Equal(display.BrightnessOverride, loaded.BrightnessOverride);
        Assert.Equal(3, loaded.PositionInterpolationDuration);
        Assert.Equal(2, loaded.TransformationInterpolationDuration);
    }

    [Fact(DisplayName = "A transient text display is left out of the saved world")]
    public void TransientTextDisplaysAreNotSaved()
    {
        var display = new TextDisplay { Level = null!, Transient = true };

        Assert.Null(EntityNbt.Save(display));
    }

    [Fact(DisplayName = "Changing a text display's text sends it to players tracking the display", Timeout = TestServerFactory.TestTimeout)]
    public async Task ChangedTextIsSentToTrackingPlayers()
    {
        await using var context = TestServerFactory.CreateServer($"text-display-{Guid.NewGuid():N}");
        WorldBlockEntities.AddEmptyChunk(context.World);
        var position = new VectorD(8, 64, 8);
        var player = TestServerFactory.CreateConnectedPlayer(context.Server, context.World, "DisplayWatcher", position);
        await TestServerFactory.JoinConnectedPlayerAsync(context.Server, player);
        player.LoadedChunks.Add(0L);
        var display = Assert.IsType<TextDisplay>(context.World.SpawnEntity(position, EntityType.TextDisplay));
        Assert.Equal(display.Uuid, player.TrackedEntities[display.EntityId]);
        var client = (TestClient)player.Client;
        client.ClearPackets();

        display.Text = ChatMessage.Simple("Updated");

        var packet = Assert.Single(client.SentPackets.OfType<SetEntityDataPacket>());
        Assert.Equal(display.EntityId, packet.EntityId);
        Assert.Same(display, packet.Entity);

        using var writer = new NetworkBuffer();
        packet.Serialize(writer);
        using var reader = new NetworkBuffer(writer.AsSpan(0, writer.Size).ToArray());
        Assert.Equal(display.EntityId, reader.ReadVarInt());
        for (var index = 0; index < ExpectedTypes.Length; index++)
        {
            Assert.Equal(index, reader.ReadByte());
            Assert.Equal((int)ExpectedTypes[index], reader.ReadVarInt());
            var value = ReadValue(reader, index, ExpectedTypes[index]);
            if (index == 23)
                Assert.Equal("Updated", Assert.IsType<ChatMessage>(value).Text);
        }
    }

    [Fact(DisplayName = "A text display rejects an empty text, which the client can't read")]
    public void RejectsMissingText()
    {
        var display = new TextDisplay { Level = null! };

        Assert.Throws<ArgumentNullException>(() => display.Text = null!);
    }
}
