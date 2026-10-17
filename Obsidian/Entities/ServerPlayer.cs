using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Diagnostics.CodeAnalysis;
using System.Threading;

namespace Obsidian.Entities;

/// <summary>
/// A server-side player: one without a Minecraft network connection, which plugins add through
/// <see cref="IServer.AddServerPlayerAsync"/>. It plays like any other player (see <see cref="PlayerBase"/>), but what
/// it would be shown is dropped, and nothing about it is saved.
/// </summary>
public sealed class ServerPlayer : PlayerBase
{
    // Joining, joined, or removed. A leave can finish while a join handler is awaiting; that join must roll back.
    private int lifecycle;

    internal bool CompleteJoin() => Interlocked.CompareExchange(ref this.lifecycle, 1, 0) == 0;

    internal void CancelJoin() => Interlocked.Exchange(ref this.lifecycle, 2);

    [SetsRequiredMembers]
    public ServerPlayer(Guid uuid, string username, IServer server, IWorld world)
        : base(uuid, username, Obsidian.Server.GetNextEntityId(), world)
    {
        this.Server = server;
        this.ClientInformation = this.ClientInformation with { AllowsListing = true };
    }

    public override ILogger Logger => (this.Server as Server)?.logger ?? NullLogger.Instance;

    // There is no client to receive packets, so anything the server would show this player is dropped.
    public override bool SendPacket(IClientboundPacket packet) => false;

    public override ValueTask QueuePacketAsync(IClientboundPacket packet) => default;

    internal override bool Connected => Volatile.Read(ref this.lifecycle) != 2;

    public override int Ping => 0;

    public override string? ClientIP => null;

    public override ValueTask DisconnectAsync(ChatMessage reason) => new(this.Server.RemoveServerPlayerAsync(this));

    public override ValueTask KickAsync(ChatMessage reason) => new(this.Server.RemoveServerPlayerAsync(this));

    public override ValueTask KickAsync(string reason) => new(this.Server.RemoveServerPlayerAsync(this));

    public override ValueTask SendMessageAsync(ChatMessage message, Guid sender, SecureMessageSignature messageSignature) =>
        default;

    public override ValueTask SpawnParticleAsync(ParticleData data) => default;

    // Server-side players aren't saved: they start at the spawn, and their permissions last until they're removed.
    public override Task SaveAsync() => Task.CompletedTask;

    public override Task LoadAsync(bool loadFromPersistentWorld = true)
    {
        if (!double.IsFinite(this.Position.X) || !double.IsFinite(this.Position.Y) || !double.IsFinite(this.Position.Z) || this.Position == default)
            this.Position = this.Level.LevelData.SpawnPosition;

        if (!this.Alive)
            this.Health = 20f;

        return Task.CompletedTask;
    }

    public override Task LoadPermsAsync() => Task.CompletedTask;

    public override Task SavePermsAsync() => Task.CompletedTask;
}
