using Obsidian.Entities;
using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets;
public sealed partial class ResourcePackResponse : IServerboundPacket, IClientboundPacket
{
    [Field(0), ActualType(typeof(int)), VarLength]
    public ResourcePackResponseResult Result { get; private set; }

    public int Id { get; init; }

    public ValueTask HandleAsync(Client client) => default;
    public ValueTask HandleAsync(Server server, Player player) => default;
}

public enum ResourcePackResponseResult
{
    SuccessfullyLoaded,
    Declined,
    FailedDownload,
    Accepted
}
