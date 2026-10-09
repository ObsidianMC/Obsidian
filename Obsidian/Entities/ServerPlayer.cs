using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Diagnostics.CodeAnalysis;

namespace Obsidian.Entities;

/// <summary>
/// A supported player implementation for server-side actors that do not have a Minecraft network connection.
/// </summary>
public sealed class ServerPlayer : Player
{
    [SetsRequiredMembers]
    public ServerPlayer(Guid uuid, string username, IServer server, IWorld world)
        : base(uuid, username, new ServerPlayerClient(server), world)
    {
        this.Server = server;
        ((ServerPlayerClient)this.Client).Attach(this);
    }

    private sealed class ServerPlayerClient(IServer server) : IClient
    {
        public int Id { get; } = Obsidian.Server.GetNextEntityId();

        public string? Brand { get; set; } = "obsidian:server-player";

        public string? Ip => null;

        public bool Connected => false;

        public long? LastKeepAliveId { get; set; }

        public IServer Server { get; } = server;

        public ClientState State { get; private set; } = ClientState.Play;

        public SignatureData? SignatureData { get; set; }

        public int Ping { get; set; }

        public ReadOnlyMemory<byte> RandomToken => ReadOnlyMemory<byte>.Empty;

        public IPlayer? Player { get; private set; }

        public ILogger Logger { get; } = NullLogger.Instance;

        internal void Attach(IPlayer player) => this.Player = player;

        public bool SendPacket(IClientboundPacket packet) => false;

        public ReadOnlySpan<byte> SetSharedKeyAndDecodeVerifyToken(ReadOnlySpan<byte> sharedKey, ReadOnlySpan<byte> verifyToken) =>
            throw new NotSupportedException("Server-side players do not negotiate network encryption.");

        public ValueTask DisconnectAsync(ChatMessage reason) => default;

        public ValueTask QueuePacketAsync(IClientboundPacket packet) => default;

        public void SetState(ClientState state) => this.State = state;

        public ValueTask<bool> VerifyProfileAsync() => ValueTask.FromResult(true);

        public void Dispose() { }
    }
}
