using Obsidian.Entities;
using Obsidian.Packets.Handshaking;
using System;
using System.IO;
using System.Threading.Tasks;

namespace Obsidian.Packets
{
    public class JoinGame
    {
        public JoinGame(int entityid, byte gamemode, int dimension, byte difficulty, string leveltype, bool debugging)
        {
            this.EntityId = entityid;
            this.GameMode = gamemode;
            this.Dimension = dimension;
            this.Difficulty = difficulty;
            this.LevelType = leveltype;
            this.ReducedDebugInfo = !debugging;
        }

        public int EntityId { get; private set; }

        public byte GameMode { get; private set; } = 0; // survival

        public int Dimension { get; private set; } = 1; //overworld

        public byte Difficulty { get; private set; } = 0; // peaceful

        public byte MaxPlayers { get; private set; } = 0; // Gets ignored by client

        public string LevelType { get; private set; } = "default";

        public bool ReducedDebugInfo { get; private set; } = false;

        public static async Task<JoinGame> FromArrayAsync(byte[] data)
        {
            MemoryStream stream = new MemoryStream(data);
            var entity = await stream.ReadVarIntAsync();
            var gamemode = await stream.ReadUnsignedByteAsync();
            var dimension = await stream.ReadIntAsync();
            var difficulty = await stream.ReadUnsignedByteAsync();
            await stream.ReadUnsignedByteAsync(); //maxplayers
            var leveltype = await stream.ReadStringAsync();
            var reducedebug = await stream.ReadBooleanAsync();
            return new JoinGame(entity, gamemode, dimension, difficulty, leveltype, !reducedebug);
        }

        public async Task<byte[]> ToArrayAsync()
        {
            MemoryStream stream = new MemoryStream();
            await stream.WriteIntAsync(EntityId);
            await stream.WriteUnsignedByteAsync(GameMode);
            await stream.WriteIntAsync(Dimension);
            await stream.WriteUnsignedByteAsync(Difficulty);
            await stream.WriteUnsignedByteAsync(MaxPlayers);
            await stream.WriteStringAsync(LevelType);
            await stream.WriteBooleanAsync(ReducedDebugInfo);
            return stream.ToArray();
        }
    }
}