using Obsidian.API.Events;
using Obsidian.Events.EventArgs;

namespace Obsidian.Events;

public sealed class MinecraftEvents
{
    public AsyncEvent<PacketReceivedEventArgs> PacketReceived = new(nameof(PacketReceived), HandleException);
    public AsyncEvent<QueuePacketEventArgs> QueuePacket = new(nameof(QueuePacket), HandleException);
    public AsyncEvent<PlayerJoinEventArgs> PlayerJoin = new(nameof(PlayerJoin), HandleException);
    public AsyncEvent<PlayerLeaveEventArgs> PlayerLeave = new(nameof(PlayerLeave), HandleException);
    public AsyncEvent<PlayerTeleportEventArgs> PlayerTeleported = new(nameof(PlayerTeleported), HandleException);
    public AsyncEvent<PermissionGrantedEventArgs> PermissionGranted = new(nameof(PermissionGranted), HandleException);
    public AsyncEvent<PermissionRevokedEventArgs> PermissionRevoked = new(nameof(PermissionRevoked), HandleException);
    public AsyncEvent<ContainerClickEventArgs> ContainerClick = new(nameof(ContainerClick), HandleException);
    public AsyncEvent<BlockBreakEventArgs> BlockBreak = new(nameof(BlockBreak), HandleException);
    public AsyncEvent<IncomingChatMessageEventArgs> IncomingChatMessage = new(nameof(IncomingChatMessage), HandleException);
    public AsyncEvent<ServerStatusRequestEventArgs> ServerStatusRequest = new(nameof(ServerStatusRequest), HandleException);
    public AsyncEvent<EntityInteractEventArgs> EntityInteract = new(nameof(EntityInteract), HandleException);
    public AsyncEvent<PlayerAttackEntityEventArgs> PlayerAttackEntity = new(nameof(PlayerAttackEntity), HandleException);
    public AsyncEvent<PlayerInteractEventArgs> PlayerInteract = new(nameof(PlayerInteract), HandleException);
    public AsyncEvent<ContainerClosedEventArgs> ContainerClosed = new(nameof(ContainerClosed), HandleException);
    public AsyncEvent ServerTick = new(nameof(ServerTick), HandleException);

    private static void HandleException(AsyncEvent e, Exception exception)
    {
    }

    private static void HandleException<T>(AsyncEvent<T> e, Exception exception)
    {
    }
}
