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

        internal async Task<QueuePacketEventArgs> InvokeQueuePacketAsync(QueuePacketEventArgs args)
        {
            await this.QueuePacket.InvokeAsync(args);

            return args;
        }

        internal async Task<InventoryClickEventArgs> InvokeInventoryClickAsync(InventoryClickEventArgs args)
        {
            await this.ClickEvent.InvokeAsync(args);

            return args;
        }

        internal async Task<BlockInteractEventArgs> InvokeBlockInteractAsync(BlockInteractEventArgs eventArgs)
        {
            await this.BlockInteract.InvokeAsync(eventArgs);

            return eventArgs;
        }

        internal async Task<IncomingChatMessageEventArgs> InvokeIncomingChatMessageAsync(IncomingChatMessageEventArgs eventArgs)
        {
            await this.IncomingChatMessage.InvokeAsync(eventArgs);

            return eventArgs;
        }

        internal async Task<PlayerTeleportEventArgs> InvokePlayerTeleportedAsync(PlayerTeleportEventArgs eventArgs)
        {
            await this.PlayerTeleported.InvokeAsync(eventArgs);
            return eventArgs;
        }

        internal async Task<PermissionGrantedEventArgs> InvokePermissionGrantedAsync(PermissionGrantedEventArgs eventArgs)
        {
            await this.PermissionGranted.InvokeAsync(eventArgs);
            return eventArgs;
        }

        internal async Task<PermissionRevokedEventArgs> InvokePermissionRevokedAsync(PermissionRevokedEventArgs eventArgs)
        {
            await this.PermissionRevoked.InvokeAsync(eventArgs);
            return eventArgs;
        }

        internal async Task<EntityInteractEventArgs> InvokeEntityInteractAsync(EntityInteractEventArgs eventArgs)
        {
            await this.EntityInteract.InvokeAsync(eventArgs);
            return eventArgs;
        }

        internal async Task<PlayerAttackEntityEventArgs> InvokePlayerAttackEntityAsync(PlayerAttackEntityEventArgs eventArgs)
        {
            await this.PlayerAttackEntity.InvokeAsync(eventArgs);
            return eventArgs;
        }

        internal async Task<ItemUsedEventArgs> InvokeItemUsedAsync(ItemUsedEventArgs eventArgs)
        {
            await this.ItemUsed.InvokeAsync(eventArgs);
            return eventArgs;
        }

        internal ValueTask InvokePacketReceivedAsync(PacketReceivedEventArgs eventArgs) =>
            this.PacketReceived.InvokeAsync(eventArgs);

        internal ValueTask InvokePlayerJoinAsync(PlayerJoinEventArgs eventArgs) =>
            this.PlayerJoin.InvokeAsync(eventArgs);

        internal ValueTask InvokePlayerLeaveAsync(PlayerLeaveEventArgs eventArgs) =>
            this.PlayerLeave.InvokeAsync(eventArgs);

        internal ValueTask InvokeServerTickAsync() =>
            this.ServerTick.InvokeAsync();

        internal async Task<ServerStatusRequestEventArgs> InvokeServerStatusRequest(ServerStatusRequestEventArgs eventargs)
        {
            await this.ServerStatusRequest.InvokeAsync(eventargs);

            return eventargs;
        }
    }
}