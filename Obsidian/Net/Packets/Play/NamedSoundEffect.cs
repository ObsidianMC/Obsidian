using Obsidian.Serializer.Attributes;
using Obsidian.Sounds;
using Obsidian.Util.DataTypes;
using System;

namespace Obsidian.Net.Packets.Play
{
    internal class NamedSoundEffect : Packet
    {
        [PacketOrder(0)]
        public string Name { get; }

        [PacketOrder(1)]
        public SoundCategory Category { get; }

        [PacketOrder(2)]
        public SoundPosition Location { get; }

        [PacketOrder(3)]
        public float Volume { get; }

        [PacketOrder(4)]
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