using Obsidian.Events.EventArgs;
using System;
using System.Threading.Tasks;

namespace Obsidian.Events
{
    public class MinecraftEventHandler
    {
        private readonly AsyncEvent<PacketReceivedEventArgs> packetReceived;

        private readonly AsyncEvent<PlayerJoinEventArgs> playerJoin;
        private readonly AsyncEvent<PlayerLeaveEventArgs> playerLeave;
        private readonly AsyncEvent<InventoryClickEventArgs> clickEvent;
        private readonly AsyncEvent serverTick;

        public MinecraftEventHandler()
        {
            // Events that don't need additional arguments
            packetReceived = new AsyncEvent<PacketReceivedEventArgs>(HandleException, "PacketReceived");
            playerJoin = new AsyncEvent<PlayerJoinEventArgs>(HandleException, "PlayerJoin");
            playerLeave = new AsyncEvent<PlayerLeaveEventArgs>(HandleException, "PlayerLeave");
            serverTick = new AsyncEvent(HandleException, "ServerTick");
            clickEvent = new AsyncEvent<InventoryClickEventArgs>(HandleException, "InventoryClick");
        }

        /// <summary>
        /// Invoked when any packet gets received.
        /// Used for testing whether events work.
        /// </summary>
        public event AsyncEventHandler<PacketReceivedEventArgs> PacketReceived
        {
            add { this.packetReceived.Register(value); }
            remove { this.packetReceived.Unregister(value); }
        }

        public event AsyncEventHandler<InventoryClickEventArgs> InventoryClick
        {
            add { this.clickEvent.Register(value); }
            remove { this.clickEvent.Unregister(value); }
        }

        public event AsyncEventHandler<PlayerJoinEventArgs> PlayerJoin
        {
            add { this.playerJoin.Register(value); }
            remove { this.playerJoin.Unregister(value); }
        }

        public event AsyncEventHandler ServerTick
        {
            add { this.serverTick.Register(value); }
            remove { this.serverTick.Unregister(value); }
        }

        public event AsyncEventHandler<PlayerLeaveEventArgs> PlayerLeave
        {
            add { this.playerLeave.Register(value); }
            remove { this.playerLeave.Unregister(value); }
        }

        private void HandleException(string eventname, Exception ex) { }

        internal Task InvokeInventoryClickAsync(InventoryClickEventArgs args) =>
            this.clickEvent.InvokeAsync(args);

        internal Task InvokePacketReceivedAsync(PacketReceivedEventArgs eventArgs) =>
            this.packetReceived.InvokeAsync(eventArgs);
        internal Task InvokePlayerJoinAsync(PlayerJoinEventArgs eventArgs) =>
            this.playerJoin.InvokeAsync(eventArgs);

        internal Task InvokePlayerLeaveAsync(PlayerLeaveEventArgs eventArgs) =>
            this.playerLeave.InvokeAsync(eventArgs);

        internal Task InvokeServerTickAsync() =>
            this.serverTick.InvokeAsync();
    }
}