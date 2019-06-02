using Obsidian.Entities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace Obsidian.Packets.Play
{
    class NamedSoundEffect : Packet
    {
        public NamedSoundEffect(string name, SoundCategory category, Position position, float volume, float pitch) : base(0x1A)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("message", nameof(name));
            }

            this.Name = name;
            this.Category = category;
            this.Position = position;
            this.Volume = volume;
            this.Pitch = pitch;
        }

        public string Name { get; }

        public SoundCategory Category { get; }

        public Position Position { get; }

        public float Volume { get; }

        public float Pitch { get; }

        protected override Task PopulateAsync() => throw new NotImplementedException();

        public override async Task<byte[]> ToArrayAsync()
        {
            using (var stream = new MemoryStream())
            {
                await stream.WriteStringAsync(this.Name);
                await stream.WriteVarIntAsync(this.Category);
                await stream.WriteIntAsync(this.Position.X);
                await stream.WriteIntAsync(this.Position.Y);
                await stream.WriteIntAsync(this.Position.Z);
                await stream.WriteFloatAsync(this.Volume);
                await stream.WriteFloatAsync(this.Pitch);
                return stream.ToArray();
            }
        }
    }
}
