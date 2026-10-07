using Microsoft.Extensions.Logging;
using Obsidian.Integrated;
using Obsidian.Net.Packets;
using Obsidian.Net.Packets.Common;
using Obsidian.Net.Packets.Configuration.Clientbound;
using Obsidian.Net.Packets.Login.Serverbound;
using Obsidian.WorldData;
using System.Threading;

namespace Obsidian.Net.ClientHandlers;
internal sealed partial class LoginClientHandler : ClientHandler
{
    // How long the local player has to answer the join query.
    private static readonly TimeSpan JoinQueryTimeout = TimeSpan.FromSeconds(10);

    // The id of the join query waiting for its answer, or 0 for none.
    private int pendingJoinQuery;

    public async override ValueTask<bool> HandleAsync(PacketData packetData)
    {
        var (id, buffer) = packetData;

        switch (id)
        {
            case 0x00:
                {
                    if (await this.Server.ShouldThrottleAsync(this.Client))
                        return false;

                    try
                    {
                        await this.HandleLoginStartAsync(buffer.GetBuffer());
                    }
                    catch
                    {
                        return false;
                    }

                    return true;
                }
            case 0x01:
                {
                    try
                    {
                        await this.HandleEncryptionResponseAsync(buffer.GetBuffer());
                    }
                    catch
                    {
                        return false;
                    }

                    return true;
                }
            case 0x02:
                {
                    try
                    {
                        await this.HandleCustomQueryAnswerAsync(buffer.GetBuffer());
                    }
                    catch
                    {
                        return false;
                    }

                    return true;
                }
            case 0x03:
                {
                    this.Client.SetState(ClientState.Configuration);

                    this.Configure();

                    return true;
                }
            default:
                Log.UnknownPacket(this.Logger, id);
                await this.Client.DisconnectAsync("Unknown Packet Id.");
                break;
        }

        return false;
    }

    private void Configure()
    {
        this.SendPacket(new SelectKnownPacksPacket
        {
            KnownPacks = [new() { Id = "core", Version = ServerConstants.ProtocolDescription, Namespace = "minecraft" }]
        });

        //This is very inconvenient
        this.SendPacket(new RegistryDataPacket(CodecRegistry.Dialog.CodecKey,
            CodecRegistry.Dialog.All.ToDictionary(x => x.Key, x => (ICodec)x.Value)));
        this.SendPacket(new RegistryDataPacket(CodecRegistry.Dimensions.CodecKey,
            CodecRegistry.Dimensions.All.ToDictionary(x => x.Key, x => (ICodec)x.Value)));
        this.SendPacket(new RegistryDataPacket(CodecRegistry.Biomes.CodecKey,
            CodecRegistry.Biomes.All.ToDictionary(x => x.Key, x => (ICodec)x.Value)));
        this.SendPacket(new RegistryDataPacket(CodecRegistry.ChatType.CodecKey,
            CodecRegistry.ChatType.All.ToDictionary(x => x.Key, x => (ICodec)x.Value)));
        this.SendPacket(new RegistryDataPacket(CodecRegistry.DamageType.CodecKey,
            CodecRegistry.DamageType.All.ToDictionary(x => x.Key, x => (ICodec)x.Value)));
        this.SendPacket(new RegistryDataPacket(CodecRegistry.TrimPattern.CodecKey,
            CodecRegistry.TrimPattern.All.ToDictionary(x => x.Key, x => (ICodec)x.Value)));
        this.SendPacket(new RegistryDataPacket(CodecRegistry.TrimMaterial.CodecKey,
            CodecRegistry.TrimMaterial.All.ToDictionary(x => x.Key, x => (ICodec)x.Value)));

        this.SendPacket(new RegistryDataPacket(CodecRegistry.CatVariant.CodecKey,
            CodecRegistry.CatVariant.All.ToDictionary(x => x.Key, x => (ICodec)x.Value)));
        this.SendPacket(new RegistryDataPacket(CodecRegistry.ChickenVariant.CodecKey,
            CodecRegistry.ChickenVariant.All.ToDictionary(x => x.Key, x => (ICodec)x.Value)));
        this.SendPacket(new RegistryDataPacket(CodecRegistry.CowVariant.CodecKey,
            CodecRegistry.CowVariant.All.ToDictionary(x => x.Key, x => (ICodec)x.Value)));
        this.SendPacket(new RegistryDataPacket(CodecRegistry.FrogVariant.CodecKey,
            CodecRegistry.FrogVariant.All.ToDictionary(x => x.Key, x => (ICodec)x.Value)));
        this.SendPacket(new RegistryDataPacket(CodecRegistry.PigVariant.CodecKey,
            CodecRegistry.PigVariant.All.ToDictionary(x => x.Key, x => (ICodec)x.Value)));
        this.SendPacket(new RegistryDataPacket(CodecRegistry.ZombieNautilusVariant.CodecKey,
            CodecRegistry.ZombieNautilusVariant.All.ToDictionary(x => x.Key, x => (ICodec)x.Value)));

        //Figure out why sending all the wolf variants throw a network protocol error
        this.SendPacket(new RegistryDataPacket(CodecRegistry.WolfVariant.CodecKey, new Dictionary<string, ICodec>()
        {
            { CodecRegistry.WolfVariant.Black.Name, CodecRegistry.WolfVariant.Black },
        }));
        this.SendPacket(new RegistryDataPacket(CodecRegistry.WolfSoundVariant.CodecKey,
            CodecRegistry.WolfSoundVariant.All.ToDictionary(x => x.Key, x => (ICodec)x.Value)));
        this.SendPacket(new RegistryDataPacket(CodecRegistry.PaintingVariant.CodecKey,
            CodecRegistry.PaintingVariant.All.ToDictionary(x => x.Key, x => (ICodec)x.Value)));

        // Item components refer to these by network id (the index of the entry sent here), e.g. an item's enchantments.
        this.SendPacket(new RegistryDataPacket("minecraft:enchantment", EnchantmentsRegistry.All.Select(enchantment => enchantment.Identifier)));
        this.SendPacket(new RegistryDataPacket("minecraft:instrument", InstrumentsRegistry.All.Select(instrument => instrument.Identifier)));

        this.SendPacket(UpdateTagsPacket.ClientboundConfiguration with { Tags = TagsRegistry.Categories });

        this.SendPacket(FinishConfigurationPacket.Default);
    }

    private async Task HandleLoginStartAsync(byte[] data)
    {
        var loginStart = HelloPacket.Deserialize(data);

        var username = this.Server.Configuration.Network.MulitplayerDebugMode ? $"Player{Globals.Random.Next(1, 999)}" : loginStart.Username;
        var world = this.Server.DefaultWorld;

        Log.LoginRequest(this.Logger, username);

        // On an integrated server, the local player's name is reserved for the client that knows the join secret, and
        // nobody else may join until the world is opened to LAN.
        if (this.Server.Integrated is IntegratedSession integrated)
        {
            if (integrated.IsLocalPlayer(username))
            {
                this.SendJoinQuery();
                return;
            }

            if (!integrated.IsPublished)
            {
                await this.Client.DisconnectAsync("This world isn't open to LAN.");
                return;
            }
        }

        await this.Server.DisconnectPlayerIfConnectedAsync(username);

        if (this.Server.Configuration.OnlineMode && await this.Client.TrySetCachedProfileAsync(username))
        {
            this.Client.Initialize(world);

            return;
        }

        if (this.Server.Configuration.Whitelist && !this.Server.IsWhitelisted(username))
        {
            await this.Client.DisconnectAsync("You are not whitelisted on this server\nContact server administrator");
        }
        else
        {
            this.Client.InitializeOffline(username, world);
        }
    }

    /// <summary>
    /// Asks the client claiming to be the integrated server's local player for the join secret, and disconnects it if
    /// the answer doesn't come in time.
    /// </summary>
    private void SendJoinQuery()
    {
        var queryId = Globals.Random.Next(1, int.MaxValue);
        Volatile.Write(ref this.pendingJoinQuery, queryId);

        this.SendPacket(new Packets.Login.Clientbound.CustomQueryPacket
        {
            MessageId = queryId,
            Channel = IntegratedSession.JoinQueryChannel,
            Payload = ReadOnlyMemory<byte>.Empty
        });

        _ = Task.Delay(JoinQueryTimeout).ContinueWith(async _ =>
        {
            if (Interlocked.CompareExchange(ref this.pendingJoinQuery, 0, queryId) == queryId)
                await this.Client.DisconnectAsync("Timed out joining the world.");
        }, TaskScheduler.Default);
    }

    /// <summary>
    /// Logs the local player in once they answered the join query with the right secret: with the configured UUID and
    /// without Mojang authentication, even in online mode. Any other answer disconnects them.
    /// </summary>
    private async Task HandleCustomQueryAnswerAsync(byte[] data)
    {
        var answer = CustomQueryAnswerPacket.Deserialize(data);
        var integrated = this.Server.Integrated;

        var expected = Interlocked.Exchange(ref this.pendingJoinQuery, 0);
        if (integrated is null || expected == 0 || answer.MessageId != expected)
        {
            await this.Client.DisconnectAsync("Unexpected login query answer.");
            return;
        }

        if (!answer.Successful || !integrated.VerifyJoinSecret(answer.Data.Span))
        {
            Log.JoinSecretRejected(this.Logger);
            await this.Client.DisconnectAsync("Only the world's owner can join as this player.");
            return;
        }

        var name = integrated.Configuration.LocalPlayerName!;

        // Like vanilla's integrated player list, a second connection as the local player is refused.
        if (this.Server.IsPlayerOnline(name))
        {
            await this.Client.DisconnectAsync(new ChatMessage { Translate = "multiplayer.disconnect.name_taken" });
            return;
        }

        this.Client.InitializeOffline(name, this.Server.DefaultWorld, integrated.Configuration.LocalPlayerUuid);
    }

    private async Task HandleEncryptionResponseAsync(byte[] data)
    {
        this.Client.ThrowIfInvalidEncryptionRequest();

        // Decrypt the shared secret and verify the token
        await KeyPacket.Deserialize(data).HandleAsync(this.Client);
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Warning, Message = "Disconnecting client that sent unknown login packet {PacketId}")]
        public static partial void UnknownPacket(ILogger logger, int packetId);

        [LoggerMessage(Level = LogLevel.Debug, Message = "Login request from {Username}")]
        public static partial void LoginRequest(ILogger logger, string username);

        [LoggerMessage(Level = LogLevel.Warning, Message = "Rejected a login as the local player: wrong or missing join secret")]
        public static partial void JoinSecretRejected(ILogger logger);
    }
}
