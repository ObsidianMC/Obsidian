namespace Obsidian.Net.Packets
{
    public interface IClientboundPacket : IPacket
    {
        public void Serialize(MinecraftStream stream);
    }
}
