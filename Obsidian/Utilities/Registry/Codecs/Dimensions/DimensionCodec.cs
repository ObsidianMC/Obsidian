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

            var compound = new NbtCompound()
            {
                new NbtTag<int>("id", this.Id),

                new NbtTag<string>("name", this.Name),

               element
            };

            list.Add(compound);
        }

        public NbtCompound ToNbt()
        {
            var compound = new NbtCompound(this.Name)
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
