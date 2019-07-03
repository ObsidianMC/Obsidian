using Obsidian.Entities;
using Obsidian.Util;

namespace Obsidian.Net.Packets
{
    public class SoundEffect : Packet
    {
        public SoundEffect(int soundId, Position location, SoundCategory category = SoundCategory.Master, float pitch = 1.0f, float volume = 1f) : base(0x4D, new byte[0])
        {
            this.SoundId = soundId;
            this.X = (int)location.X;
            this.Y = (int)location.Y;
            this.Z = (int)location.Z;
            this.Category = category;
            this.Pitch = pitch;
            this.Volume = volume;
        }

        [Variable]
        public int SoundId { get; set; }

        [Variable]
        public SoundCategory Category { get; set; }

        [Variable]
        public int X { get; set; }

        [Variable]
        public int Y { get; set; }

        [Variable]
        public int Z { get; set; }

        [Variable]
        public float Volume { get; set; }

        [Variable]
        public float Pitch { get; set; }


        public Position Location => new Position
        {
            X = this.X,

            Y = this.Y,

            Z = this.Z
        };
    }
}