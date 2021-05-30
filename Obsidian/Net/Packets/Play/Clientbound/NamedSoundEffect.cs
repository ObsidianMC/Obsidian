using Obsidian.API;
using Obsidian.Serialization.Attributes;
using System;

namespace Obsidian.Net.Packets.Play.Clientbound
{
    [ClientOnly]
    public partial class NamedSoundEffect : IClientboundPacket
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
    }
}