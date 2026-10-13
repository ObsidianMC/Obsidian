using System.Diagnostics.CodeAnalysis;

namespace Obsidian.API.Inventory.DataComponents;

public sealed record class ToolDataComponent : DataComponent
{
    public override DataComponentType Type => DataComponentType.Tool;

    public override string Identifier => "minecraft:tool";

    private ImmutableArray<ToolRule> rules;

    public required ImmutableArray<ToolRule> Rules
    {
        get => this.rules;
        init => this.rules = value;
    }

    private float defaultMiningSpeed;

    public required float DefaultMiningSpeed
    {
        get => this.defaultMiningSpeed;
        init => this.defaultMiningSpeed = value;
    }

    private int damagePerBlock;

    public required int DamagePerBlock
    {
        get => this.damagePerBlock;
        init => this.damagePerBlock = value;
    }

    private bool canDestroyBlocksInCreative = true;

    public bool CanDestroyBlocksInCreative
    {
        get => this.canDestroyBlocksInCreative;
        init => this.canDestroyBlocksInCreative = value;
    }

    [SetsRequiredMembers]
    internal ToolDataComponent() { }

    protected override void ReadCore(INetStreamReader reader)
    {
        this.rules = reader.ReadLengthPrefixedArray(() => new ToolRule()
        {
            Blocks = reader.ReadIdSet(ComponentValueCodecs.Blocks),
            Speed = reader.ReadOptionalFloat(),
            CorrectDropForBlocks = reader.ReadOptionalBoolean()
        }).ToImmutableArray();

        this.defaultMiningSpeed = reader.ReadSingle();
        this.damagePerBlock = reader.ReadVarInt();
        this.canDestroyBlocksInCreative = reader.ReadBoolean();
    }

    public override void Write(INetStreamWriter writer)
    {
        writer.WriteLengthPrefixedArray((rule) => ToolRule.Write(rule, writer), this.Rules.AsSpan());
        writer.WriteSingle(this.DefaultMiningSpeed);
        writer.WriteVarInt(this.DamagePerBlock);
        writer.WriteBoolean(this.CanDestroyBlocksInCreative);
    }
}

public readonly record struct ToolRule : INetworkSerializable<ToolRule>
{
    public required IdSet Blocks { get; init; }

    public float? Speed { get; init; }

    public bool? CorrectDropForBlocks { get; init; }

    public static ToolRule Read(INetStreamReader reader) => new()
    {
        Blocks = IdSet.Read(reader, ComponentValueCodecs.Blocks),
        Speed = reader.ReadOptionalFloat(),
        CorrectDropForBlocks = reader.ReadOptionalBoolean()
    };

    public static void Write(ToolRule value, INetStreamWriter writer)
    {
        IdSet.Write(value.Blocks, writer, ComponentValueCodecs.Blocks);
        writer.WriteOptional(value.Speed);
        writer.WriteOptional(value.CorrectDropForBlocks);
    }
}

/// <summary>
/// A set of a registry's entries, like vanilla's <c>HolderSet</c> stream codec: a tag, or ids that a connection may
/// number differently (<see cref="INetStreamReader.ReadRegistryId"/>).
/// </summary>
public readonly record struct IdSet : INetworkSerializable<IdSet>
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
    public ImmutableArray<int>? Ids { get; init; }

    /// <summary>Reads a set of the registry <paramref name="registry"/>, e.g. <c>minecraft:block</c>.</summary>
    public static IdSet Read(INetStreamReader reader, string registry) => ReadSet(reader, registry);

    /// <summary>Reads a set without translating its ids.</summary>
    [Obsolete("Name the set's registry with Read(reader, registry), so its ids are translated across a connection.")]
    public static IdSet Read(INetStreamReader reader) => ReadSet(reader, null);

    /// <summary>Writes a set of the registry <paramref name="registry"/>, e.g. <c>minecraft:block</c>.</summary>
    public static void Write(IdSet value, INetStreamWriter writer, string registry) =>
        WriteSet(value, writer, registry);

    /// <summary>Writes a set without translating its ids.</summary>
    [Obsolete("Name the set's registry with Write(value, writer, registry), so its ids are translated.")]
    public static void Write(IdSet value, INetStreamWriter writer) => WriteSet(value, writer, null);

    // A null registry keeps the ids as they are, for the obsolete overloads.
    private static IdSet ReadSet(INetStreamReader reader, string? registry)
    {
        var type = reader.ReadVarInt();

        return type == 0
            ? new() { Type = type, TagName = reader.ReadString() }
            : new() { Type = type, Ids = ReadIds(reader, type - 1, registry) };
    }

    private static ImmutableArray<int> ReadIds(INetStreamReader reader, int count, string? registry)
    {
        if (count < 0 || count > 65536)
            throw new System.IO.InvalidDataException("Invalid ID set size.");

        var ids = ImmutableArray.CreateBuilder<int>(count);
        for (var i = 0; i < count; i++)
            ids.Add(registry is null ? reader.ReadVarInt() : reader.ReadRegistryId(registry));

        return ids.MoveToImmutable();
    }

    private static void WriteSet(IdSet value, INetStreamWriter writer, string? registry)
    {
        writer.WriteVarInt(value.Type == 0 ? 0 : value.Ids!.Value.Length + 1);
        if (value.Type == 0)
        {
            if (string.IsNullOrEmpty(value.TagName))
                throw new NullReferenceException("TagName must have a value set if type is 0.");

            writer.WriteString(value.TagName!);
            return;
        }

        if (!value.Ids.HasValue)
            throw new NullReferenceException("Ids must have a value set if type is anything other than 0.");

        var ids = value.Ids.Value;

        foreach (var id in ids)
        {
            if (registry is null)
                writer.WriteVarInt(id);
            else
                writer.WriteRegistryId(registry, id);
        }
    }
}
