using Obsidian.Entities;
using System;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets
{
    class NamedSoundEffect : Packet
    {
        public NamedSoundEffect(string name, Location location, SoundCategory category, float volume, float pitch) : base(0x1A)
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

        public Location Location { get; }

        public float Volume { get; }

        public float Pitch { get; }

        protected override Task PopulateAsync() => throw new NotImplementedException();

        public override async Task<byte[]> ToArrayAsync()
        {
            using (var stream = new MinecraftStream())
            {
                await stream.WriteStringAsync(this.Name);
                await stream.WriteVarIntAsync(this.Category);
                await stream.WriteIntAsync((int)this.Location.X);
                await stream.WriteIntAsync((int)this.Location.Y);
                await stream.WriteIntAsync((int)this.Location.Z);
                await stream.WriteFloatAsync(this.Volume);
                await stream.WriteFloatAsync(this.Pitch);
                return stream.ToArray();
            }
        }
    }
}
