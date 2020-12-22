using Obsidian.API;
using Obsidian.Entities;
using Obsidian.Serialization.Attributes;
using System;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Play.Client
{
    public partial class NamedSoundEffect : IPacket
    {
        [Field(0)]
        public string Name { get; private set; }

        [Field(1), ActualType(typeof(int)), VarLength]
        public SoundCategory Category { get; private set; }

        [Field(2)]
        public SoundPosition Position { get; private set; }

        [Field(3)]
        public float Volume { get; private set; }

        [Field(4)]
        public float Pitch { get; private set; }

        public int Id => 0x18;

        private NamedSoundEffect()
        {
        }

        public NamedSoundEffect(string name, SoundPosition location, SoundCategory category, float pitch, float volume)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("message", nameof(name));
            }

            this.Name = name;
            this.Category = category;
            this.Position = location;
            this.Volume = volume;
            this.Pitch = pitch;
        }

        public Task WriteAsync(MinecraftStream stream) => Task.CompletedTask;

        public Task ReadAsync(MinecraftStream stream) => Task.CompletedTask;

        public Task HandleAsync(Obsidian.Server server, Player player) => Task.CompletedTask;
    }
}