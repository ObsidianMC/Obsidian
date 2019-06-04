using Obsidian.Entities;
using System;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets
{
    public class SoundEffect : Packet
    {
        public SoundEffect(int soundId, Location location, SoundCategory category = SoundCategory.Master, float pitch = 1.0f, float volume = 1f) : base(0x4D, new byte[0])
        {
            this.SoundId = soundId;
            this.Location = location;
            this.Category = category;
            this.Pitch = pitch;
            this.Volume = volume;
        }

        public SoundCategory Category { get; set; }
        public float Pitch { get; set; }
        public Location Location { get; set; }
        public int SoundId { get; set; }
        public float Volume { get; set; }

        protected override Task PopulateAsync()
        {
            throw new NotImplementedException();
        }

        public override async Task<byte[]> ToArrayAsync()
        {
            using (var stream = new MinecraftStream())
            {
                await stream.WriteVarIntAsync(this.SoundId);
                await stream.WriteVarIntAsync((int)this.Category);
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