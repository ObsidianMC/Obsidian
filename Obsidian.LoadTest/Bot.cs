using System.Collections.Frozen;
using System.Diagnostics;
using System.Net.Sockets;
using Obsidian.Registries;
using Obsidian.Utilities;

namespace Obsidian.LoadTest;

/// <summary>
/// A headless client: it logs in offline, answers what a vanilla client must (keep-alives, pings, teleports, chunk
/// batches), and once in the world moves like <see cref="BotOptions.Mode"/> says. It reads only the packets it needs and
/// skips the rest.
/// </summary>
internal sealed class Bot(int index, BotOptions options, LoadStats stats)
{
    private enum State { Login, Configuration, Play }

    private readonly Random random = new(index);
    private readonly Lock positionLock = new();
    private BotConnection? connection;
    private State state;
    private long connectedAt;
    private long playStartedAt;

    // Where the bot is, the point it roams around, and the way it's heading. Guarded by positionLock.
    private double x, y, z;
    private double homeX, homeZ, cruiseY;
    private double heading;
    private bool positioned;

    private string dimension = "overworld";
    private bool setUp;
    private bool spawnAreaLoaded;
    private int chunksReceived;
    private long chunkBatchStarted;
    private double nanosPerChunk;
    private int chunkBatches;

    public string Name { get; } = $"{options.NamePrefix}{index}";

    /// <summary>The dimension this bot moves to after joining: the first bots stay in the overworld.</summary>
    private string TargetDimension =>
        index >= options.Bots * (1 - options.NetherFraction) ? "nether" : "overworld";

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        stats.Connecting();
        this.connectedAt = Stopwatch.GetTimestamp();
        var reason = "closed";
        Task? movement = null;
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        try
        {
            this.connection = await BotConnection.ConnectAsync(options.Host, options.Port, stats, cancellationToken);
            this.Login();

            while (!cancellationToken.IsCancellationRequested)
            {
                var (id, data) = await this.connection.ReceiveAsync(cancellationToken);
                var entered = this.state != State.Play;
                if (this.Handle(id, data) is string disconnect)
                {
                    reason = disconnect;
                    break;
                }

                if (entered && this.state == State.Play)
                {
                    this.playStartedAt = Stopwatch.GetTimestamp();
                    stats.Joined(Stopwatch.GetElapsedTime(this.connectedAt));
                    movement = this.MoveAsync(stop.Token);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            reason = "stopped";
        }
        catch (Exception exception) when (exception is IOException or SocketException or EndOfStreamException or InvalidDataException or ObjectDisposedException)
        {
            reason = exception.Message;
        }
        finally
        {
            await stop.CancelAsync();
            if (movement is not null)
                await movement;

            stats.Disconnected(this.state == State.Play, reason);
            this.connection?.Dispose();
        }
    }

    private void Login()
    {
        this.Send(Ids.Handshake.Intention, writer =>
        {
            writer.WriteVarInt((int)ServerConstants.DefaultProtocol);
            writer.WriteString(options.Host);
            writer.WriteUnsignedShort((ushort)options.Port);
            writer.WriteVarInt(2); // login
        });
        this.Send(Ids.Login.Hello, writer =>
        {
            writer.WriteString(this.Name);
            writer.WriteUuid(OfflineUuid(this.Name));
        });
        this.state = State.Login;
    }

    /// <summary>The UUID offline-mode servers give a player of this name, which ops.json lists bots by.</summary>
    public static Guid OfflineUuid(string name) => GuidHelper.FromStringHash($"OfflinePlayer:{name}");

    // Handles a packet, and returns why the server disconnected the bot if it did.
    private string? Handle(int id, PacketReader data)
    {
        switch (this.state)
        {
            case State.Login:
                switch (Ids.Login.Clientbound.GetValueOrDefault(id))
                {
                    case "LoginCompression":
                        this.connection!.SetCompressionThreshold(data.ReadVarInt());
                        break;
                    case "LoginDisconnect":
                        return "login: " + data.ReadString();
                    case "LoginFinished":
                        this.Send(Ids.Login.LoginAcknowledged);
                        this.state = State.Configuration;
                        this.Send(Ids.Configuration.CustomPayload, writer =>
                        {
                            writer.WriteString("minecraft:brand");
                            writer.WriteString("vanilla");
                        });
                        this.SendClientInformation(Ids.Configuration.ClientInformation);
                        break;
                }
                break;

            case State.Configuration:
                switch (Ids.Configuration.Clientbound.GetValueOrDefault(id))
                {
                    case "KeepAlive":
                        var configurationKeepAlive = data.ReadLong();
                        this.Send(Ids.Configuration.KeepAlive, writer => writer.WriteLong(configurationKeepAlive));
                        break;
                    case "Ping":
                        var configurationPing = data.ReadInt();
                        this.Send(Ids.Configuration.Pong, writer => writer.WriteInt(configurationPing));
                        break;
                    case "SelectKnownPacks":
                        // Like a vanilla client of the same version, the bot knows the packs the server offers.
                        var packs = new (string, string, string)[data.ReadVarInt()];
                        for (var i = 0; i < packs.Length; i++)
                            packs[i] = (data.ReadString(), data.ReadString(), data.ReadString());
                        this.Send(Ids.Configuration.SelectKnownPacks, writer =>
                        {
                            writer.WriteVarInt(packs.Length);
                            foreach (var (packNamespace, packId, version) in packs)
                            {
                                writer.WriteString(packNamespace);
                                writer.WriteString(packId);
                                writer.WriteString(version);
                            }
                        });
                        break;
                    case "CodeOfConduct":
                        this.Send(Ids.Configuration.AcceptCodeOfConduct);
                        break;
                    case "Disconnect":
                        return "kicked during configuration";
                    case "FinishConfiguration":
                        this.Send(Ids.Configuration.FinishConfiguration);
                        this.state = State.Play;
                        break;
                }
                break;

            case State.Play:
                return this.HandlePlay(Ids.Play.Clientbound.GetValueOrDefault(id), data);
        }

        return null;
    }

    private string? HandlePlay(string? packet, PacketReader data)
    {
        switch (packet)
        {
            case "KeepAlive":
                var keepAlive = data.ReadLong();
                this.Send(Ids.Play.KeepAlive, writer => writer.WriteLong(keepAlive));
                break;

            case "Ping":
                var ping = data.ReadInt();
                this.Send(Ids.Play.Pong, writer => writer.WriteInt(ping));
                break;

            case "PlayerPosition":
                this.Teleport(data);
                break;

            case "Respawn":
                // Changing dimension (or respawning): the bot roams around wherever it's put next.
                lock (this.positionLock)
                    this.positioned = false;
                if (this.setUp)
                    this.dimension = this.TargetDimension;
                break;

            case "PlayerCombatKill":
                this.Send(Ids.Play.ClientCommand, writer => writer.WriteVarInt(0)); // respawn
                break;

            case "SetTime":
                stats.ObserveGameTime(this.dimension, data.ReadLong());
                break;

            case "LevelChunkWithLight":
                stats.AddChunks(1);
                this.chunksReceived++;
                break;

            case "ChunkBatchStart":
                this.chunkBatchStarted = Stopwatch.GetTimestamp();
                break;

            case "ChunkBatchFinished":
                this.FinishChunkBatch(data.ReadVarInt());
                break;

            case "StartConfiguration":
                return "server reconfigured the bot (not supported)";

            case "Disconnect":
                return "kicked";
        }

        return null;
    }

    private void Teleport(PacketReader data)
    {
        var teleportId = data.ReadVarInt();
        var (tx, ty, tz) = (data.ReadDouble(), data.ReadDouble(), data.ReadDouble());
        data.ReadDouble(); data.ReadDouble(); data.ReadDouble(); // velocity
        data.ReadFloat(); data.ReadFloat(); // rotation
        var relative = data.ReadInt();

        bool first;
        lock (this.positionLock)
        {
            this.x = (relative & 1) != 0 ? this.x + tx : tx;
            this.y = (relative & 2) != 0 ? this.y + ty : ty;
            this.z = (relative & 4) != 0 ? this.z + tz : tz;

            first = !this.positioned;
            if (first)
            {
                // The point the bot roams around: where it was put after joining or changing dimension, or for a spread
                // bot, its home somewhere within the radius of it, which it moves to once it moves (see Step).
                this.positioned = true;
                (this.homeX, this.homeZ) = (this.x, this.z);
                if (options.Mode == BotMode.Spread)
                {
                    var angle = this.random.NextDouble() * Math.Tau;
                    var distance = options.Radius * Math.Sqrt(this.random.NextDouble());
                    this.homeX += Math.Cos(angle) * distance;
                    this.homeZ += Math.Sin(angle) * distance;
                }
                this.cruiseY = this.y + options.Altitude;
                this.heading = this.random.NextDouble() * Math.Tau;
            }
        }

        this.Send(Ids.Play.AcceptTeleportation, writer => writer.WriteVarInt(teleportId));
        this.SendPosition();
        if (!first)
            return;

        this.Send(Ids.Play.PlayerLoaded);
        if (this.setUp)
            return;

        // Once, after joining: creative (as an operator) so the bot can fly and can't die, then its dimension.
        this.setUp = true;
        if (options.Creative)
        {
            this.Send(Ids.Play.ChatCommand, writer => writer.WriteString("gamemode creative"));
            this.Send(Ids.Play.PlayerAbilities, writer => writer.WriteByte(0x02)); // flying
        }

        if (this.TargetDimension != "overworld")
            this.Send(Ids.Play.ChatCommand, writer => writer.WriteString($"dimension {this.TargetDimension}"));
    }

    // Like vanilla, asks for as many chunks a tick as arrive in about 7 ms, from how fast the batches came.
    private void FinishChunkBatch(int count)
    {
        if (count > 0)
        {
            var nanos = Stopwatch.GetElapsedTime(this.chunkBatchStarted).TotalNanoseconds / count;
            this.nanosPerChunk = this.chunkBatches++ == 0 ? nanos : this.nanosPerChunk * 0.8 + nanos * 0.2;
        }

        var chunksPerTick = this.nanosPerChunk <= 0 ? 64f : (float)Math.Clamp(7_000_000 / this.nanosPerChunk, 0.01, 64);
        this.Send(Ids.Play.ChunkBatchReceived, writer => writer.WriteFloat(chunksPerTick));

        // About the area within the view distance, which a joining client waits for.
        if (!this.spawnAreaLoaded && this.chunksReceived >= Math.PI * options.ViewDistance * options.ViewDistance)
        {
            this.spawnAreaLoaded = true;
            stats.SpawnAreaLoaded(Stopwatch.GetElapsedTime(this.playStartedAt));
        }
    }

    // Each client tick: move, send the position, and end the tick, like a vanilla client.
    private async Task MoveAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(50));
        var ticks = 0;
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                ticks++;
                bool moved;
                lock (this.positionLock)
                    moved = this.positioned && this.setUp && ticks > 40 && this.Step();

                // Vanilla sends the position at least once a second while standing still.
                if (moved || ticks % 20 == 0)
                    this.SendPosition();
                this.Send(Ids.Play.ClientTickEnd);
            }
        }
        catch (Exception exception) when (exception is OperationCanceledException or IOException or SocketException or ObjectDisposedException)
        {
            // Stopping, or the connection closed, which the receive loop reports.
        }
    }

    // Moves the bot one tick's worth, under positionLock: roaming bots fly at their cruise height in a slowly turning
    // direction, turning back towards home outside the radius. Returns whether it moved.
    private bool Step()
    {
        if (options.Mode == BotMode.Idle)
            return false;

        if (Math.Abs(this.y - this.cruiseY) > 0.01)
        {
            // Rise (or sink) to the cruise height first, at most 1 block a tick.
            this.y += Math.Clamp(this.cruiseY - this.y, -1, 1);
            return true;
        }

        var (dx, dz) = (this.x - this.homeX, this.z - this.homeZ);
        var radius = options.Mode == BotMode.Spread ? options.WanderRadius : options.Radius;

        // A spread bot away from home (it starts at the spawn) flies there at 100 blocks a second: the server doesn't
        // follow a jump of thousands of blocks in one move.
        if (options.Mode == BotMode.Spread && dx * dx + dz * dz > (radius + 8) * (radius + 8))
        {
            var distance = Math.Sqrt(dx * dx + dz * dz);
            var travel = Math.Min(5, distance);
            this.x -= dx / distance * travel;
            this.z -= dz / distance * travel;
            return true;
        }

        if (dx * dx + dz * dz > radius * radius)
            this.heading = Math.Atan2(-dz, -dx) + (this.random.NextDouble() - 0.5);
        else
            this.heading += (this.random.NextDouble() - 0.5) * 0.1;

        var step = options.Speed / 20;
        this.x += Math.Cos(this.heading) * step;
        this.z += Math.Sin(this.heading) * step;
        return true;
    }

    private void SendPosition()
    {
        double px, py, pz;
        float yaw;
        lock (this.positionLock)
            (px, py, pz, yaw) = (this.x, this.y, this.z, (float)(this.heading * 180 / Math.PI - 90));

        this.Send(Ids.Play.MovePlayerPosRot, writer =>
        {
            writer.WriteDouble(px);
            writer.WriteDouble(py);
            writer.WriteDouble(pz);
            writer.WriteFloat(yaw);
            writer.WriteFloat(0);
            writer.WriteByte(0); // not on ground, no collision
        });
    }

    private void SendClientInformation(int id) => this.Send(id, writer =>
    {
        writer.WriteString("en_us");
        writer.WriteByte((byte)options.ViewDistance);
        writer.WriteVarInt(0); // chat shown
        writer.WriteBoolean(true); // chat colors
        writer.WriteByte(0x7F); // skin parts
        writer.WriteVarInt(1); // right-handed
        writer.WriteBoolean(false); // no text filtering
        writer.WriteBoolean(true); // listed
        writer.WriteVarInt(0); // all particles
    });

    private void Send(int id, Action<PacketWriter>? write = null) => this.connection!.Send(id, write);

    /// <summary>Packet ids by name, from Obsidian's tables for the protocol it's built for.</summary>
    private static class Ids
    {
        public static class Handshake
        {
            public static readonly int Intention = Id(PacketsRegistry.Handshake.ServerboundNames, "Intention");
        }

        public static class Login
        {
            public static readonly FrozenDictionary<int, string> Clientbound = PacketsRegistry.Login.ClientboundNames;
            public static readonly int Hello = Id(PacketsRegistry.Login.ServerboundNames, "Hello");
            public static readonly int LoginAcknowledged = Id(PacketsRegistry.Login.ServerboundNames, "LoginAcknowledged");
        }

        public static class Configuration
        {
            public static readonly FrozenDictionary<int, string> Clientbound = PacketsRegistry.Configuration.ClientboundNames;
            public static readonly int ClientInformation = Id(PacketsRegistry.Configuration.ServerboundNames, "ClientInformation");
            public static readonly int CustomPayload = Id(PacketsRegistry.Configuration.ServerboundNames, "CustomPayload");
            public static readonly int KeepAlive = Id(PacketsRegistry.Configuration.ServerboundNames, "KeepAlive");
            public static readonly int Pong = Id(PacketsRegistry.Configuration.ServerboundNames, "Pong");
            public static readonly int SelectKnownPacks = Id(PacketsRegistry.Configuration.ServerboundNames, "SelectKnownPacks");
            public static readonly int AcceptCodeOfConduct = Id(PacketsRegistry.Configuration.ServerboundNames, "AcceptCodeOfConduct");
            public static readonly int FinishConfiguration = Id(PacketsRegistry.Configuration.ServerboundNames, "FinishConfiguration");
        }

        public static class Play
        {
            public static readonly FrozenDictionary<int, string> Clientbound = PacketsRegistry.Play.ClientboundNames;
            public static readonly int AcceptTeleportation = Id(PacketsRegistry.Play.ServerboundNames, "AcceptTeleportation");
            public static readonly int ChatCommand = Id(PacketsRegistry.Play.ServerboundNames, "ChatCommand");
            public static readonly int ChunkBatchReceived = Id(PacketsRegistry.Play.ServerboundNames, "ChunkBatchReceived");
            public static readonly int ClientCommand = Id(PacketsRegistry.Play.ServerboundNames, "ClientCommand");
            public static readonly int ClientTickEnd = Id(PacketsRegistry.Play.ServerboundNames, "ClientTickEnd");
            public static readonly int KeepAlive = Id(PacketsRegistry.Play.ServerboundNames, "KeepAlive");
            public static readonly int MovePlayerPosRot = Id(PacketsRegistry.Play.ServerboundNames, "MovePlayerPosRot");
            public static readonly int PlayerAbilities = Id(PacketsRegistry.Play.ServerboundNames, "PlayerAbilities");
            public static readonly int PlayerLoaded = Id(PacketsRegistry.Play.ServerboundNames, "PlayerLoaded");
            public static readonly int Pong = Id(PacketsRegistry.Play.ServerboundNames, "Pong");
        }

        private static int Id(FrozenDictionary<int, string> names, string name) =>
            names.First(entry => entry.Value == name).Key;
    }
}
