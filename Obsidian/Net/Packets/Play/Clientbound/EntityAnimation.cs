using Obsidian.Entities;
using Obsidian.Serialization.Attributes;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Play.Clientbound
{
    [ClientOnly]
    public partial class EntityAnimation : ISerializablePacket
    {
        [Field(0), VarLength]
        public int EntityId { get; set; }

        [Field(1), ActualType(typeof(byte))]
        public EAnimation Animation { get; set; }

        public int Id => 0x05;

        public Task ReadAsync(MinecraftStream stream) => Task.CompletedTask;

        public Task HandleAsync(Server server, Player player) => Task.CompletedTask;
    }

    public enum EAnimation : byte
    {
        SwingMainArm,
        TakeDamage,
        LeaveBed,
        SwingOffhand,
        CriticalEffect,
        MagicalCriticalEffect
    }
}
