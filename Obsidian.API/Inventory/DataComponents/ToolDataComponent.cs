using System.Diagnostics.CodeAnalysis;

namespace Obsidian.API.Inventory.DataComponents;
public sealed class ToolDataComponent : IDataComponent
{
    public DataComponentType Type => DataComponentType.Tool;

    public string Identifier => "minecraft:tool";

    public required List<ToolRule> Rules { get; set; }

    public required float DefaultMiningSpeed { get; set; }

    public required int DamagePerBlock { get; set; }

    [SetsRequiredMembers]
    internal ToolDataComponent() { }

    public void Read(INetStreamReader reader)
    {
        this.Rules = reader.ReadLengthPrefixedArray(() => new ToolRule()
        {
            Blocks = reader.ReadIdSet(),
            Speed = reader.ReadOptionalFloat(),
            CorrectDropForBlocks = reader.ReadOptionalBoolean()
        });

        this.DefaultMiningSpeed = reader.ReadSingle();
        this.DamagePerBlock = reader.ReadVarInt();
    }

    public void Write(INetStreamWriter writer)
    {
        writer.WriteLengthPrefixedArray((rule) => ToolRule.Write(rule, writer), this.Rules);
        writer.WriteSingle(this.DefaultMiningSpeed);
        writer.WriteVarInt(this.DamagePerBlock);
    }
}

public readonly struct ToolRule : INetworkSerializable<ToolRule>
{
    public required IdSet Blocks { get; init; }

    public float? Speed { get; init; }

    public bool? CorrectDropForBlocks { get; init; }

    public static ToolRule Read(INetStreamReader reader) => new()
    {
        Blocks = IdSet.Read(reader),
        Speed = reader.ReadOptionalFloat(),
        CorrectDropForBlocks = reader.ReadOptionalBoolean()
    };

    public static void Write(ToolRule value, INetStreamWriter writer)
    {
        IdSet.Write(value.Blocks, writer);
        writer.WriteOptional(value.Speed);
        writer.WriteOptional(value.CorrectDropForBlocks);
    }
}

public readonly struct IdSet : INetworkSerializable<IdSet>
{
    /// <summary>
    /// Value used to determine the data that follows. 
    /// It can be either: 
    /// <list type="bullet">
    /// <item>
    /// 0 - Represents a named set of IDs defined by a tag.
    /// </item>
    /// <item>
    /// Anything else - Represents an ad-hoc set of IDs enumerated inline.
    /// </item>
    /// </list> 
    /// </summary>
    public required int Type { get; init; }

    /// <summary>
    /// The registry tag defining the ID set. Only present if Type is 0.
    /// </summary>
    public string? TagName { get; init; }

    /// <summary>
    /// An array of registry IDs. Only present if Type is not 0. 
    /// The size of the array is equal to Type - 1.
    /// </summary>
    public List<int>? Ids { get; init; }

    public static IdSet Read(INetStreamReader reader)
    {
        var type = reader.ReadVarInt();

        return type == 0
            ? new() { Type = type, TagName = reader.ReadString() }
            : new() { Type = type, Ids = reader.ReadLengthPrefixedArray(reader.ReadVarInt) };
    }

    public static void Write(IdSet value, INetStreamWriter writer)
    {
        writer.WriteVarInt(value.Type);
        if (value.Type == 0)
        {
            if (string.IsNullOrEmpty(value.TagName))
                throw new NullReferenceException("TagName must have a value set if type is 0.");

            writer.WriteString(value.TagName!);
            return;
        }

        if (value.Ids == null)
            throw new NullReferenceException("Ids must have a value set if type is anything other than 0.");

        writer.WriteVarInt(value.Ids.Count);

        foreach (var id in value.Ids)
            writer.WriteVarInt(id);
    }
}
