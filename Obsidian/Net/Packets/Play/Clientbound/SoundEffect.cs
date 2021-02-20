using Obsidian.API;
using Obsidian.Entities;
using Obsidian.Serialization.Attributes;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Play.Clientbound
{
    [ClientOnly]
    public partial class SoundEffect : ISerializablePacket
    {
        [Field(0), ActualType(typeof(int)), VarLength]
        public Sounds SoundId { get; }

        [Field(1), ActualType(typeof(int)), VarLength]
        public SoundCategory Category { get; }

        [Field(2)]
        public SoundPosition Position { get; }

        [Field(3)]
        public float Volume { get; }

        [Field(4)]
        public float Pitch { get; }

        public int Id => 0x51;

        public SoundEffect(Sounds soundId, SoundPosition position, SoundCategory category = SoundCategory.Master, float pitch = 1f, float volume = 1f)
        {
            SoundId = soundId;
            Position = position;
            Category = category;
            Pitch = pitch;
            Volume = volume;
        }

        public Task WriteAsync(MinecraftStream stream) => Task.CompletedTask;

        public Task ReadAsync(MinecraftStream stream) => Task.CompletedTask;

        public Task HandleAsync(Server server, Player player) => Task.CompletedTask;
    }
}