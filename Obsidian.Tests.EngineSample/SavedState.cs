using Obsidian.API;
using Obsidian.API.Data;
using Obsidian.API.Inventory.DataComponents;
using Obsidian.API.Plugins;
using Obsidian.Nbt;

namespace EngineSample;

/// <summary>
/// Phase 5: network ids saved independently of a block, and membership attached to players, entities or chunks.
/// Both codecs use detached lists; a mod sets membership again after changing a list returned by GetAttachment.
/// </summary>
public sealed class SavedState
{
    public SavedDataType<List<int>> Networks { get; }
    public AttachmentType<List<int>> Membership { get; }

    public SavedState(IPluginRegistry registry)
    {
        var codec = new ComponentCodec<List<int>>(
            values => new NbtArray<int>("", values.ToArray()),
            tag => ((NbtArray<int>)tag).GetArray().ToList());
        this.Networks = registry.RegisterSavedData("sample:networks", codec);
        this.Membership = registry.RegisterAttachment("sample:membership", codec,
            new ComponentNetworkCodec<List<int>>(Write, Read), copyOnDeath: true);
    }

    private static void Write(INetStreamWriter writer, List<int> values)
    {
        writer.WriteVarInt(values.Count);
        foreach (var value in values)
            writer.WriteVarInt(value);
    }

    private static List<int> Read(INetStreamReader reader)
    {
        var count = reader.ReadVarInt();
        if (count < 0 || count > 1024)
            throw new InvalidOperationException("Invalid membership count.");

        var values = new List<int>(count);
        for (var i = 0; i < count; i++)
            values.Add(reader.ReadVarInt());

        return values;
    }
}
