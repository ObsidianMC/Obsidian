using Obsidian.API;
using Obsidian.Entities;
using Obsidian.Serializer.Attributes;
using Obsidian.Util.Registry.Codecs;
using Obsidian.Util.Registry.Codecs.Biomes;
using Obsidian.Util.Registry.Codecs.Dimensions;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Play.Client
{
    public class MixedCodec
    {
        public CodecCollection<int, DimensionCodec> Dimensions { get; set; }

        public CodecCollection<string, BiomeCodec> Biomes { get; set; }
    }

    public partial class JoinGame : IPacket
    {
        [Field(0)]
        public int EntityId { get; set; }

        [Field(1)]
        public bool Hardcore { get; set; } = false;

        [Field(2), ActualType(typeof(byte))]
        public Gamemode Gamemode { get; set; } = Gamemode.Survival;

        [Field(3)]
        public sbyte PreviousGamemode { get; set; } = 0;

        [Field(4), VarLength]
        public int WorldCount { get; set; }

        [Field(5)]
        public List<string> WorldNames { get; set; }

        [Field(6)]
        public MixedCodec Codecs { get; set; }

        [Field(7)]
        public DimensionCodec Dimension { get; set; }

        [Field(8)]
        public string DimensionName { get; set; }

        [Field(9)]
        public long HashedSeed { get; set; }

        [Field(10), VarLength]
        private int MaxPlayers { get; } = 0;

        [Field(11), VarLength]
        public int ViewDistance { get; set; } = 8;

        [Field(12)]
        public bool ReducedDebugInfo { get; set; } = false;

        [Field(13)]
        public bool EnableRespawnScreen { get; set; } = true;

        [Field(14)]
        public bool Debug { get; set; } = false;

        [Field(15)]
        public bool Flat { get; set; } = false;

        public int Id => 0x24;

        public JoinGame() { }

        public Task WriteAsync(MinecraftStream stream) => Task.CompletedTask;

        public Task ReadAsync(MinecraftStream stream) => Task.CompletedTask;

        public Task HandleAsync(Obsidian.Server server, Player player) => Task.CompletedTask;
    }

    public enum LevelType
    {
        Default,
        Flat,
        LargeBiomes,
        Amplified,
        Customized,
        Buffet,

        Default_1_1
    }
}