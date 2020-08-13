using Obsidian.Serializer.Attributes;
using Obsidian.Serializer.Enums;
using Obsidian.Sounds;
using Obsidian.Util.DataTypes;
using System;

namespace Obsidian.Net.Packets.Play
{
    internal class NamedSoundEffect : Packet
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

        public NamedSoundEffect(string name, SoundPosition location, SoundCategory category, float pitch, float volume) : base(0x1A)
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
    }
}