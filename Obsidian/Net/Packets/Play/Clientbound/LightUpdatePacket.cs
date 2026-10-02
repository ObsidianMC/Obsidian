namespace Obsidian.Net.Packets.Play.Clientbound;

/// <summary>
/// Updates the light of a chunk the client already has, like vanilla's <c>ClientboundLightUpdatePacket</c>.
/// </summary>
public partial class LightUpdatePacket(IChunk chunk)
{
    public IChunk Chunk { get; } = chunk;

    public override void Serialize(INetStreamWriter writer)
    {
        writer.WriteVarInt(Chunk.X);
        writer.WriteVarInt(Chunk.Z);

        Chunk.WriteLightMaskTo(writer, LightType.Sky);
        Chunk.WriteLightMaskTo(writer, LightType.Block);

        Chunk.WriteEmptyLightMaskTo(writer, LightType.Sky);
        Chunk.WriteEmptyLightMaskTo(writer, LightType.Block);

        Chunk.WriteLightTo(writer, LightType.Sky);
        Chunk.WriteLightTo(writer, LightType.Block);
    }
}
