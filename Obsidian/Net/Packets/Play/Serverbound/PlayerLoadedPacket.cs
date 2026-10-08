namespace Obsidian.Net.Packets.Play.Serverbound;

public partial class PlayerLoadedPacket
{
    public override ValueTask HandleAsync(IServer server, IPlayer player) =>
        player is Entities.Player connectedPlayer ? connectedPlayer.ShowFirstJoinDialogAsync() : ValueTask.CompletedTask;
}
