using Obsidian.Nbt;

namespace Obsidian.Utilities.Registry.Codecs.Dimensions
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

            var compound = new NbtCompound
            {
                new NbtTag<int>("id", this.Id),

                new NbtTag<string>("name", this.Name),

               element
            };

            list.Add(compound);
        }

        public void TransferTags(NbtWriter writer)
        {
            writer.WriteBool("piglin_safe", this.Element.PiglinSafe);
            writer.WriteBool("natural", this.Element.Natural);

            writer.WriteFloat("ambient_light", this.Element.AmbientLight);

            if (this.Element.FixedTime.HasValue)
                writer.WriteLong("fixed_time", this.Element.FixedTime.Value);

            writer.WriteString("infiniburn", this.Element.Infiniburn);

            writer.WriteBool("respawn_anchor_works", this.Element.RespawnAnchorWorks);
            writer.WriteBool("has_skylight", this.Element.HasSkylight);
            writer.WriteBool("bed_works", this.Element.BedWorks);

            writer.WriteString("effects", this.Element.Effects);

            writer.WriteBool("has_raids", this.Element.HasRaids);

            writer.WriteInt("min_y", this.Element.MinY);

            writer.WriteInt("height", this.Element.Height);

            writer.WriteInt("logical_height", this.Element.LogicalHeight);

            writer.WriteFloat("coordinate_scale", this.Element.CoordinateScale);

            writer.WriteBool("ultrawarm", this.Element.Ultrawarm);
            writer.WriteBool("has_ceiling", this.Element.HasCeiling);
        }

        public NbtCompound ToNbt()
        {
            var compound = new NbtCompound
            {
                new NbtTag<bool>("piglin_safe", this.Element.PiglinSafe),

                new NbtTag<bool>("natural", this.Element.Natural),

                new NbtTag<float>("ambient_light", this.Element.AmbientLight),

                new NbtTag<string>("infiniburn", this.Element.Infiniburn),

                new NbtTag<bool>("respawn_anchor_works", this.Element.RespawnAnchorWorks),
                new NbtTag<bool>("has_skylight", this.Element.HasSkylight),
                new NbtTag<bool>("bed_works", this.Element.BedWorks),

                new NbtTag<string>("effects", this.Element.Effects),

                new NbtTag<bool>("has_raids", this.Element.HasRaids),

                new NbtTag<int>("min_y", this.Element.MinY),

                new NbtTag<int>("height", this.Element.Height),

                new NbtTag<int>("logical_height", this.Element.LogicalHeight),

                new NbtTag<float>("coordinate_scale", this.Element.CoordinateScale),

                new NbtTag<bool>("ultrawarm", this.Element.Ultrawarm),
                new NbtTag<bool>("has_ceiling", this.Element.HasCeiling)
            };

            if (this.Element.FixedTime.HasValue)
                compound.Add(new NbtTag<long>("fixed_time", this.Element.FixedTime.Value));

            return compound;
        }
    }
}
