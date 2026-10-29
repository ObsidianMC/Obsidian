using Obsidian.Entities;

namespace Obsidian.Net.Packets.Play.Serverbound;

/// <summary>
/// A spectator's choice of entity to watch through, replacing <c>spectate_entity</c> in 26.2.
/// </summary>
public partial class SpectatorActionPacket
{
    /// <summary>
    /// The entity to watch, or null when the spectator picked none.
    /// </summary>
    public int? SpectateEntityId { get; private set; }

    public override void Populate(INetStreamReader reader)
    {
        // An optional VarInt: 0 for none, otherwise the id plus one.
        var value = reader.ReadVarInt();
        this.SpectateEntityId = value == 0 ? null : value - 1;
    }

    public async override ValueTask HandleAsync(IServer server, IClientPlayer player)
    {
        if (player.GameMode != GameMode.Spectator || player is not PlayerBase spectator || this.SpectateEntityId is not { } id)
            return;

        // Like vanilla's handleSpectatorAction, only an entity in reach can be watched.
        if (InteractPacket.FindTarget(player, id) is { } entity)
            await spectator.SetCameraAsync(entity);
    }
}
