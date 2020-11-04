using Obsidian.API;
using Obsidian.Entities;
using Obsidian.Serializer.Attributes;
using Obsidian.Serializer.Enums;
using System;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Play.Client
{
    internal class NamedSoundEffect : IPacket
    {
        [Field(0)]
        public string Name { get; }

        [Field(1, Type = DataType.VarInt)]
        public SoundCategory Category { get; }

        [Field(2)]
        public SoundPosition Location { get; }

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

            this.Name = name;
            this.Category = category;
            this.Location = location;
            this.Volume = volume;
            this.Pitch = pitch;
        }

        public Task WriteAsync(MinecraftStream stream) => Task.CompletedTask;

        public Task ReadAsync(MinecraftStream stream) => Task.CompletedTask;

        public Task HandleAsync(Obsidian.Server server, Player player) => Task.CompletedTask;
    }
}