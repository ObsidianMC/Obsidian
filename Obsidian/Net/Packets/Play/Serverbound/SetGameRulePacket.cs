using Obsidian.WorldData;

namespace Obsidian.Net.Packets.Play.Serverbound;

public partial class SetGameRulePacket
{
    public IReadOnlyList<KeyValuePair<string, string>> Entries { get; private set; } = [];

    public override void Populate(INetStreamReader reader)
    {
        var count = reader.ReadVarInt();
        // Each entry needs at least two length bytes; reject impossible sizes before allocating.
        if (count < 0 || reader is NetworkBuffer buffer && count > (buffer.Size - buffer.Offset) / 2)
            throw new System.IO.InvalidDataException("Invalid game rule entry count.");
        var entries = new List<KeyValuePair<string, string>>();
        for (var i = 0; i < count; i++)
            entries.Add(new(reader.ReadString(), reader.ReadString()));
        this.Entries = entries;
    }

    public override async ValueTask HandleAsync(IServer server, IClientPlayer player)
    {
        // Vanilla requires COMMANDS_GAMEMASTER, the same level-2 permission as /gamerule.
        if (server.Operators is not OperatorList operators || operators.GetPermissionLevel(player) < 2)
            return;

        foreach (var (name, value) in this.Entries)
            await GameRuleSynchronization.SetAsync(server, name, value);
    }
}
