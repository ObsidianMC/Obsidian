using Obsidian.Serializer.Attributes;
using Obsidian.Sounds;
using Obsidian.Util.DataTypes;

namespace Obsidian.Net.Packets.Play
{
    public class SoundEffect : Packet
    {
        [PacketOrder(0)]
        public int SoundId { get; set; }

        [PacketOrder(1)]
        public SoundCategory Category { get; set; }

        [PacketOrder(2)]
        public SoundPosition Position { get; set; }

        [PacketOrder(3)]
        public float Volume { get; set; }

        [PacketOrder(4)]
        public float Pitch { get; set; }

        public SoundEffect(int soundId, SoundPosition position, SoundCategory category = SoundCategory.Master, float pitch = 1.0f, float volume = 1f) : base(0x4D)
        {
            this.SoundId = soundId;
            this.Position = position;
            this.Category = category;
            this.Pitch = pitch;
            this.Volume = volume;
        }
    }
}