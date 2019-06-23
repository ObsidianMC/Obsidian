using Obsidian.Events.EventArgs;
using System;
using System.Threading.Tasks;

namespace Obsidian.Events
{
    public class MinecraftEventHandler
    {
        private AsyncEvent<PacketReceivedEventArgs> _packetReceived;

        private AsyncEvent<PlayerJoinEventArgs> _playerJoin;
        private AsyncEvent<PlayerLeaveEventArgs> _playerLeave;

        private AsyncEvent _serverTick;

        public MinecraftEventHandler()
        {
            // Events that don't need additional arguments
            _packetReceived = new AsyncEvent<PacketReceivedEventArgs>(HandleException, "PacketReceived");
            _playerJoin = new AsyncEvent<PlayerJoinEventArgs>(HandleException, "PlayerJoin");
            _playerLeave = new AsyncEvent<PlayerLeaveEventArgs>(HandleException, "PlayerLeave");
            _serverTick = new AsyncEvent(HandleException, "ServerTick");
        }

        /// <summary>
        /// Invoked when any packet gets received.
        /// Used for testing whether events work.
        /// </summary>
        public event AsyncEventHandler<PacketReceivedEventArgs> PacketReceived
        {
            add { this._packetReceived.Register(value); }
            remove { this._packetReceived.Unregister(value); }
        }

        public event AsyncEventHandler<PlayerJoinEventArgs> PlayerJoin
        {
            add { this._playerJoin.Register(value); }
            remove { this._playerJoin.Unregister(value); }
        }

        public event AsyncEventHandler ServerTick
        {
            add { this._serverTick.Register(value); }
            remove { this._serverTick.Unregister(value); }
        }

        public event AsyncEventHandler<PlayerLeaveEventArgs> PlayerLeave
        {
            add { this._playerLeave.Register(value); }
            remove { this._playerLeave.Unregister(value); }
        }

        private void HandleException(string eventname, Exception ex)
        {
        }

        internal async Task InvokePacketReceived(PacketReceivedEventArgs eventArgs)
        {
            // invokes event on a new parallel task.
            await Task.Factory.StartNew(async () => { await this._packetReceived.InvokeAsync(eventArgs); });
        }

        internal async Task InvokePlayerJoin(PlayerJoinEventArgs eventArgs)
        {
            await Task.Factory.StartNew(async () => { await this._playerJoin.InvokeAsync(eventArgs); });
        }

        internal async Task InvokePlayerLeave(PlayerLeaveEventArgs eventArgs)
        {
            await Task.Factory.StartNew(async () => { await this._playerLeave.InvokeAsync(eventArgs); });
        }

        internal async Task InvokeServerTick()
        {
            await Task.Factory.StartNew(async () => { await this._serverTick.InvokeAsync(); });
        }
    }
}