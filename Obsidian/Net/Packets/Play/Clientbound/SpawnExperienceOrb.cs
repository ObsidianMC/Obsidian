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
        public int EntityId { get; private set; }

        [Field(1), Absolute]
        public PositionF Position { get; private set; }

        [Field(2)]
        public short Count { get; private set; }

        public int Id => 0x01;


        public SpawnExperienceOrb(short count, PositionF position)
        {
            Count = count;
            Position = position;
            // Source: https://minecraft.gamepedia.com/Java_Edition_data_values/Pre-flattening/Entity_IDs
            EntityId = 2;
        }

        public Task WriteAsync(MinecraftStream stream) => Task.CompletedTask;

        public Task ReadAsync(MinecraftStream stream) => Task.CompletedTask;

        public Task HandleAsync(Server server, Player player) => Task.CompletedTask;
    }
}
