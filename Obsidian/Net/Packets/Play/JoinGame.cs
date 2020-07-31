using System;
using System.Threading.Tasks;
using Obsidian.PlayerData;
using Obsidian.Serializer.Attributes;
using Obsidian.World;

namespace Obsidian.Net.Packets.Play
{
    public class JoinGame : Packet
    {
        [PacketOrder(0, true)]
        public int EntityId { get; set; }

        [PacketOrder(1)]
        public Gamemode GameMode { get; set; } = Gamemode.Survival;

        [PacketOrder(2)]
        public Dimension Dimension { get; set; } = Dimension.Overworld;

        [PacketOrder(3)]
        public Difficulty Difficulty { get; set; } = Difficulty.Peaceful;

        [PacketOrder(4)]
        private byte maxPlayers { get; } = 0;

        [PacketOrder(5)]
        public string LevelType { get; set; } = "default";

        [PacketOrder(6)]
        public bool ReducedDebugInfo { get; set; }

        public JoinGame() : base(0x25) { }

        public JoinGame(byte[] data) : base(0x25, data) { }

        public JoinGame(int entityid, Gamemode gamemode, Dimension dimension, Difficulty difficulty, string leveltype, bool debugging) : base(0x25, Array.Empty<byte>())
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