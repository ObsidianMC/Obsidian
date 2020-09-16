using Obsidian.Net;
using System;
using System.Threading.Tasks;

namespace Obsidian.Entities
{
    public class Living : Entity
    {
        public LivingBitMask LivingBitMask { get; set; }

        public float Health { get; set; }

        public uint ActiveEffectColor { get; private set; }

        public bool AmbientPotionEffect { get; set; }

        public int Arrows { get; set; }

        public override async Task WriteAsync(MinecraftStream stream)
        {
            await stream.WriteEntityMetdata(6, EntityMetadataType.Byte, (byte)LivingBitMask);

            await stream.WriteEntityMetdata(7, EntityMetadataType.Float, Health);

            await stream.WriteEntityMetdata(8, EntityMetadataType.VarInt, (int)ActiveEffectColor);

            await stream.WriteEntityMetdata(9, EntityMetadataType.Boolean, AmbientPotionEffect);

            await stream.WriteEntityMetdata(10, EntityMetadataType.VarInt, Arrows);
        }

    }

    public enum HandState
    {
    }

    [Flags]
    public enum LivingBitMask : byte
    {
        None = 0x00,

        HandActive = 0x01,
        ActiveHand = 0x02,
        InRiptideSpinAttack = 0x04
    }
}
