using Obsidian.Net;
using Obsidian.Net.Packets.Play;
using Obsidian.Serializer;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace Obsidian.Tests
{
    public class Deserialization
    {
        [Fact]
        public void Handshake()
        {
            throw new NotImplementedException();
        }

        [Fact]
        public void LoginStart()
        {
            throw new NotImplementedException();
        }

        [Fact]
        public void EncryptionResponse()
        {
            throw new NotImplementedException();
        }

        [Fact]
        public async Task IncomingChatMessage()
        {
            var message = "My chat message";

            using var stream = new MinecraftStream();
            await stream.WriteStringAsync(message);
            stream.Position = 0;

            var packet = PacketSerializer.FastDeserialize<IncomingChatMessage>(stream);

            Assert.Equal(message, packet.Message);
        }

        [Fact]
        public void ClientSettings()
        {
            throw new NotImplementedException();
        }

        [Fact]
        public void KeepAlive()
        {
            throw new NotImplementedException();
        }

        [Fact]
        public void PlayerPosition()
        {
            throw new NotImplementedException();
        }

        [Fact]
        public void PlayerPositionLook()
        {
            throw new NotImplementedException();
        }

        [Fact]
        public void PlayerLook()
        {
            throw new NotImplementedException();
        }

        [Fact]
        public void PlayerDigging()
        {
            throw new NotImplementedException();
        }

        [Fact]
        public void AnimationServerPacket()
        {
            throw new NotImplementedException();
        }

        [Fact]
        public void PlayerBlockPlacement()
        {
            throw new NotImplementedException();
        }
    }
}
