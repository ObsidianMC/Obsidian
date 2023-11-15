using System.Text.Json.Serialization;

namespace Obsidian.Utilities.Mojang;

public sealed class MojangUser
{
    public required Guid Id { get; init; }

    public required string Name { get; init; }

    public bool? Legacy { get; init; }

    public bool? Demo { get; init; }

    public List<SkinProperty>? Properties { get; init; }
}

public sealed class CachedUser
{
    public required string Name { get; set; }

    public required Guid Id { get; init; }

    public DateTimeOffset ExpiresOn { get; set; }

    [JsonIgnore]
    public bool Expired => DateTimeOffset.UtcNow >= this.ExpiresOn;
}

