using Obsidian.API;
using Obsidian.Net.Packets;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Net.Packets.Play.Serverbound;
using Obsidian.Tests.Fakes;
using Obsidian.Tests.Utilities;
using Obsidian.WorldData;
using System.Threading.Tasks;
using Xunit;

namespace Obsidian.Tests;

public sealed partial class PendingTeleports
{
    [Fact]
    public async Task ClientMovementAndConfirmationsNormalizeExactRotation()
    {
        await using var context = Create();
        var player = AddPlayer(context, "Rotation");
        var client = (TestClient)player.Client;

        foreach (var row in ClientRotationCases.Finite)
        {
            await MoveWithRotation(start, row.Yaw, row.Pitch).HandleAsync(context.Server, player);
            ClientRotationCases.AssertRotation(player, row.ExpectedYaw, row.ExpectedPitch);

            await Rotate(row.Yaw, row.Pitch).HandleAsync(context.Server, player);
            ClientRotationCases.AssertRotation(player, row.ExpectedYaw, row.ExpectedPitch);

            await player.TeleportAsync(start);
            var confirmation = Read<AcceptTeleportationPacket>(buffer =>
            {
                buffer.WriteVarInt(player.TeleportId);
                VectorD.Write(start, buffer);
                buffer.WriteSingle(row.Yaw);
                buffer.WriteSingle(row.Pitch);
            });
            await confirmation.HandleAsync(context.Server, player);

            Assert.True(client.Connected);
            Assert.False(player.HasPendingTeleport);
            ClientRotationCases.AssertRotation(player, row.ExpectedYaw, row.ExpectedPitch);
        }
    }

    [Fact]
    public async Task NonFiniteMovementDisconnectsBeforeChangingState()
    {
        await using var context = Create();
        foreach (var pending in new[] { false, true })
        {
            VectorD[] positions =
            [
                new(double.NaN, 80, 4.5),
                new(4.5, double.NaN, 4.5),
                new(4.5, 80, double.NaN)
            ];
            foreach (var position in positions)
            {
                await AssertInvalidMovementAsync(Move(position), pending);
                await AssertInvalidMovementAsync(MoveWithRotation(position, 45, 30), pending);
            }

            foreach (var invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
            {
                await AssertInvalidMovementAsync(Rotate(invalid, 30), pending);
                await AssertInvalidMovementAsync(Rotate(45, invalid), pending);
                await AssertInvalidMovementAsync(MoveWithRotation(start, invalid, 30), pending);
                await AssertInvalidMovementAsync(MoveWithRotation(start, 45, invalid), pending);
            }
        }

        async Task AssertInvalidMovementAsync(ServerboundPacket packet, bool pending)
        {
            var player = AddPlayer(context, $"InvalidMovement{context.Server.OnlinePlayers.Count}");
            var client = (TestClient)player.Client;
            if (pending)
                await player.TeleportAsync(start);

            await packet.HandleAsync(context.Server, player);

            Assert.False(client.Connected);
            Assert.Equal("multiplayer.disconnect.invalid_player_movement", client.DisconnectReason?.Translate);
            Assert.Equal(start, player.Position);
            Assert.Equal((0f, 0f), (player.Yaw, player.Pitch));
            Assert.Equal(MovementFlags.None, player.MovementFlags);
        }
    }

    [Fact]
    public async Task ClientCoordinatesClampToVanillaMovementLimits()
    {
        await using var context = Create();
        var player = AddPlayer(context, "ClampedCoordinates");
        var client = (TestClient)player.Client;
        (VectorD Input, VectorD Expected)[] cases =
        [
            (new(double.PositiveInfinity, 80, 4.5), new(3.0E7, 80, 4.5)),
            (new(double.NegativeInfinity, 80, 4.5), new(-3.0E7, 80, 4.5)),
            (new(4.5, double.PositiveInfinity, 4.5), new(4.5, 2.0E7, 4.5)),
            (new(4.5, double.NegativeInfinity, 4.5), new(4.5, -2.0E7, 4.5)),
            (new(4.5, 80, double.PositiveInfinity), new(4.5, 80, 3.0E7)),
            (new(4.5, 80, double.NegativeInfinity), new(4.5, 80, -3.0E7)),
            (new(double.MaxValue, double.MaxValue, double.MaxValue), new(3.0E7, 2.0E7, 3.0E7)),
            (new(-double.MaxValue, -double.MaxValue, -double.MaxValue), new(-3.0E7, -2.0E7, -3.0E7))
        ];

        foreach (var row in cases)
        {
            // Keep movement within an already loaded chunk; no terrain generation or distance checks are needed.
            var (x, z) = Region.ChunkOf(row.Expected);
            var world = context.World;
            world.LoadRegionByChunk(x, z).SetChunk(new Chunk(x, z, world.MinY, world.Height, ChunkGenStage.full));
            var initial = row.Expected + new VectorD(0.125, 0, 0);

            player.Position = initial;
            player.LastPosition = initial;
            await Move(row.Input).HandleAsync(context.Server, player);
            Assert.Equal(row.Expected, player.Position);
            Assert.True(client.Connected);

            player.Position = initial;
            player.LastPosition = initial;
            await MoveWithRotation(row.Input, 12.345f, -23.456f).HandleAsync(context.Server, player);
            Assert.Equal(row.Expected, player.Position);
            Assert.True(client.Connected);
            ClientRotationCases.AssertRotation(player, 12.345f, -23.456f);

            player.Position = initial;
            player.LastPosition = initial;
            await player.SendPositionAsync(VectorD.Zero);
            await client.ConfirmTeleportAsync(new PlayerPositionPacket
            {
                TeleportId = player.TeleportId,
                Position = row.Input,
                Yaw = 12.345f,
                Pitch = -23.456f
            });
            Assert.Equal(row.Expected, player.Position);
            Assert.True(client.Connected);
            Assert.False(player.HasPendingTeleport);
        }
    }

    [Fact]
    public async Task NonFiniteConfirmationRotationDisconnectsWithoutApplyingIt()
    {
        await using var context = Create();
        foreach (var invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
        {
            (float Yaw, float Pitch)[] rotations = [(invalid, 0), (0, invalid)];
            foreach (var rotation in rotations)
            {
                var player = AddPlayer(context, $"InvalidConfirmation{context.Server.OnlinePlayers.Count}");
                var client = (TestClient)player.Client;
                await player.TeleportAsync(start);

                await player.ConfirmTeleportAsync(player.TeleportId, start, rotation.Yaw, rotation.Pitch);

                Assert.False(client.Connected);
                Assert.Equal("multiplayer.disconnect.invalid_player_movement", client.DisconnectReason?.Translate);
                Assert.Equal(start, player.Position);
                Assert.Equal((0f, 0f), (player.Yaw, player.Pitch));
            }
        }
    }
}
