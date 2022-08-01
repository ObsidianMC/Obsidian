using Obsidian.Commands;
using Obsidian.Entities;
using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Serverbound;

//TODO USE THIS PACKET PROPERLY
public partial class ChatCommandPacket : IServerboundPacket
{
    [Field(0)]
    public string Command { get; private set; }

    [Field(1)]
    public long Timestamp { get; private set; }

    [Field(2)]
    public long Salt { get; private set; }

    [Field(3)]
    public List<ArgumentSignature> ArgumentSignatures { get; private set; }

    [Field(4)]
    public bool SignedPreview { get; private set; }

    public int Id => 0x03;

    public ValueTask HandleAsync(Server server, Player player) => ValueTask.CompletedTask;
}
