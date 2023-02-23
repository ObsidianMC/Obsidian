namespace Obsidian.IO;

/// <summary>
/// Helper interface for types that can be written
/// </summary>
public interface IPacketWritable
{
    void WriteToPacketWriter<O>(ref PacketWriter<O> writer) where O : IPacketOutput;
}
