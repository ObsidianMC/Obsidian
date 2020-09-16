using Obsidian.Net;
using Obsidian.Net.Packets;
using Obsidian.Net.Packets.Handshaking;
using Obsidian.Net.Packets.Login;
using Obsidian.Net.Packets.Play;
using Obsidian.Net.Packets.Play.Client;
using Obsidian.Net.Packets.Play.Server;
using Obsidian.Serializer;
using Obsidian.Serializer.Enums;
using Obsidian.Util;
using Obsidian.Util.DataTypes;
using System.Threading.Tasks;
using Xunit;

namespace Obsidian.Tests
{
    public class Deserialization
    {
        [Fact]
        public async Task IncomingChatMessage()
        {
            var message = "message";

            using var stream = new MinecraftStream();
            await stream.WriteStringAsync(message);
            stream.Position = 0;

            var packet = PacketSerializer.FastDeserialize<IncomingChatMessage>(stream);

            Assert.Equal(message, packet.Message);
        }

        [Fact]
        public async Task Handshake()
        {
            var version = ProtocolVersion.v1_13_2;
            var serverAddress = "serverAddress";
            var serverPort = ushort.MaxValue;
            var nextState = ClientState.Status;

            using var stream = new MinecraftStream();
            await stream.WriteVarIntAsync(version);
            await stream.WriteStringAsync(serverAddress);
            await stream.WriteUnsignedShortAsync(serverPort);
            await stream.WriteVarIntAsync(nextState);
            stream.Position = 0;

            var packet = PacketSerializer.FastDeserialize<Handshake>(stream);

            Assert.Equal(version, packet.Version);
            Assert.Equal(serverAddress, packet.ServerAddress);
            Assert.Equal(serverPort, packet.ServerPort);
            Assert.Equal(nextState, packet.NextState);
        }

        [Fact]
        public async Task LoginStart()
        {
            var username = "username";

            using var stream = new MinecraftStream();
            await stream.WriteStringAsync(username);
            stream.Position = 0;

            var packet = PacketSerializer.FastDeserialize<LoginStart>(stream);

            Assert.Equal(username, packet.Username);
        }

        [Fact]
        public async Task ClientSettings()
        {
            var locale = "locale";
            var viewDistance = sbyte.MaxValue;
            var chatMode = int.MaxValue;
            var chatColors = true;
            var skinParts = byte.MaxValue;
            var mainHand = int.MaxValue;

            using var stream = new MinecraftStream();
            await stream.WriteStringAsync(locale);
            await stream.WriteByteAsync(viewDistance);
            await stream.WriteIntAsync(chatMode);
            await stream.WriteBooleanAsync(chatColors);
            await stream.WriteUnsignedByteAsync(skinParts);
            await stream.WriteIntAsync(mainHand);
            stream.Position = 0;

            var packet = PacketSerializer.FastDeserialize<ClientSettings>(stream);

            Assert.Equal(locale, packet.Locale);
            Assert.Equal(viewDistance, packet.ViewDistance);
            Assert.Equal(chatMode, packet.ChatMode);
            Assert.Equal(chatColors, packet.ChatColors);
            Assert.Equal(skinParts, packet.SkinParts);
            Assert.Equal(mainHand, packet.MainHand);
        }

        [Fact]
        public async Task KeepAlive()
        {
            var keepAliveId = long.MaxValue;

            using var stream = new MinecraftStream();
            await stream.WriteLongAsync(keepAliveId);
            stream.Position = 0;

            var packet = PacketSerializer.FastDeserialize<KeepAlive>(stream);

            Assert.Equal(keepAliveId, packet.KeepAliveId);
        }

        [Fact]
        public async Task PlayerLook()
        {
            var yaw = float.MaxValue;
            var pitch = float.MaxValue;
            var onGround = true;

            using var stream = new MinecraftStream();
            await stream.WriteFloatAsync(yaw);
            await stream.WriteFloatAsync(pitch);
            await stream.WriteBooleanAsync(onGround);
            stream.Position = 0;

            var packet = PacketSerializer.FastDeserialize<PlayerRotation>(stream);

            Assert.Equal(yaw, packet.Yaw);
            Assert.Equal(pitch, packet.Pitch);
            Assert.Equal(onGround, packet.OnGround);
        }

        [Fact]
        public async Task AnimationServerPacket()
        {
            var hand = Hand.OffHand;

            using var stream = new MinecraftStream();
            await stream.WriteVarIntAsync(hand);
            stream.Position = 0;

            var packet = PacketSerializer.FastDeserialize<Animation>(stream);

            Assert.Equal(hand, packet.Hand);
        }

        [Fact]
        public async Task PlayerDigging()
        {
            var status = int.MaxValue;
            var location = new Position(1.0, 2.0, 3.0);
            var face = sbyte.MaxValue;

            using var stream = new MinecraftStream();
            await stream.WriteVarIntAsync(status);
            await stream.WritePositionAsync(location);
            await stream.WriteByteAsync(face);
            stream.Position = 0;

            var packet = PacketSerializer.FastDeserialize<PlayerDigging>(stream);

            Assert.Equal((DiggingStatus)status, packet.Status);
            Assert.Equal(location.X, packet.Location.X);
            Assert.Equal(location.Y, packet.Location.Y);
            Assert.Equal(location.Z, packet.Location.Z);
            Assert.Equal((BlockFace)face, packet.Face);
        }

        [Fact]
        public async Task PlayerBlockPlacement()
        {
            var location = new Position(1.0, 2.0, 3.0);
            var face = BlockFace.Top;
            var hand = int.MaxValue;
            var cursorX = float.MaxValue;
            var cursorY = float.MaxValue;
            var cursorZ = float.MaxValue;

            using var stream = new MinecraftStream();
            await stream.WritePositionAsync(location);
            await stream.WriteVarIntAsync(face);
            await stream.WriteIntAsync(hand);
            await stream.WriteFloatAsync(cursorX);
            await stream.WriteFloatAsync(cursorY);
            await stream.WriteFloatAsync(cursorZ);
            stream.Position = 0;

            var packet = PacketSerializer.FastDeserialize<PlayerBlockPlacement>(stream);

            Assert.Equal(location.X, packet.Location.X);
            Assert.Equal(location.Y, packet.Location.Y);
            Assert.Equal(location.Z, packet.Location.Z);
            Assert.Equal((Hand)hand, packet.Hand);
            Assert.Equal(cursorX, packet.CursorX);
            Assert.Equal(cursorY, packet.CursorY);
            Assert.Equal(cursorZ, packet.CursorZ);
        }

        [Fact]
        public async Task PlayerPositionLook()
        {
            var pitch = new Angle(byte.MaxValue - 1);
            var yaw = new Angle(byte.MaxValue);
            var position = new Position(1.0, 2.0, 3.0);
            var flags = PositionFlags.X | PositionFlags.Y_ROT;
            var teleportId = int.MaxValue;

            using var stream = new MinecraftStream();
            await stream.WriteAsync(DataType.Position, null, position);
            await stream.WriteFloatAsync(yaw.Degrees);
            await stream.WriteFloatAsync(pitch.Degrees);
            await stream.WriteUnsignedByteAsync((byte)flags);
            await stream.WriteVarIntAsync(teleportId);
            stream.Position = 0;

            var packet = PacketSerializer.FastDeserialize<ClientPlayerPositionLook>(stream);

            Assert.Equal(position.X, packet.Position.X);
            Assert.Equal(position.Y, packet.Position.Y);
            Assert.Equal(position.Z, packet.Position.Z);
            Assert.Equal(yaw.Degrees, packet.Pitch);
            Assert.Equal(pitch.Degrees, packet.Yaw);
            Assert.Equal(flags, packet.Flags);
            Assert.Equal(teleportId, packet.TeleportId);
        }

        [Fact]
        public async Task PlayerPosition()
        {
            var position = new Position(1.0, 2.0, 3.0);
            var onGround = true;

            using var stream = new MinecraftStream();
            await stream.WriteDoubleAsync(position.X);
            await stream.WriteDoubleAsync(position.Y);
            await stream.WriteDoubleAsync(position.Z);
            await stream.WriteBooleanAsync(onGround);
            stream.Position = 0;

            var packet = PacketSerializer.FastDeserialize<PlayerPosition>(stream);

            Assert.Equal(position.X, packet.Position.X);
            Assert.Equal(position.Y, packet.Position.Y);
            Assert.Equal(position.Z, packet.Position.Z);
            Assert.Equal(onGround, packet.OnGround);
        }

        [Fact]
        public async Task EncryptionResponse()
        {
            var sharedSecret = new byte[] { 1, 2, 3, 4, 5 };
            var verifyToken = new byte[] { 6, 7, 8, 9, 10 };

            using var stream = new MinecraftStream();
            await stream.WriteVarIntAsync(sharedSecret.Length);
            await stream.WriteAsync(sharedSecret);
            await stream.WriteVarIntAsync(verifyToken.Length);
            await stream.WriteAsync(verifyToken);
            stream.Position = 0;

            var packet = PacketSerializer.FastDeserialize<EncryptionResponse>(stream);

            Assert.Equal(sharedSecret, packet.SharedSecret);
            Assert.Equal(verifyToken, packet.VerifyToken);
        }
    }
}
