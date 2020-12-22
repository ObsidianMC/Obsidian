using Obsidian.API;
using Obsidian.Net;
using System.Threading.Tasks;

namespace Obsidian.Entities
{
    public class Living : Entity, ILiving
    {
        public LivingBitMask LivingBitMask { get; set; }

        public float Health { get; set; }

        public uint ActiveEffectColor { get; private set; }

        public bool AmbientPotionEffect { get; set; }

        public int AbsorbedArrows { get; set; }

        public int AbsorbtionAmount { get; set; }

        public PositionF BedBlockPosition { get; set; }

        public override async Task WriteAsync(MinecraftStream stream)
        {
            await base.WriteAsync(stream);

            await stream.WriteEntityMetdata(7, EntityMetadataType.Byte, (byte)this.LivingBitMask);

            await stream.WriteEntityMetdata(8, EntityMetadataType.Float, this.Health);

            await stream.WriteEntityMetdata(9, EntityMetadataType.VarInt, (int)this.ActiveEffectColor);

            await stream.WriteEntityMetdata(10, EntityMetadataType.Boolean, this.AmbientPotionEffect);

            await stream.WriteEntityMetdata(11, EntityMetadataType.VarInt, this.AbsorbedArrows);

            await stream.WriteEntityMetdata(12, EntityMetadataType.VarInt, this.AbsorbtionAmount);

            await stream.WriteEntityMetdata(13, EntityMetadataType.OptPosition, this.BedBlockPosition, this.BedBlockPosition != default);
        }

        public override void Write(MinecraftStream stream)
        {
            base.Write(stream);

            stream.WriteEntityMetadataType(7, EntityMetadataType.Byte);
            stream.WriteByte((byte)LivingBitMask);

            stream.WriteEntityMetadataType(8, EntityMetadataType.Float);
            stream.WriteFloat(Health);

            stream.WriteEntityMetadataType(9, EntityMetadataType.VarInt);
            stream.WriteVarInt((int)ActiveEffectColor);

            stream.WriteEntityMetadataType(10, EntityMetadataType.Boolean);
            stream.WriteBoolean(AmbientPotionEffect);

            stream.WriteEntityMetadataType(11, EntityMetadataType.VarInt);
            stream.WriteVarInt(AbsorbedArrows);

            stream.WriteEntityMetadataType(12, EntityMetadataType.VarInt);
            stream.WriteVarInt(AbsorbtionAmount);

            stream.WriteEntityMetadataType(13, EntityMetadataType.OptPosition);
            stream.WriteBoolean(BedBlockPosition != default);
            if (BedBlockPosition != default)
                stream.WritePositionF(BedBlockPosition);
        }
    }
}
