#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Obsidian.API;
using Obsidian.Net;
using Obsidian.Net.Packets;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Net.Packets.Play.Serverbound;
using Xunit;

namespace Obsidian.Tests;

/// <summary>
/// Replays vanilla's movement and interaction packets, captured by the asset generator's MovementFixtures from the
/// same inputs as the rows here: Obsidian writes each clientbound packet to vanilla's bytes and reads each serverbound
/// one to vanilla's values.
/// </summary>
public class MovementPayloads
{
    private static readonly Dictionary<string, Action<byte[]>> packets = new()
    {
        ["move_entity_pos"] = Writes(new MoveEntityPosPacket { EntityId = 301, Delta = new Vector(1234, -56, 32767), OnGround = true }),
        ["move_entity_pos_rot"] = Writes(new MoveEntityPosRotPacket
        {
            EntityId = 302, Delta = new Vector(-32768, 0, 17), Yaw = new Angle(64), Pitch = new Angle(16), OnGround = false
        }),
        ["move_entity_rot"] = Writes(new MoveEntityRotPacket { EntityId = 303, Yaw = new Angle(128), Pitch = new Angle(16), OnGround = true }),
        ["entity_position_sync"] = Writes(new EntityPositionSyncPacket
        {
            EntityId = 304, Position = new VectorD(-1024.25, 70.5, 3e6 + 0.125), Yaw = 90, Pitch = 22.5f, OnGround = true
        }),
        ["teleport_entity"] = Writes(new TeleportEntityPacket
        {
            EntityId = 305, Position = new VectorD(1.5, -64, 2.25), Delta = new VectorD(0.5, -0.25, 0), Yaw = 90f, Pitch = 22.5f
        }),
        ["set_entity_motion"] = Writes(new SetEntityMotionPacket { EntityId = 306, Velocity = new Velocity(0.25, -0.5, 1.75) }),
        ["swing_animation"] = Writes(new SwingAnimationPacket
        {
            EntityId = 307, Hand = InteractionHand.OffHand,
            Animation = new SwingAnimation { Type = SwingAnimationType.Stab, Duration = 19 }
        }),
        ["animate_wake_up"] = Writes(new AnimatePacket { EntityId = 308, Animation = EntityAnimationType.WakeUp }),
        ["open_sign_editor"] = Writes(new OpenSignEditorPacket { Position = new Vector(-12, 70, 23), Slot = SignTextSlot.Back }),

        ["accept_teleportation"] = Reads<AcceptTeleportationPacket>(packet =>
        {
            Assert.Equal(417, packet.TeleportId);
            Assert.Equal(new VectorD(-12.5, 64, 1e7 + 0.75), packet.Position);
            Assert.Equal((-170.5f, 45.25f), (packet.Yaw, packet.Pitch));
        }),
        ["attack"] = Reads<AttackPacket>(packet => Assert.Equal(300000, packet.EntityId)),
        ["interact"] = Reads<InteractPacket>(packet =>
        {
            Assert.Equal((309, InteractionHand.OffHand, true), (packet.EntityId, packet.Hand, packet.Sneaking));
            // A low-precision vector: within a step of its 15-bit quantization over the largest axis, rounded up.
            Assert.Equal(0.25f, packet.Location.X, 1e-3f);
            Assert.Equal(1.5f, packet.Location.Y, 1e-3f);
            Assert.Equal(-0.375f, packet.Location.Z, 1e-3f);
        }),
        ["punch"] = Reads<PunchPacket>(_ => { }),
        ["spectator_action"] = Reads<SpectatorActionPacket>(packet => Assert.Equal(310, packet.SpectateEntityId)),
        ["spectator_action_none"] = Reads<SpectatorActionPacket>(packet => Assert.Null(packet.SpectateEntityId)),
        ["sign_update"] = Reads<SignUpdatePacket>(packet =>
        {
            Assert.Equal(new Vector(5, -60, -7), packet.Position);
            Assert.Equal(["Hello", "", "世界", "line four"], packet.Lines);
            Assert.Equal(SignTextSlot.Front, packet.Slot);
        }),
        ["pick_item_from_block"] = Reads<PickItemFromBlockPacket>(packet =>
            Assert.Equal((new Vector(-30000, 319, 29999), true), (packet.Position, packet.IncludeData))),
        ["move_player_pos_rot"] = Reads<MovePlayerPosRotPacket>(packet =>
        {
            Assert.Equal(new VectorD(0.5, 65, -0.5), packet.Position);
            Assert.Equal(((Angle)359.5f, (Angle)(-90f)), (packet.Yaw, packet.Pitch));
            Assert.Equal(MovementFlags.OnGround | MovementFlags.HorizontalCollision, packet.MovementFlags);
        }),
        ["move_player_rot"] = Reads<MovePlayerRotPacket>(packet =>
        {
            Assert.Equal(((Angle)(-45f), (Angle)12.5f), (packet.Yaw, packet.Pitch));
            Assert.Equal(MovementFlags.HorizontalCollision, packet.MovementFlags);
        }),
        ["use_item_on"] = Reads<UseItemOnPacket>(packet =>
        {
            Assert.Equal((InteractionHand.MainHand, new Vector(4, 70, -4), BlockFace.West), (packet.Hand, packet.Position, packet.Face));
            Assert.Equal(new VectorD(0.5, 0.25, 0.25), packet.Cursor);
            Assert.Equal((true, false, 42), (packet.InsideBlock, packet.IsWorldBorderHit, packet.Sequence));
        }),
        ["use_item"] = Reads<UseItemPacket>(packet => Assert.Equal((InteractionHand.OffHand, 43), (packet.Hand, packet.Sequence))),
        ["player_action"] = Reads<PlayerActionPacket>(packet =>
            Assert.Equal((PlayerActionStatus.StartedDigging, new Vector(1, 2, 3), BlockFace.Up, 44),
                (packet.Status, packet.Position, packet.Face, packet.Sequence))),
    };

    private static JsonDocument Fixture()
    {
        var name = $"Obsidian.Assets.client-movement-{ServerConstants.ProtocolDescription}.json";
        using var stream = typeof(NetworkBuffer).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Missing vanilla movement fixture {name}; rebuild vanilla assets.");
        return JsonDocument.Parse(stream);
    }

    // Every captured packet, so that a capture without a row here fails rather than going untested.
    public static TheoryData<string> Captured()
    {
        using var fixture = Fixture();
        return new(fixture.RootElement.EnumerateObject().Select(packet => packet.Name));
    }

    [Theory]
    [MemberData(nameof(Captured))]
    public void MatchesVanilla(string packet)
    {
        using var fixture = Fixture();
        var bytes = Convert.FromHexString(fixture.RootElement.GetProperty(packet).GetString()!);

        Assert.True(packets.TryGetValue(packet, out var check), $"No row replays vanilla's {packet}.");
        check(bytes);
    }

    private static Action<byte[]> Writes(ClientboundPacket packet) => vanilla =>
    {
        using var buffer = new NetworkBuffer();
        packet.Serialize(buffer);
        Assert.Equal(Convert.ToHexString(vanilla), Convert.ToHexString(buffer.AsSpan(0, buffer.Size)));
    };

    private static Action<byte[]> Reads<TPacket>(Action<TPacket> check) where TPacket : ServerboundPacket, new() => vanilla =>
    {
        using var buffer = new NetworkBuffer(vanilla);
        var packet = new TPacket();
        packet.Populate(buffer);
        Assert.Equal(vanilla.Length, buffer.Offset);
        check(packet);
    };
}
