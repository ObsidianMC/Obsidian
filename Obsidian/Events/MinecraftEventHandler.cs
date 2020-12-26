using Obsidian.API.Events;
using Obsidian.Events.EventArgs;
using System;
using System.Threading.Tasks;

namespace Obsidian.Events
{
    public class MinecraftEventHandler
    {
        private readonly AsyncEvent<PacketReceivedEventArgs> packetReceived;
        private readonly AsyncEvent<QueuePacketEventArgs> queuePacket;

        private readonly AsyncEvent<PlayerJoinEventArgs> playerJoin;
        private readonly AsyncEvent<PlayerLeaveEventArgs> playerLeave;
        private readonly AsyncEvent<PlayerTeleportEventArgs> playerTeleported;

        private readonly AsyncEvent<PermissionGrantedEventArgs> permissionGranted;
        private readonly AsyncEvent<PermissionRevokedEventArgs> permissionRevoked;

        private readonly AsyncEvent<InventoryClickEventArgs> clickEvent;
        private readonly AsyncEvent<BlockInteractEventArgs> blockInteract;
        private readonly AsyncEvent<IncomingChatMessageEventArgs> incomingChatMessage;
        private readonly AsyncEvent<ServerStatusRequestEventArgs> serverStatusRequest;
        private readonly AsyncEvent serverTick;

        public MinecraftEventHandler()
        {
            // Events that don't need additional arguments
            this.packetReceived = new AsyncEvent<PacketReceivedEventArgs>(HandleException, "PacketReceived");
            this.queuePacket = new AsyncEvent<QueuePacketEventArgs>(HandleException, "QueuePacket");

            this.playerJoin = new AsyncEvent<PlayerJoinEventArgs>(HandleException, "PlayerJoin");
            this.playerLeave = new AsyncEvent<PlayerLeaveEventArgs>(HandleException, "PlayerLeave");
            this.serverTick = new AsyncEvent(HandleException, "ServerTick");
            this.permissionGranted = new AsyncEvent<PermissionGrantedEventArgs>(HandleException, "PermissionGranted");
            this.permissionRevoked = new AsyncEvent<PermissionRevokedEventArgs>(HandleException, "PermissionRevoked");
            this.clickEvent = new AsyncEvent<InventoryClickEventArgs>(HandleException, "InventoryClick");
            this.blockInteract = new AsyncEvent<BlockInteractEventArgs>(HandleException, "BlockInteract");
            this.incomingChatMessage = new AsyncEvent<IncomingChatMessageEventArgs>(HandleException, "IncomingChatMessage");
            this.playerTeleported = new AsyncEvent<PlayerTeleportEventArgs>(HandleException, "PlayerTeleported");
            this.serverStatusRequest = new AsyncEvent<ServerStatusRequestEventArgs>(HandleException, "ServerStatusRequest");
        }

        /// <summary>
        /// Invoked when any packet gets received.
        /// Used for testing whether events work.
        /// </summary>
        public event AsyncEventHandler<PacketReceivedEventArgs> PacketReceived
        {
            add => packetReceived.Register(value);
            remove => packetReceived.Unregister(value);
        }

        public event AsyncEventHandler<PlayerTeleportEventArgs> PlayerTeleported
        {
            add => playerTeleported.Register(value);
            remove => playerTeleported.Unregister(value);
        }

        public event AsyncEventHandler<QueuePacketEventArgs> QueuePacket
        {
            add => queuePacket.Register(value);
            remove => queuePacket.Unregister(value);
        }

        public event AsyncEventHandler<InventoryClickEventArgs> InventoryClick
        {
            add => clickEvent.Register(value);
            remove => clickEvent.Unregister(value);
        }

        public event AsyncEventHandler<PermissionGrantedEventArgs> PermissionGranted
        {
            add => permissionGranted.Register(value);
            remove => permissionGranted.Unregister(value);
        }
        public event AsyncEventHandler<PermissionRevokedEventArgs> PermissionRevoked
        {
            add => permissionRevoked.Register(value);
            remove => permissionRevoked.Unregister(value);
        }

        public event AsyncEventHandler<PlayerJoinEventArgs> PlayerJoin
        {
            add => playerJoin.Register(value);
            remove => playerJoin.Unregister(value);
        }

        public event AsyncEventHandler ServerTick
        {
            add => serverTick.Register(value);
            remove => serverTick.Unregister(value);
        }

        public event AsyncEventHandler<PlayerLeaveEventArgs> PlayerLeave
        {
            add => playerLeave.Register(value);
            remove => playerLeave.Unregister(value);
        }

        public event AsyncEventHandler<BlockInteractEventArgs> BlockInteract
        {
            add => blockInteract.Register(value);
            remove => blockInteract.Unregister(value);
        }

        public event AsyncEventHandler<IncomingChatMessageEventArgs> IncomingChatMessage
        {
            add => incomingChatMessage.Register(value);
            remove => incomingChatMessage.Unregister(value);
        }

        public event AsyncEventHandler<ServerStatusRequestEventArgs> ServerStatusRequest
        {
            add => serverStatusRequest.Register(value);
            remove => serverStatusRequest.Unregister(value);
        }

        private void HandleException(string eventname, Exception ex)
        {
        }

        internal async Task<QueuePacketEventArgs> InvokeQueuePacketAsync(QueuePacketEventArgs args)
        {
            await this.queuePacket.InvokeAsync(args);

            return args;
        }

        internal async Task<InventoryClickEventArgs> InvokeInventoryClickAsync(InventoryClickEventArgs args)
        {
            await this.clickEvent.InvokeAsync(args);

            return args;
        }

        internal async Task<BlockInteractEventArgs> InvokeBlockInteractAsync(BlockInteractEventArgs eventArgs)
        {
            await this.blockInteract.InvokeAsync(eventArgs);

            return eventArgs;
        }

        internal async Task<IncomingChatMessageEventArgs> InvokeIncomingChatMessageAsync(IncomingChatMessageEventArgs eventArgs)
        {
            await this.incomingChatMessage.InvokeAsync(eventArgs);

            return eventArgs;
        }

        internal async Task<PlayerTeleportEventArgs> InvokePlayerTeleportedAsync(PlayerTeleportEventArgs eventArgs)
        {
            await this.playerTeleported.InvokeAsync(eventArgs);
            return eventArgs;
        }

        internal async Task<PermissionGrantedEventArgs> InvokePermissionGrantedAsync(PermissionGrantedEventArgs eventArgs)
        {
            await this.permissionGranted.InvokeAsync(eventArgs);
            return eventArgs;
        }

        internal async Task<PermissionRevokedEventArgs> InvokePermissionRevokedAsync(PermissionRevokedEventArgs eventArgs)
        {
            await this.permissionRevoked.InvokeAsync(eventArgs);
            return eventArgs;
        }

        internal Task InvokePacketReceivedAsync(PacketReceivedEventArgs eventArgs) =>
            this.packetReceived.InvokeAsync(eventArgs);

        internal Task InvokePlayerJoinAsync(PlayerJoinEventArgs eventArgs) =>
            this.playerJoin.InvokeAsync(eventArgs);

        internal Task InvokePlayerLeaveAsync(PlayerLeaveEventArgs eventArgs) =>
            this.playerLeave.InvokeAsync(eventArgs);

        internal Task InvokeServerTickAsync() =>
            this.serverTick.InvokeAsync();

        internal async Task<ServerStatusRequestEventArgs> InvokeServerStatusRequest(ServerStatusRequestEventArgs eventargs)
        {
            await this.serverStatusRequest.InvokeAsync(eventargs);

            return eventargs;
        }
    }
}