using Obsidian.Net;
using Obsidian.Net.Packets;
using Obsidian.Net.Packets.Handshaking;
using Obsidian.Net.Packets.Login;
using Obsidian.Net.Packets.Play;
using Obsidian.Serializer;
using Obsidian.Util;
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

            var packet = PacketSerializer.FastDeserialize<PlayerLook>(stream);

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

            var packet = PacketSerializer.FastDeserialize<AnimationServerPacket>(stream);

            Assert.Equal(hand, packet.Hand);
        }

        //[Fact]
        //public void PlayerDigging()
        //{
        //    throw new NotImplementedException();
        //}

        //[Fact]
        //public void PlayerBlockPlacement()
        //{
        //    throw new NotImplementedException();
        //}

        //[Fact]
        //public void PlayerPositionLook()
        //{
        //    throw new NotImplementedException();
        //}

        //[Fact]
        //public void PlayerPosition()
        //{
        //    throw new NotImplementedException();
        //}

        //[Fact]
        //public void EncryptionResponse()
        //{
        //    throw new NotImplementedException();
        //}
    }
}
