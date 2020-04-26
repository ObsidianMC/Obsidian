using Obsidian.Events.EventArgs;
using System;
using System.Threading.Tasks;

namespace Obsidian.Events
{
    public class MinecraftEventHandler
    {
        private readonly AsyncEvent<PacketReceivedEventArgs> _packetReceived;

        private readonly AsyncEvent<PlayerJoinEventArgs> _playerJoin;
        private readonly AsyncEvent<PlayerLeaveEventArgs> _playerLeave;

        private readonly AsyncEvent _serverTick;

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

        internal Task InvokePacketReceivedAsync(PacketReceivedEventArgs eventArgs)
        {
            _ = Task.Run(async () => { await this._packetReceived.InvokeAsync(eventArgs); });

            return Task.CompletedTask;
        }

        internal Task InvokePlayerJoinAsync(PlayerJoinEventArgs eventArgs)
        {
            _ = Task.Run(async () => { await this._playerJoin.InvokeAsync(eventArgs); });

            return Task.CompletedTask;
        }

        internal Task InvokePlayerLeaveAsync(PlayerLeaveEventArgs eventArgs)
        {
            _ = Task.Run(async () => { await this._playerLeave.InvokeAsync(eventArgs); });

            return Task.CompletedTask;
        }

        internal Task InvokeServerTickAsync()
        {
            _ = Task.Run(async () => { await this._serverTick.InvokeAsync(); });
            return Task.CompletedTask;
        }
    }
}