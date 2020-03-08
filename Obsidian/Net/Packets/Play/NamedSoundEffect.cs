using System;
using System.Threading.Tasks;
using Obsidian.Sounds;
using Obsidian.Util.DataTypes;

namespace Obsidian.Net.Packets.Play
{
    internal class NamedSoundEffect : Packet
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

        protected override Task PopulateAsync(MinecraftStream stream) => throw new NotImplementedException();

        protected override async Task ComposeAsync(MinecraftStream stream)
        {
            await stream.WriteStringAsync(this.Name);
            await stream.WriteVarIntAsync(this.Category);
            await stream.WriteIntAsync((int)(this.Location.X / 32.0D));
            await stream.WriteIntAsync((int)(this.Location.Y / 32.0D));
            await stream.WriteIntAsync((int)(this.Location.Z / 32.0D));
            await stream.WriteFloatAsync(this.Volume);
            await stream.WriteFloatAsync(this.Pitch);
        }
    }
}