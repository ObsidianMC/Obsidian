using Obsidian.Entities;
using Obsidian.Util;
using Obsidian.Util.Extensions;
using System;
using System.IO;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets
{
    /*public class Packet
    {
        internal byte[] data;

        internal int id;

        public Packet(int packetid) => this.id = packetid;

        public Packet(int packetId, byte[] data)
        {
            this.id = packetId;
            this.data = data;
        }


        protected virtual Task ComposeAsync(MinecraftStream stream) => Task.CompletedTask;

        protected virtual Task HandleAsync(Server server, Player player) => Task.CompletedTask;
    }*/

    public interface IPacket
    {
        int Id { get; }

        Task WriteAsync(MinecraftStream stream);

        Task ReadAsync(MinecraftStream stream);

        Task HandleAsync(Server server, Player player);
    }
}