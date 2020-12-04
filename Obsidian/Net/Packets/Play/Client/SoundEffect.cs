using Obsidian.API;
using Obsidian.Entities;
using Obsidian.Serializer.Attributes;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Play.Client
{
    public partial class SoundEffect : IPacket
    {
        [Field(0), ActualType(typeof(int)), VarLength]
        public Sounds SoundId { get; set; }

        [Field(1), ActualType(typeof(int)), VarLength]
        public SoundCategory Category { get; set; }

        [Field(2)]
        public SoundPosition Position { get; set; }

        [Field(3)]
        public float Volume { get; set; }

        [Field(4)]
        public float Pitch { get; set; }

        public int Id => 0x51;

        private SoundEffect()
        {
        }

        public SoundEffect(Sounds soundId, SoundPosition position, SoundCategory category = SoundCategory.Master, float pitch = 1.0f, float volume = 1f)
        {
            this.SoundId = soundId;
            this.Position = position;
            this.Category = category;
            this.Pitch = pitch;
            this.Volume = volume;
        }

        public Task WriteAsync(MinecraftStream stream) => Task.CompletedTask;

        public Task ReadAsync(MinecraftStream stream) => Task.CompletedTask;

        public Task HandleAsync(Obsidian.Server server, Player player) => Task.CompletedTask;
    }
}