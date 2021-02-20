using Obsidian.API;
using Obsidian.Entities;
using Obsidian.Serialization.Attributes;
using System;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Play.Clientbound
{
    [ClientOnly]
    public partial class NamedSoundEffect : IPacket
    {
        [Field(0)]
        public string Name { get; }

        [Field(1), ActualType(typeof(int)), VarLength]
        public SoundCategory Category { get; }

        [Field(2)]
        public SoundPosition Position { get; }

        [Field(3)]
        public float Volume { get; }

        [Field(4)]
        public float Pitch { get; }

        public int Id => 0x18;

        public NamedSoundEffect(string name, SoundPosition location, SoundCategory category, float pitch, float volume)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("message", nameof(name));
            }

            Name = name;
            Category = category;
            Position = location;
            Volume = volume;
            Pitch = pitch;
        }

        public Task WriteAsync(MinecraftStream stream) => Task.CompletedTask;

        public Task ReadAsync(MinecraftStream stream) => Task.CompletedTask;

        public Task HandleAsync(Server server, Player player) => Task.CompletedTask;
    }
}