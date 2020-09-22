using Obsidian.Serializer.Attributes;
using Obsidian.Serializer.Enums;
using Obsidian.Util.DataTypes;

namespace Obsidian.Net.Packets.Play.Client
{
    public class BlockBreakAnimation : Packet
    {
        [Field(0, Type = DataType.VarInt)]
        public int EntityId { get; set; }

        [Field(1)]
        public Position Location { get; set; }

        /// <summary>
        /// 0-9 to set it, any other value to remove it
        /// </summary>
        [Field(2)]
        public sbyte DestroyStage { get; set; }

        public BlockBreakAnimation() : base(0x09) { }
    }
}
