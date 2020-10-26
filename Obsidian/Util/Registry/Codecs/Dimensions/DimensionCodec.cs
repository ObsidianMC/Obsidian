using Obsidian.Nbt.Tags;

namespace Obsidian.Util.Registry.Codecs.Dimensions
{
    public class DimensionCodec
    {
        public string Name { get; set; }

        public int Id { get; set; }

        public DimensionElement Element { get; set; }

        public void Write(NbtList list)
        {
            var element = this.ToNbt();

            element.Name = "element";

            var compound = new NbtCompound()
            {
                new NbtInt("id", this.Id),

                new NbtString("name", this.Name),

               element
            };

            list.Add(compound);
        }

        public NbtCompound ToNbt()
        {
            var compound = new NbtCompound("")
            {
                new NbtByte("piglin_safe", (byte)(this.Element.PiglinSafe ? 1 : 0)),
                new NbtByte("natural", (byte)(this.Element.Natural ? 1 : 0)),

                new NbtFloat("ambient_light", this.Element.AmbientLight),

                new NbtString("infiniburn", this.Element.Infiniburn),

                new NbtByte("respawn_anchor_works", (byte)(this.Element.RespawnAnchorWorks ? 1 : 0)),
                new NbtByte("has_skylight", (byte)(this.Element.HasSkylight ? 1 : 0)),
                new NbtByte("bed_works", (byte)(this.Element.BedWorks ? 1 : 0)),

                new NbtString("effects", this.Element.Effects),

                new NbtByte("has_raids", (byte)(this.Element.HasRaids ? 1 : 0)),

                new NbtInt("logical_height", this.Element.LogicalHeight),

                new NbtFloat("coordinate_scale", this.Element.CoordinateScale),

                new NbtByte("ultrawarm", (byte)(this.Element.Ultrawarm ? 1 : 0)),
                new NbtByte("has_ceiling", (byte)(this.Element.HasCeiling ? 1 : 0))
            };

            if (this.Element.FixedTime.HasValue)
                compound.Add(new NbtFloat("fixed_time", this.Element.FixedTime.Value));

            return compound;
        }
    }
}
