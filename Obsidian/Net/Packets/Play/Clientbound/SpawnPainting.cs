using Obsidian.API;
using Obsidian.Entities;
using Obsidian.Serialization.Attributes;
using Obsidian.Util.Registry.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Play.Clientbound
{
    public partial class SpawnPainting : IPacket
    {

        [Field(0), VarLength]
        public int EntityId;

        [Field(1)]
        public Guid UUID;

        [Field(2), VarLength]
        public int Motive;

        [Field(3)]
        public Position Position;

        [Field(4), ActualType(typeof(byte))]
        public PaintingDirection Direction;

        public int Id => 0x03;

        public SpawnPainting()
        {
            
        }

        public SpawnPainting(int entityId, Guid uuid, int motive, Position pos, PaintingDirection direction)
        {
            EntityId = entityId;
            UUID = uuid;
            Motive = motive;
            Position = pos;
            Direction = direction;
        }


        public Task WriteAsync(MinecraftStream stream) => Task.CompletedTask;

        public Task ReadAsync(MinecraftStream stream) => Task.CompletedTask;

        public Task HandleAsync(Server server, Player player) => Task.CompletedTask;

    }
}
