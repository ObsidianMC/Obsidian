using System;
using System.Linq;
using System.Threading.Tasks;
using Obsidian.API;
using Obsidian.Entities;
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
}
