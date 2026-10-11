using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Obsidian.API;
using System;
using System.Threading.Tasks;

namespace Obsidian.Tests.Fakes;

/// <summary>
/// A client without a connection, for creating players outside a server. Only its id and logger work.
/// </summary>
public sealed class FakeClient : IClient
{
    public int Id => 1;
    public string? Brand { get; set; }
    public string? Ip => null;
    public bool Connected => false;
    public long? LastKeepAliveId { get; set; }
    public IServer Server => throw new NotImplementedException();
    public ClientState State => ClientState.Play;
    public SignatureData? SignatureData { get; set; }
    public int Ping { get; set; }
    public ReadOnlyMemory<byte> RandomToken => ReadOnlyMemory<byte>.Empty;
    public IPlayer? Player => null;
    public ILogger Logger => NullLogger.Instance;

    public bool SendPacket(IClientboundPacket packet) => throw new NotImplementedException();
    public ReadOnlySpan<byte> SetSharedKeyAndDecodeVerifyToken(ReadOnlySpan<byte> sharedKey, ReadOnlySpan<byte> verifyToken) =>
        throw new NotImplementedException();
    public ValueTask DisconnectAsync(ChatMessage reason) => throw new NotImplementedException();
    public ValueTask QueuePacketAsync(IClientboundPacket packet) => throw new NotImplementedException();
    public void SetState(ClientState state) => throw new NotImplementedException();
    public ValueTask<bool> VerifyProfileAsync() => throw new NotImplementedException();
    public void Dispose() { }
}
