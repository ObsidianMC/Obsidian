using Obsidian.Entities;
using Obsidian.Serialization.Attributes;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Play.Clientbound
{
    [ClientOnly]
    public partial class DestroyEntities : ISerializablePacket
    {
        [Field(0), VarLength]
        public List<int> EntityIds { get; set; } = new();

        public int Id => 0x36;

        public void AddEntity(int entity) => EntityIds.Add(entity);

        public void AddEntityRange(params int[] entities) => EntityIds.AddRange(entities);

        public Task ReadAsync(MinecraftStream stream) => Task.CompletedTask;

        public Task HandleAsync(Server server, Player player) => Task.CompletedTask;
    }
}
