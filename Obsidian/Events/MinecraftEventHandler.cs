using Obsidian.API.Events;
using Obsidian.Events.EventArgs;
using System;
using System.Threading.Tasks;

namespace Obsidian.Events
{
    public class MinecraftEventHandler
    {
        public AsyncEvent<PacketReceivedEventArgs> PacketReceived;
        public AsyncEvent<QueuePacketEventArgs> QueuePacket;
        public AsyncEvent<PlayerJoinEventArgs> PlayerJoin;
        public AsyncEvent<PlayerLeaveEventArgs> PlayerLeave;
        public AsyncEvent<PlayerTeleportEventArgs> PlayerTeleported;
        public AsyncEvent<PermissionGrantedEventArgs> PermissionGranted;
        public AsyncEvent<PermissionRevokedEventArgs> PermissionRevoked;
        public AsyncEvent<InventoryClickEventArgs> ClickEvent;
        public AsyncEvent<BlockBreakEventArgs> BlockBreak;
        public AsyncEvent<BlockInteractEventArgs> BlockInteract;
        public AsyncEvent<IncomingChatMessageEventArgs> IncomingChatMessage;
        public AsyncEvent<ServerStatusRequestEventArgs> ServerStatusRequest;
        public AsyncEvent<EntityInteractEventArgs> EntityInteract;
        public AsyncEvent<PlayerAttackEntityEventArgs> PlayerAttackEntity;
        public AsyncEvent<ItemUsedEventArgs> ItemUsed;
        public AsyncEvent ServerTick;

        public MinecraftEventHandler()
        {
            // Events that don't need additional arguments
            PacketReceived = new("PacketReceived", HandleException);
            QueuePacket = new("QueuePacket", HandleException);

            PlayerJoin = new("PlayerJoin", HandleException);
            PlayerLeave = new("PlayerLeave", HandleException);
            ServerTick = new("ServerTick", HandleException);
            PermissionGranted = new("PermissionGranted", HandleException);
            PermissionRevoked = new("PermissionRevoked", HandleException);
            ClickEvent = new("InventoryClick", HandleException);
            BlockBreak = new("BlockBreak", HandleException);
            BlockInteract = new("BlockInteract", HandleException);
            IncomingChatMessage = new("IncomingChatMessage", HandleException);
            PlayerTeleported = new("PlayerTeleported", HandleException);
            ServerStatusRequest = new("ServerStatusRequest", HandleException);
            EntityInteract = new("EntityInteract", HandleException);
            PlayerAttackEntity = new("PlayerAttackEntity", HandleException);
            ItemUsed = new("ItemUsed", HandleException);
        }

        private void HandleException(AsyncEvent e, Exception exception)
        {
        }

        private void HandleException<T>(AsyncEvent<T> e, Exception exception)
        {
        }

        internal async ValueTask<QueuePacketEventArgs> InvokeQueuePacketAsync(QueuePacketEventArgs eventArgs)
        {
            await QueuePacket.InvokeAsync(eventArgs);
            return eventArgs;
        }

        internal async ValueTask<InventoryClickEventArgs> InvokeInventoryClickAsync(InventoryClickEventArgs eventArgs)
        {
            await ClickEvent.InvokeAsync(eventArgs);
            return eventArgs;
        }

        internal async ValueTask<BlockBreakEventArgs> InvokeBlockBreakAsync(BlockBreakEventArgs eventArgs)
        {
            await BlockBreak.InvokeAsync(eventArgs);
            return eventArgs;
        }

        internal async ValueTask<BlockInteractEventArgs> InvokeBlockInteractAsync(BlockInteractEventArgs eventArgs)
        {
            await BlockInteract.InvokeAsync(eventArgs);
            return eventArgs;
        }

        internal async ValueTask<IncomingChatMessageEventArgs> InvokeIncomingChatMessageAsync(IncomingChatMessageEventArgs eventArgs)
        {
            await IncomingChatMessage.InvokeAsync(eventArgs);
            return eventArgs;
        }

        internal async ValueTask<PlayerTeleportEventArgs> InvokePlayerTeleportedAsync(PlayerTeleportEventArgs eventArgs)
        {
            await PlayerTeleported.InvokeAsync(eventArgs);
            return eventArgs;
        }

        internal async ValueTask<PermissionGrantedEventArgs> InvokePermissionGrantedAsync(PermissionGrantedEventArgs eventArgs)
        {
            await PermissionGranted.InvokeAsync(eventArgs);
            return eventArgs;
        }

        internal async ValueTask<PermissionRevokedEventArgs> InvokePermissionRevokedAsync(PermissionRevokedEventArgs eventArgs)
        {
            await PermissionRevoked.InvokeAsync(eventArgs);
            return eventArgs;
        }

        internal async ValueTask<EntityInteractEventArgs> InvokeEntityInteractAsync(EntityInteractEventArgs eventArgs)
        {
            await EntityInteract.InvokeAsync(eventArgs);
            return eventArgs;
        }

        internal async ValueTask<PlayerAttackEntityEventArgs> InvokePlayerAttackEntityAsync(PlayerAttackEntityEventArgs eventArgs)
        {
            await PlayerAttackEntity.InvokeAsync(eventArgs);
            return eventArgs;
        }

        internal async ValueTask<ItemUsedEventArgs> InvokeItemUsedAsync(ItemUsedEventArgs eventArgs)
        {
            await ItemUsed.InvokeAsync(eventArgs);
            return eventArgs;
        }

        internal ValueTask InvokePacketReceivedAsync(PacketReceivedEventArgs eventArgs) =>
            PacketReceived.InvokeAsync(eventArgs);

        internal ValueTask InvokePlayerJoinAsync(PlayerJoinEventArgs eventArgs) =>
            PlayerJoin.InvokeAsync(eventArgs);

        internal ValueTask InvokePlayerLeaveAsync(PlayerLeaveEventArgs eventArgs) =>
            PlayerLeave.InvokeAsync(eventArgs);

        internal ValueTask InvokeServerTickAsync() =>
            ServerTick.InvokeAsync();

        internal async ValueTask<ServerStatusRequestEventArgs> InvokeServerStatusRequest(ServerStatusRequestEventArgs eventArgs)
        {
            await ServerStatusRequest.InvokeAsync(eventArgs);
            return eventArgs;
        }
    }
}