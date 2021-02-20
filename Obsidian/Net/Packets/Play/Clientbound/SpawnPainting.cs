using Obsidian.API;
using Obsidian.Entities;
using Obsidian.Serialization.Attributes;
using System;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Play.Clientbound
{
    public partial class SpawnPainting : IPacket
    {
        [Field(0), VarLength]
        public int EntityId { get; private set; }

        [Field(1)]
        public Guid UUID { get; private set; }

        [Field(2), VarLength]
        public int Motive { get; private set; }

        [Field(3)]
        public Position Position { get; private set; }

        [Field(4), ActualType(typeof(byte))]
        public PaintingDirection Direction { get; private set; }

        public int Id => 0x03;

        public SpawnPainting()
        {
        }

        public SpawnPainting(Guid uuid, int motive, Position pos, PaintingDirection direction)
        {
            // Source: https://minecraft.gamepedia.com/Java_Edition_data_values/Pre-flattening/Entity_IDs
            EntityId = 9;
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
