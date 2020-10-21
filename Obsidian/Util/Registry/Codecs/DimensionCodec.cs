using Obsidian.Nbt.Tags;

namespace Obsidian.Util.Registry.Codecs
{
    public class DimensionCodec
    {
        public string Name { get; set; }

        public int Id { get; set; }

        /// <summary>
        /// Whether piglins shake and transform to zombified piglins.
        /// </summary>
        public bool PiglinSafe { get; set; }

        /// <summary>
        /// When false, compasses spin randomly. When true, nether portals can spawn zombified piglins.
        /// </summary>
        public bool Natural { get; set; }

        /// <summary>
        /// How much light the dimension has. 0.0 to 1.0
        /// </summary>
        public float AmbientLight { get; set; } = 0.0f;

        /// <summary>
        /// If this is set to a number, the time of the day is the specified value. 
        /// false, or 0 to 24000
        /// </summary>
        public long? FixedTime { get; set; }

        /// <summary>
        /// A resource location defining what block tag to use for infiniburn.
        /// </summary>
        public string Infiniburn { get; set; }

        /// <summary>
        /// Whether players can charge and use respawn anchors.
        /// </summary>
        public bool RespawnAnchorWorks { get; set; }

        /// <summary>
        /// Whether the dimension has skylight access or not.
        /// </summary>
        public bool HasSkylight { get; set; }

        /// <summary>
        /// Whether players can use a bed to sleep.
        /// </summary>
        public bool BedWorks { get; set; }

        /// <summary>
        /// ?
        /// </summary>
        public string Effects { get; set; }

        /// <summary>
        /// Whether players with the Bad Omen effect can cause a raid.
        /// </summary>
        public bool HasRaids { get; set; }

        /// <summary>
        /// The maximum height to which chorus fruits and nether portals can bring players within this dimension.
        /// </summary>
        public int LogicalHeight { get; set; }

        /// <summary>
        /// The multiplier applied to coordinates when traveling to the dimension.
        /// </summary>
        public float CoordinateScale { get; set; }

        /// <summary>
        /// Whether the dimensions behaves like the nether (water evaporates and sponges dry) or not.
        /// Also causes lava to spread thinner.
        /// </summary>
        public bool Ultrawarm { get; set; }

        /// <summary>
        /// Whether the dimension has a bedrock ceiling or not. When true, causes lava to spread faster.
        /// </summary>
        public bool HasCeiling { get; set; }

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
                new NbtByte("piglin_safe", (byte)(this.PiglinSafe ? 1 : 0)),
                new NbtByte("natural", (byte)(this.Natural ? 1 : 0)),

                new NbtFloat("ambient_light", this.AmbientLight),

                new NbtString("infiniburn", this.Infiniburn),

                new NbtByte("respawn_anchor_works", (byte)(this.RespawnAnchorWorks ? 1 : 0)),
                new NbtByte("has_skylight", (byte)(this.HasSkylight ? 1 : 0)),
                new NbtByte("bed_works", (byte)(this.BedWorks ? 1 : 0)),

                new NbtString("effects", this.Effects),

                new NbtByte("has_raids", (byte)(this.HasRaids ? 1 : 0)),

                new NbtInt("logical_height", this.LogicalHeight),

                new NbtFloat("coordinate_scale", this.CoordinateScale),

                new NbtByte("ultrawarm", (byte)(this.Ultrawarm ? 1 : 0)),
                new NbtByte("has_ceiling", (byte)(this.HasCeiling ? 1 : 0))
            };

            if (this.FixedTime.HasValue)
                compound.Add(new NbtFloat("fixed_time", this.FixedTime.Value));

            return compound;
        }

        /*public void Write(NbtWriter writer, bool isAlone = false)
        {
            if (isAlone)
                writer.BeginCompound("");

            writer.WriteString("name", this.Name.ToLower().ToSnakeCase());

            writer.WriteByte("piglin_safe", (byte)(this.PiglinSafe ? 1 : 0));
            writer.WriteByte("natural", (byte)(this.Natural ? 1 : 0));

            writer.WriteFloat("ambient_light", this.AmbientLight);

            if (this.FixedTime.HasValue)
                writer.WriteLong("fixed_time", this.FixedTime.Value);

            writer.WriteString("infiniburn", this.Infiniburn);

            writer.WriteByte("respawn_anchor_works", (byte)(this.RespawnAnchorWorks ? 1 : 0));
            writer.WriteByte("has_skylight", (byte)(this.HasSkylight ? 1 : 0));
            writer.WriteByte("bed_works", (byte)(this.BedWorks ? 1 : 0));

            writer.WriteString("effects", this.Effects);

            writer.WriteByte("has_raids", (byte)(this.HasRaids ? 1 : 0));

            writer.WriteInt("logical_height", this.LogicalHeight);

            writer.WriteFloat("coordinate_scale", this.CoordinateScale);

            writer.WriteByte("ultrawarm", (byte)(this.Ultrawarm ? 1 : 0));
            writer.WriteByte("has_ceiling", (byte)(this.HasCeiling ? 1 : 0));

            if (isAlone)
                writer.EndCompound();
        }*/
    }
}
