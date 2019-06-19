using Obsidian.Entities;
using Obsidian.Util;
using System;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets
{
    class NamedSoundEffect : Packet
    {
        public NamedSoundEffect(string name, Position location, SoundCategory category, float pitch, float volume) : base(0x1A)
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

        public string Name { get; }

        public SoundCategory Category { get; }

        public Position Location { get; }

        public float Volume { get; }

        public float Pitch { get; }

        public override Task PopulateAsync() => throw new NotImplementedException();

        public override async Task<byte[]> ToArrayAsync()
        {
            using (var stream = new MinecraftStream())
            {
                await stream.WriteStringAsync(this.Name);
                await stream.WriteVarIntAsync(this.Category);
                await stream.WriteIntAsync((int)this.Location.X * 8);
                await stream.WriteIntAsync((int)this.Location.Y * 8);
                await stream.WriteIntAsync((int)this.Location.Z * 8);
                await stream.WriteFloatAsync(this.Volume);
                await stream.WriteFloatAsync(this.Pitch);
                return stream.ToArray();
            }
        }
    }
}
