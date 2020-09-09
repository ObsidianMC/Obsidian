using Obsidian.PlayerData;
using Obsidian.Serializer.Attributes;
using Obsidian.Serializer.Enums;
using Obsidian.World;
using System;

namespace Obsidian.Net.Packets.Play
{
    public class JoinGame : Packet
    {
        [Field(0, Type = DataType.Int)]
        public int EntityId { get; set; }

        [Field(1, Type = DataType.UnsignedByte)]
        public Gamemode GameMode { get; set; } = Gamemode.Survival;

        [Field(2, Type = DataType.Int)]
        public Dimension Dimension { get; set; } = Dimension.Overworld;

        [Field(3)]
        public long HashedSeed { get; set; }

        [Field(4)]
        private byte maxPlayers { get; } = 0;

        [Field(5, Type = DataType.String, MaxLength = 16)]
        public LevelType LevelType { get; set; } = LevelType.Default;

        [Field(6, Type = DataType.VarInt)]
        public int ViewDistance { get; set; } = 8;

        [Field(7)]
        public bool ReducedDebugInfo { get; set; } = false;

        [Field(8)]
        public bool EnableRespawnScreen { get; set; } = true;

        public JoinGame() : base(0x26) { }

        public JoinGame(byte[] data) : base(0x26, data) { }
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