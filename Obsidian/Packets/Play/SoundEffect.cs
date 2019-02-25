using Obsidian.Entities;
using System;
using System.IO;
using System.Threading.Tasks;

namespace Obsidian.Packets
{
    public class SoundEffect : Packet
    {
        public SoundEffect(int soundId, Position position, SoundCategory category = SoundCategory.Master, float pitch = 1.0f, float volume = 1f) : base(0x4D, new byte[0])
        {
            this.SoundId = soundId;
            this.Position = position;
            this.Category = category;
            this.Pitch = pitch;
            this.Volume = volume;
        }

        public SoundCategory Category { get; set; }
        public float Pitch { get; set; }
        public Position Position { get; set; }
        public int SoundId { get; set; }
        public float Volume { get; set; }

        public override Task Populate()
        {
            throw new NotImplementedException();
        }

        public override async Task<byte[]> ToArrayAsync()
        {
            using (var stream = new MemoryStream())
            {
                await stream.WriteVarIntAsync(this.SoundId);
                await stream.WriteVarIntAsync((int)this.Category);
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