using Obsidian.Entities;
using Obsidian.PlayerData;
using Obsidian.Util;

namespace Obsidian.Net.Packets
{
    public class JoinGame : Packet
    {
        [Variable(VariableType.Int)]
        public int EntityId { get; private set; }

        [Variable(VariableType.UnsignedByte)]
        public Gamemode GameMode { get; private set; } = Gamemode.Survival;

        [Variable(VariableType.Int)]
        public Dimension Dimension { get; private set; } = Dimension.Overworld;

        [Variable(VariableType.UnsignedByte)]
        public Difficulty Difficulty { get; private set; } = Difficulty.Peaceful;

        [Variable]
        public byte MaxPlayers { get; private set; } = 0; // Gets ignored by client

        [Variable]
        public string LevelType { get; private set; } = "default";

        [Variable]
        public bool ReducedDebugInfo { get; private set; } = false;

        public JoinGame(byte[] data) : base(0x25, data)
        {
        }

        public JoinGame(int entityid, Gamemode gamemode, Dimension dimension, Difficulty difficulty, string leveltype, bool debugging) : base(0x25, new byte[0])
        {
            this.EntityId = entityid;
            this.GameMode = gamemode;
            this.Dimension = dimension;
            this.Difficulty = difficulty;
            this.LevelType = leveltype;
            this.ReducedDebugInfo = !debugging;
        }
    }
}