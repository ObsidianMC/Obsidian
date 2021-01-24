using Obsidian.API;
using Obsidian.Chat;
using Obsidian.Entities;
using Obsidian.Util.Extensions;
using System;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Play.Clientbound
{
    public class ScoreboardObjectivePacket : IPacket
    {
        public string ObjectiveName { get; set; }

        public ScoreboardMode Mode { get; set; }

        public IChatMessage Value { get; set; }

        public DisplayType Type { get; set; }

        public int Id => 0x4A;

        public Task HandleAsync(Server server, Player player)
        {
            throw new NotImplementedException();
        }

        public Task ReadAsync(MinecraftStream stream)
        {
            throw new NotImplementedException();
        }

        public void Serialize(MinecraftStream stream)
        {
            using var packetStream = new MinecraftStream();
            packetStream.WriteString(this.ObjectiveName);
            packetStream.WriteByte((sbyte)this.Mode);

            if (this.Mode is ScoreboardMode.Create or ScoreboardMode.Update)
            {
                packetStream.WriteChat((ChatMessage)this.Value);
                packetStream.WriteVarInt(this.Type);
            }

            stream.Lock.Wait();

            stream.WriteVarInt(this.Id.GetVarIntLength() + (int)packetStream.Length);
            stream.WriteVarInt(this.Id);

            packetStream.Position = 0;
            packetStream.CopyTo(stream);

            stream.Lock.Release();
        }

        public Task WriteAsync(MinecraftStream stream)
        {
            throw new NotImplementedException();
        }
    }



    public enum ScoreboardMode : sbyte
    {
        Create,

        Remove,

        Update
    }
}
