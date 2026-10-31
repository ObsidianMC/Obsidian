using Microsoft.Extensions.ObjectPool;
using System.Net.Sockets;
using System.Threading;

namespace Obsidian;

public partial class Client
{
    // Socket shutdown does not join its completion callbacks. A completed operation can be preempted before
    // invoking Completed, so returning its args at disconnect lets that callback invoke the next renter's handler.
    // Keep each operation's lease through its synchronous processing or asynchronous completion handler.
    private sealed class SocketEventLease
    {
        private readonly Client owner;
        private readonly ObjectPool<SocketAsyncEventArgs> pool;
        private readonly Lock gate = new();
        private int operations;
        private bool retired;

        public SocketAsyncEventArgs Arguments { get; }

        public SocketEventLease(Client owner, ObjectPool<SocketAsyncEventArgs> pool)
        {
            this.owner = owner;
            this.pool = pool;
            this.Arguments = pool.Get();
            this.Arguments.Completed += this.OnCompleted;
        }

        public bool TryStart()
        {
            lock (this.gate)
            {
                if (this.retired)
                    return false;

                this.operations++;
                return true;
            }
        }

        public void Complete()
        {
            lock (this.gate)
            {
                this.operations--;
                if (this.retired && this.operations == 0)
                    this.Return();
            }
        }

        public void Retire()
        {
            lock (this.gate)
            {
                if (this.retired)
                    return;

                this.retired = true;
                if (this.operations == 0)
                    this.Return();
            }
        }

        private void Return()
        {
            this.Arguments.Completed -= this.OnCompleted;
            this.pool.Return(this.Arguments);
        }

        private async void OnCompleted(object? sender, SocketAsyncEventArgs args)
        {
            try
            {
                await this.owner.OnSocketCompletedAsync(args);
            }
            finally
            {
                this.Complete();
            }
        }
    }
}
