using System.Threading.Tasks;

namespace Obsidian.Net.Packets
{
    public class JoinGame : Packet
    {
        public JoinGame(int entityid, byte gamemode, int dimension, byte difficulty, string leveltype, bool debugging) : base(0x25, new byte[0])
        {
            this.EntityId = entityid;
            this.GameMode = gamemode;
            this.Dimension = dimension;
            this.Difficulty = difficulty;
            this.LevelType = leveltype;
            this.ReducedDebugInfo = !debugging;
        }

        public JoinGame(byte[] data) : base(0x25, data) { }

        public int EntityId { get; private set; }

        public byte GameMode { get; private set; } = 0; // survival

        public int Dimension { get; private set; } = 1; //overworld

        public byte Difficulty { get; private set; } = 0; // peaceful

        public byte MaxPlayers { get; private set; } = 0; // Gets ignored by client

        public string LevelType { get; private set; } = "default";

        public bool ReducedDebugInfo { get; private set; } = false;

        public override async Task PopulateAsync()
        {
            using (var stream = new MinecraftStream(this.PacketData))
            {
                this.EntityId = await stream.ReadVarIntAsync();
                this.GameMode = await stream.ReadUnsignedByteAsync();
                this.Dimension = await stream.ReadIntAsync();
                this.Difficulty = await stream.ReadUnsignedByteAsync();
                this.MaxPlayers = await stream.ReadUnsignedByteAsync(); //maxplayers
                this.LevelType = await stream.ReadStringAsync();
                this.ReducedDebugInfo = await stream.ReadBooleanAsync();
            }
        }

        public override async Task<byte[]> ToArrayAsync()
        {
            using(var stream = new MinecraftStream())
            {
                await stream.WriteIntAsync(this.EntityId);
                await stream.WriteUnsignedByteAsync(this.GameMode);
                await stream.WriteIntAsync(this.Dimension);
                await stream.WriteUnsignedByteAsync(this.Difficulty);
                await stream.WriteUnsignedByteAsync(this.MaxPlayers);
                await stream.WriteStringAsync(this.LevelType);
                await stream.WriteBooleanAsync(this.ReducedDebugInfo);
                return stream.ToArray();
            }
        }
    }
}