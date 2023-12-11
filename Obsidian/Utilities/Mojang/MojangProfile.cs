using System.Text.Json.Serialization;

namespace Obsidian.Utilities.Mojang;

public sealed class MojangProfile
{
    public required Guid Id { get; init; }

    public required string Name { get; init; }

    public bool? Legacy { get; init; }

    public bool? Demo { get; init; }

    public List<SkinProperty>? Properties { get; init; }
}

public sealed class CachedProfile
{
    public required string Name { get; set; }

    public required Guid Uuid { get; init; }

    public DateTimeOffset ExpiresOn { get; set; }

    [JsonIgnore]
    public bool Expired => DateTimeOffset.UtcNow >= this.ExpiresOn;
}

