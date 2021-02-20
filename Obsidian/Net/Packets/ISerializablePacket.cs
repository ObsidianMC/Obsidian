namespace Obsidian.Net.Packets
{
    public interface ISerializablePacket : IPacket
    {
        public void Serialize(MinecraftStream stream);
    }
}
