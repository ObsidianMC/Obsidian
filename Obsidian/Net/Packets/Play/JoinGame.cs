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

        [Field(3, Type = DataType.UnsignedByte)]
        public Difficulty Difficulty { get; set; } = Difficulty.Peaceful;

        [Field(4)]
        private byte maxPlayers { get; } = 0;

        [Field(5, Type = DataType.String, MaxLength = 16)]
        public LevelType LevelType { get; set; } = LevelType.Default;

        [Field(6)]
        public bool ReducedDebugInfo { get; set; }

        public JoinGame() : base(0x25) { }

        public JoinGame(byte[] data) : base(0x25, data) { }

        public JoinGame(int entityid, Gamemode gamemode, Dimension dimension, Difficulty difficulty, LevelType leveltype, bool debugging) : base(0x25, Array.Empty<byte>())
        {
            this.EntityId = entityid;
            this.GameMode = gamemode;
            this.Dimension = dimension;
            this.Difficulty = difficulty;
            this.LevelType = leveltype;
            this.ReducedDebugInfo = !debugging;
        }
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