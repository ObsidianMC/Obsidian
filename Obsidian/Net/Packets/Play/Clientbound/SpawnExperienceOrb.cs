using Obsidian.API;
using Obsidian.Entities;
using Obsidian.Serialization.Attributes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Play.Clientbound
{
    public partial class SpawnExperienceOrb : IPacket
    {
        [Field(0), VarLength]
        public int EntityId;

        [Field(1), Absolute]
        public PositionF Position;

        [Field(2)]
        public short Count;

        public int Id => 0x01;

        public Task WriteAsync(MinecraftStream stream) => Task.CompletedTask;

        public Task ReadAsync(MinecraftStream stream) => Task.CompletedTask;

        public Task HandleAsync(Server server, Player player) => Task.CompletedTask;
    }
}
