using Obsidian.Entities;
using Obsidian.Serializer.Attributes;
using Obsidian.Serializer.Enums;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Play.Client
{
    public partial class DestroyEntities : IPacket
    {
        [Field(0, Type = DataType.VarInt)]
        public int Count { get; set; }

        [Field(1, Type = DataType.Array)]
        public List<int> EntityIds { get; set; } = new List<int>();

        public int Id => 0x36;

        public DestroyEntities() { }

        public void AddEntity(int entity) => this.EntityIds.Add(entity);

        public void AddEntityRange(params int[] entities) => this.EntityIds.AddRange(entities);

        public Task WriteAsync(MinecraftStream stream) => Task.CompletedTask;

        public Task ReadAsync(MinecraftStream stream) => Task.CompletedTask;

        public Task HandleAsync(Obsidian.Server server, Player player) => Task.CompletedTask;
    }
}
