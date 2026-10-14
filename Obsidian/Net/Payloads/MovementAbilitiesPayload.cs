namespace Obsidian.Net.Payloads;

/// <summary>
/// A player's movement abilities (see <see cref="MovementAbilities"/>), sent to the player's own Obsidian client on an
/// optional Obsidian channel so that its prediction honours them. Flight isn't in it: it travels as vanilla's
/// <c>mayfly</c> ability, which every client reads.
/// </summary>
internal static class MovementAbilitiesPayload
{
    internal const string Channel = "obsidian:movement";

    internal static byte[] Encode(MovementAbilities abilities)
    {
        using var buffer = new NetworkBuffer();
        buffer.WriteVarInt(abilities.ExtraJumps);
        buffer.WriteBoolean(abilities.CanGlide);

        return buffer.AsSpan(0, buffer.Size).ToArray();
    }

    internal static MovementAbilities Decode(INetStreamReader reader) => new()
    {
        ExtraJumps = Math.Max(0, reader.ReadVarInt()),
        CanGlide = reader.ReadBoolean()
    };
}
