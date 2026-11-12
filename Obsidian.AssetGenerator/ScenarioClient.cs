using System.Buffers.Binary;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Obsidian.AssetGenerator;

/// <summary>
/// A headless offline client of a <see cref="ScenarioServer"/>, for captures that need what vanilla sends a player:
/// it logs in, answers what a vanilla client must (keep-alives, pings, teleports, chunk batches), and records the
/// clientbound packets about block and biome changes in the order they arrive.
/// </summary>
/// <remarks>
/// The game is frozen between steps, but the server still ticks: each of those ticks sends the block changes made
/// since the last and then the prediction acknowledgements, as a running server does. <see cref="SyncAsync"/> uses
/// that to wait for a tick: it uses the empty off hand with a sequence of its own, which vanilla acknowledges in the
/// first tick after it handles it, behind that tick's block changes. Those acknowledgements aren't recorded.
/// </remarks>
internal sealed class ScenarioClient : IAsyncDisposable
{
    private const int MaxFrameLength = (1 << 21) - 1;
    private const int OffHand = 1;

    // SyncAsync's sequences, apart from the ones a capture's actions give.
    private const int FirstMarker = 1_000_000;
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromMinutes(1);

    private readonly TcpClient tcpClient;
    private readonly NetworkStream stream;
    private readonly BufferedStream input;
    private readonly Lock sendLock = new();
    private readonly Lock stateLock = new();
    private readonly PacketIds ids;
    private readonly ProtocolNames names;
    private readonly CancellationTokenSource stop = new();
    private readonly string name;
    private readonly int protocol;
    private readonly int port;

    // The sequences SyncAsync sent, whose acknowledgements aren't recorded.
    private readonly HashSet<int> markers = [];
    private readonly List<RecordedPacket> recorded = [];

    // The chunks the server has sent, which are the ones it sends block changes in.
    private readonly HashSet<(int X, int Z)> chunks = [];

    private volatile int compressionThreshold = -1;
    private Task? receiving;
    private Phase phase = Phase.Login;
    private int nextMarker = FirstMarker;

    // The acknowledged sequences no wait has taken yet.
    private readonly HashSet<int> acknowledged = [];
    private bool loaded;
    private (double X, double Y, double Z) position;
    private TaskCompletionSource changed = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private enum Phase { Login, Configuration, Play }

    private ScenarioClient(TcpClient tcpClient, string name, int protocol, int port, PacketIds ids,
        ProtocolNames names)
    {
        this.tcpClient = tcpClient;
        this.stream = tcpClient.GetStream();
        this.input = new BufferedStream(this.stream, 64 * 1024);
        this.name = name;
        this.protocol = protocol;
        this.port = port;
        this.ids = ids;
        this.names = names;
    }

    /// <summary>
    /// A recorded block change packet: its kind, what it says, and for a section update its body's bytes.
    /// </summary>
    public sealed record RecordedPacket(string Kind, JsonElement Value);

    /// <summary>Connects, logs in and waits until the player is in the world and has loaded.</summary>
    public static async Task<ScenarioClient> JoinAsync(VanillaServer server, int port, string name)
    {
        var reports = Path.Combine(server.GeneratedDirectory, "reports");
        var ids = PacketIds.Read(Path.Combine(reports, "packets.json"));
        var names = ProtocolNames.Read(Path.Combine(reports, "blocks.json"), Path.Combine(reports, "registries.json"));

        var tcpClient = new TcpClient { NoDelay = true };
        await tcpClient.ConnectAsync(IPAddress.Loopback, port);
        var client = new ScenarioClient(tcpClient, name, server.ProtocolVersion, port, ids, names);
        client.Login();
        client.receiving = Task.Run(client.ReceiveLoopAsync);
        await client.WaitUntilAsync(() => client.loaded, "the player to load");
        return client;
    }

    /// <summary>
    /// Waits until the player has accepted a teleport to <paramref name="x"/>, <paramref name="y"/>, <paramref name="z"/>.
    /// </summary>
    public Task WaitForPositionAsync(double x, double y, double z) =>
        this.WaitUntilAsync(() => this.position == (x, y, z), $"a teleport to {x} {y} {z}");

    /// <summary>Waits until the server has sent the player every one of <paramref name="wanted"/>.</summary>
    public Task WaitForChunksAsync(IReadOnlyCollection<(int X, int Z)> wanted) =>
        this.WaitUntilAsync(() => wanted.All(this.chunks.Contains), "the scenario's chunks");

    /// <summary>
    /// Waits for a server tick after everything the server has handled so far, and that tick's packets.
    /// </summary>
    public async Task SyncAsync()
    {
        int sequence;
        lock (this.stateLock)
        {
            sequence = this.nextMarker++;
            this.markers.Add(sequence);
        }

        this.Send(this.ids.UseItem, writer =>
        {
            writer.WriteVarInt(OffHand);
            writer.WriteVarInt(sequence);
            writer.WriteFloat(0);
            writer.WriteFloat(0);
        });
        await this.WaitForAckAsync(sequence);
    }

    /// <summary>
    /// Uses the main hand's item on a block's face at <paramref name="cursor"/>, with a prediction sequence, and waits
    /// for vanilla's acknowledgement of it.
    /// </summary>
    public async Task UseItemOnAsync(int x, int y, int z, int face, (float X, float Y, float Z) cursor, int sequence)
    {
        this.Send(this.ids.UseItemOn, writer =>
        {
            writer.WriteVarInt(0);
            writer.WriteLong(PackPosition(x, y, z));
            writer.WriteVarInt(face);
            writer.WriteFloat(cursor.X);
            writer.WriteFloat(cursor.Y);
            writer.WriteFloat(cursor.Z);
            writer.WriteBoolean(false);
            writer.WriteBoolean(false);
            writer.WriteVarInt(sequence);
        });
        await this.WaitForAckAsync(sequence);
    }

    /// <summary>
    /// Starts breaking a block (a creative player breaks it at once), with a prediction sequence, and waits for its
    /// acknowledgement.
    /// </summary>
    public async Task StartBreakingAsync(int x, int y, int z, int face, int sequence)
    {
        this.Send(this.ids.PlayerAction, writer =>
        {
            writer.WriteVarInt(0);
            writer.WriteLong(PackPosition(x, y, z));
            writer.WriteByte((byte)face);
            writer.WriteVarInt(sequence);
        });
        await this.WaitForAckAsync(sequence);
    }

    /// <summary>The block change packets recorded since the last call, which are forgotten.</summary>
    public List<RecordedPacket> TakeRecorded()
    {
        lock (this.stateLock)
        {
            var taken = this.recorded.ToList();
            this.recorded.Clear();
            return taken;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await this.stop.CancelAsync();
        this.tcpClient.Dispose();
        if (this.receiving is not null)
        {
            try
            {
                await this.receiving;
            }
            catch (Exception exception) when (exception is IOException or ObjectDisposedException
                or OperationCanceledException or SocketException or EndOfStreamException)
            {
                // Closed.
            }
        }

        this.input.Dispose();
        this.stop.Dispose();
    }

    // Vanilla acknowledges the highest sequence of a tick; the client never has two outstanding at once.
    private Task WaitForAckAsync(int sequence) =>
        this.WaitUntilAsync(() => this.acknowledged.Remove(sequence), $"the acknowledgement of sequence {sequence}");

    private async Task WaitUntilAsync(Func<bool> condition, string what)
    {
        using var timeout = new CancellationTokenSource(WaitTimeout);
        while (true)
        {
            Task next;
            lock (this.stateLock)
            {
                if (condition())
                    return;

                next = this.changed.Task;
            }

            if (this.receiving is { IsCompleted: true } ended)
                await ended;

            try
            {
                await next.WaitAsync(timeout.Token);
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested)
            {
                throw new TimeoutException($"The scenario client waited too long for {what}.");
            }
        }
    }

    // Under stateLock: wakes whatever waits for the client's state to change.
    private void Signal()
    {
        var waiting = this.changed;
        this.changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        waiting.SetResult();
    }

    private void Login()
    {
        this.Send(this.ids.Intention, writer =>
        {
            writer.WriteVarInt(this.protocol);
            writer.WriteString("127.0.0.1");
            writer.WriteUnsignedShort((ushort)this.port);
            writer.WriteVarInt(2);
        });
        this.Send(this.ids.Hello, writer =>
        {
            writer.WriteString(this.name);
            writer.WriteUuid(OfflineUuid(this.name));
        });
    }

    private async Task ReceiveLoopAsync()
    {
        try
        {
            while (!this.stop.IsCancellationRequested)
            {
                var (id, data, body) = await this.ReceiveAsync(this.stop.Token);
                this.Handle(id, data, body);
            }
        }
        finally
        {
            lock (this.stateLock)
                this.Signal();
        }
    }

    private void Handle(int id, PacketReader data, byte[] body)
    {
        switch (this.phase)
        {
            case Phase.Login:
                this.HandleLogin(this.ids.LoginClientbound.GetValueOrDefault(id), data);
                break;
            case Phase.Configuration:
                this.HandleConfiguration(this.ids.ConfigurationClientbound.GetValueOrDefault(id), data);
                break;
            case Phase.Play:
                this.HandlePlay(this.ids.PlayClientbound.GetValueOrDefault(id), data, body);
                break;
        }
    }

    private void HandleLogin(string? packet, PacketReader data)
    {
        switch (packet)
        {
            case "minecraft:login_compression":
                this.compressionThreshold = data.ReadVarInt();
                break;
            case "minecraft:login_disconnect":
                throw new InvalidOperationException($"Login refused: {data.ReadString()}");
            case "minecraft:login_finished":
                this.Send(this.ids.LoginAcknowledged);
                this.phase = Phase.Configuration;
                this.Send(this.ids.ConfigurationClientInformation, this.WriteClientInformation);
                break;
        }
    }

    private void HandleConfiguration(string? packet, PacketReader data)
    {
        switch (packet)
        {
            case "minecraft:keep_alive":
                var keepAlive = data.ReadLong();
                this.Send(this.ids.ConfigurationKeepAlive, writer => writer.WriteLong(keepAlive));
                break;
            case "minecraft:ping":
                var ping = data.ReadInt();
                this.Send(this.ids.ConfigurationPong, writer => writer.WriteInt(ping));
                break;
            case "minecraft:select_known_packs":
                // Like a vanilla client of the same version, the client knows the packs the server offers.
                var packs = new (string, string, string)[data.ReadVarInt()];
                for (var index = 0; index < packs.Length; index++)
                    packs[index] = (data.ReadString(), data.ReadString(), data.ReadString());

                this.Send(this.ids.SelectKnownPacks, writer =>
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
            case "minecraft:code_of_conduct":
                this.Send(this.ids.AcceptCodeOfConduct);
                break;
            case "minecraft:disconnect":
                throw new InvalidOperationException("Disconnected during configuration.");
            case "minecraft:finish_configuration":
                this.Send(this.ids.FinishConfiguration);
                this.phase = Phase.Play;
                break;
        }
    }

    private void HandlePlay(string? packet, PacketReader data, byte[] body)
    {
        switch (packet)
        {
            case "minecraft:keep_alive":
                var keepAlive = data.ReadLong();
                this.Send(this.ids.PlayKeepAlive, writer => writer.WriteLong(keepAlive));
                break;
            case "minecraft:ping":
                var ping = data.ReadInt();
                this.Send(this.ids.PlayPong, writer => writer.WriteInt(ping));
                break;
            case "minecraft:player_position":
                this.Teleport(data);
                break;
            case "minecraft:chunk_batch_finished":
                this.Send(this.ids.ChunkBatchReceived, writer => writer.WriteFloat(64));
                break;
            case "minecraft:level_chunk_with_light":
                var chunk = (data.ReadInt(), data.ReadInt());
                lock (this.stateLock)
                {
                    this.chunks.Add(chunk);
                    this.Signal();
                }

                break;
            case "minecraft:start_configuration":
            case "minecraft:disconnect":
                throw new InvalidOperationException($"The server sent {packet}.");
            case "minecraft:block_update":
                this.Record("block_update", this.ReadBlockUpdate(data));
                break;
            case "minecraft:section_blocks_update":
                this.Record("section_blocks_update", this.ReadSectionUpdate(data, body));
                break;
            case "minecraft:block_event":
                this.Record("block_event", this.ReadBlockEvent(data));
                break;
            case "minecraft:chunks_biomes":
                this.Record("chunks_biomes", ReadChunksBiomes(data));
                break;
            case "minecraft:block_changed_ack":
                this.Acknowledged(data.ReadVarInt());
                break;
        }
    }

    private void Acknowledged(int sequence)
    {
        lock (this.stateLock)
        {
            if (!this.markers.Contains(sequence))
            {
                var body = Json(writer => writer.WriteNumber("sequence", sequence));
                this.recorded.Add(new RecordedPacket("block_changed_ack", body));
            }

            this.acknowledged.Add(sequence);
            this.Signal();
        }
    }

    private void Record(string kind, JsonElement value)
    {
        lock (this.stateLock)
            this.recorded.Add(new RecordedPacket(kind, value));
    }

    private JsonElement ReadBlockUpdate(PacketReader data)
    {
        var position = UnpackPosition(data.ReadLong());
        var state = this.names.State(data.ReadVarInt());
        return Json(writer =>
        {
            WritePosition(writer, "pos", position);
            writer.WriteString("state", state);
        });
    }

    // The entries as vanilla lists them, and the body's bytes, which the replay compares with Obsidian's.
    private JsonElement ReadSectionUpdate(PacketReader data, byte[] body)
    {
        var section = data.ReadLong();
        var sectionX = (int)(section >> 42);
        var sectionY = (int)(section << 44 >> 44);
        var sectionZ = (int)(section << 22 >> 42);
        var count = data.ReadVarInt();
        var blocks = new List<((int X, int Y, int Z) Position, string State)>();
        for (var index = 0; index < count; index++)
        {
            var change = data.ReadVarLong();
            var x = sectionX << 4 | (int)(change >> 8 & 15);
            var y = sectionY << 4 | (int)(change & 15);
            var z = sectionZ << 4 | (int)(change >> 4 & 15);
            blocks.Add(((x, y, z), this.names.State((int)(change >>> 12))));
        }

        return Json(writer =>
        {
            WritePosition(writer, "section", (sectionX, sectionY, sectionZ));
            writer.WriteStartArray("blocks");
            foreach (var (position, state) in blocks)
            {
                writer.WriteStartObject();
                WritePosition(writer, "pos", position);
                writer.WriteString("state", state);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteString("bytes", Convert.ToHexStringLower(body));
        });
    }

    // Each chunk's position and its biome data's bytes, in the packet's order.
    private static JsonElement ReadChunksBiomes(PacketReader data)
    {
        var count = data.ReadVarInt();
        var chunks = new List<(int X, int Z, string Bytes)>();
        for (var index = 0; index < count; index++)
        {
            // ChunkPos packed as a long, z in the high half.
            var position = data.ReadLong();
            var bytes = data.ReadBytes(data.ReadVarInt());
            chunks.Add(((int)position, (int)(position >> 32), Convert.ToHexStringLower(bytes)));
        }

        return Json(writer =>
        {
            writer.WriteStartArray("chunks");
            foreach (var (x, z, bytes) in chunks)
            {
                writer.WriteStartObject();
                writer.WriteStartArray("pos");
                writer.WriteNumberValue(x);
                writer.WriteNumberValue(z);
                writer.WriteEndArray();
                writer.WriteString("bytes", bytes);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        });
    }

    private JsonElement ReadBlockEvent(PacketReader data)
    {
        var position = UnpackPosition(data.ReadLong());
        var type = data.ReadByte();
        var parameter = data.ReadByte();
        var block = this.names.Block(data.ReadVarInt());
        return Json(writer =>
        {
            WritePosition(writer, "pos", position);
            writer.WriteString("block", block);
            writer.WriteNumber("action", type);
            writer.WriteNumber("parameter", parameter);
        });
    }

    private void Teleport(PacketReader data)
    {
        var teleportId = data.ReadVarInt();
        var (x, y, z) = (data.ReadDouble(), data.ReadDouble(), data.ReadDouble());

        // The velocity, which the client doesn't use.
        data.ReadDouble();
        data.ReadDouble();
        data.ReadDouble();
        var (yaw, pitch) = (data.ReadFloat(), data.ReadFloat());
        var relative = data.ReadInt();

        bool first;
        lock (this.stateLock)
        {
            var (oldX, oldY, oldZ) = this.position;
            this.position = (
                (relative & 1) != 0 ? oldX + x : x,
                (relative & 2) != 0 ? oldY + y : y,
                (relative & 4) != 0 ? oldZ + z : z);
            first = !this.loaded;
        }

        var (newX, newY, newZ) = this.position;

        // Since 26.3, a client accepting a teleport says where it ended up, and facing which way. It sends no move of
        // its own: vanilla disconnects a client that sends two positions in one tick, and the frozen server's ticks
        // are short.
        this.Send(this.ids.AcceptTeleportation, writer =>
        {
            writer.WriteVarInt(teleportId);
            writer.WriteDouble(newX);
            writer.WriteDouble(newY);
            writer.WriteDouble(newZ);
            writer.WriteFloat(yaw);
            writer.WriteFloat(pitch);
        });

        if (first)
            this.Send(this.ids.PlayerLoaded);

        lock (this.stateLock)
        {
            this.loaded = true;
            this.Signal();
        }
    }

    private void WriteClientInformation(PacketWriter writer)
    {
        writer.WriteString("en_us");
        writer.WriteByte(2); // view distance
        writer.WriteVarInt(0); // chat shown
        writer.WriteBoolean(true); // chat colours
        writer.WriteByte(0x7F); // skin parts
        writer.WriteVarInt(1); // right-handed
        writer.WriteBoolean(false); // no text filtering
        writer.WriteBoolean(true); // listed
        writer.WriteVarInt(0); // all particles
    }

    private void Send(int id, Action<PacketWriter>? write = null)
    {
        var packet = new PacketWriter();
        packet.WriteVarInt(id);
        write?.Invoke(packet);

        var frame = this.Frame(packet.Written);
        lock (this.sendLock)
            this.stream.Write(frame);
    }

    private byte[] Frame(ReadOnlySpan<byte> packet)
    {
        var body = new PacketWriter();
        var threshold = this.compressionThreshold;
        if (threshold < 0)
        {
            body.Write(packet);
        }
        else if (packet.Length < threshold)
        {
            // A data length of 0 marks a packet below the threshold as uncompressed.
            body.WriteVarInt(0);
            body.Write(packet);
        }
        else
        {
            body.WriteVarInt(packet.Length);
            using var compressed = new MemoryStream();
            using (var zlib = new ZLibStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
                zlib.Write(packet);

            body.Write(compressed.GetBuffer().AsSpan(0, (int)compressed.Length));
        }

        var frame = new PacketWriter();
        frame.WriteVarInt(body.Written.Length);
        frame.Write(body.Written);
        return frame.Written.ToArray();
    }

    // The packet's id, a reader at its body, and the body's bytes.
    private async ValueTask<(int Id, PacketReader Data, byte[] Body)> ReceiveAsync(CancellationToken cancellationToken)
    {
        var frameLength = await this.ReadFrameLengthAsync(cancellationToken);
        if (frameLength is <= 0 or > MaxFrameLength)
            throw new InvalidDataException($"Invalid packet length {frameLength}.");

        var frame = new byte[frameLength];
        await this.input.ReadExactlyAsync(frame, cancellationToken);

        var packet = new PacketReader(this.compressionThreshold >= 0 ? Decompress(frame) : frame);
        var id = packet.ReadVarInt();
        var body = packet.Remaining();
        return (id, new PacketReader(body), body);
    }

    // A compressed frame starts with the packet's uncompressed length, or 0 when it isn't compressed.
    private static byte[] Decompress(byte[] frame)
    {
        var header = new PacketReader(frame);
        var dataLength = header.ReadVarInt();
        if (dataLength == 0)
            return frame[header.Position..];

        var data = new byte[dataLength];
        var compressed = new MemoryStream(frame, header.Position, frame.Length - header.Position);
        using var zlib = new ZLibStream(compressed, CompressionMode.Decompress);
        zlib.ReadExactly(data);
        return data;
    }

    private async ValueTask<int> ReadFrameLengthAsync(CancellationToken cancellationToken)
    {
        var buffer = new byte[1];
        var value = 0;
        for (var shift = 0; shift < 35; shift += 7)
        {
            if (await this.input.ReadAsync(buffer, cancellationToken) == 0)
                throw new EndOfStreamException("The server closed the connection.");

            value |= (buffer[0] & 0x7F) << shift;
            if ((buffer[0] & 0x80) == 0)
                return value;
        }

        throw new InvalidDataException("A packet length VarInt is too long.");
    }

    // Vanilla's offline UUID: a name-based (version 3) UUID of "OfflinePlayer:<name>".
    private static Guid OfflineUuid(string name)
    {
        var hash = MD5.HashData(Encoding.UTF8.GetBytes($"OfflinePlayer:{name}"));
        hash[6] = (byte)(hash[6] & 0x0F | 0x30);
        hash[8] = (byte)(hash[8] & 0x3F | 0x80);
        return new Guid(hash, bigEndian: true);
    }

    // Vanilla's BlockPos.asLong: 26 bits of X, then 26 of Z, then 12 of Y.
    private static long PackPosition(int x, int y, int z) =>
        ((long)x & 0x3FFFFFF) << 38 | ((long)z & 0x3FFFFFF) << 12 | ((long)y & 0xFFF);

    private static (int X, int Y, int Z) UnpackPosition(long packed) =>
        ((int)(packed >> 38), (int)(packed << 52 >> 52), (int)(packed << 26 >> 38));

    private static void WritePosition(Utf8JsonWriter writer, string property, (int X, int Y, int Z) position)
    {
        writer.WriteStartArray(property);
        writer.WriteNumberValue(position.X);
        writer.WriteNumberValue(position.Y);
        writer.WriteNumberValue(position.Z);
        writer.WriteEndArray();
    }

    private static JsonElement Json(Action<Utf8JsonWriter> write)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            write(writer);
            writer.WriteEndObject();
        }

        using var document = JsonDocument.Parse(buffer.ToArray());
        return document.RootElement.Clone();
    }

    /// <summary>Packet ids by vanilla's names, from its packets report.</summary>
    private sealed class PacketIds
    {
        public required Dictionary<int, string> LoginClientbound { get; init; }
        public required Dictionary<int, string> ConfigurationClientbound { get; init; }
        public required Dictionary<int, string> PlayClientbound { get; init; }
        public required int Intention { get; init; }
        public required int Hello { get; init; }
        public required int LoginAcknowledged { get; init; }
        public required int ConfigurationClientInformation { get; init; }
        public required int ConfigurationKeepAlive { get; init; }
        public required int ConfigurationPong { get; init; }
        public required int SelectKnownPacks { get; init; }
        public required int AcceptCodeOfConduct { get; init; }
        public required int FinishConfiguration { get; init; }
        public required int PlayKeepAlive { get; init; }
        public required int PlayPong { get; init; }
        public required int AcceptTeleportation { get; init; }
        public required int PlayerLoaded { get; init; }
        public required int ChunkBatchReceived { get; init; }
        public required int UseItem { get; init; }
        public required int UseItemOn { get; init; }
        public required int PlayerAction { get; init; }

        public static PacketIds Read(string path)
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;
            return new PacketIds
            {
                LoginClientbound = Names(root, "login", "clientbound"),
                ConfigurationClientbound = Names(root, "configuration", "clientbound"),
                PlayClientbound = Names(root, "play", "clientbound"),
                Intention = Id(root, "handshake", "intention"),
                Hello = Id(root, "login", "hello"),
                LoginAcknowledged = Id(root, "login", "login_acknowledged"),
                ConfigurationClientInformation = Id(root, "configuration", "client_information"),
                ConfigurationKeepAlive = Id(root, "configuration", "keep_alive"),
                ConfigurationPong = Id(root, "configuration", "pong"),
                SelectKnownPacks = Id(root, "configuration", "select_known_packs"),
                AcceptCodeOfConduct = Id(root, "configuration", "accept_code_of_conduct"),
                FinishConfiguration = Id(root, "configuration", "finish_configuration"),
                PlayKeepAlive = Id(root, "play", "keep_alive"),
                PlayPong = Id(root, "play", "pong"),
                AcceptTeleportation = Id(root, "play", "accept_teleportation"),
                PlayerLoaded = Id(root, "play", "player_loaded"),
                ChunkBatchReceived = Id(root, "play", "chunk_batch_received"),
                UseItem = Id(root, "play", "use_item"),
                UseItemOn = Id(root, "play", "use_item_on"),
                PlayerAction = Id(root, "play", "player_action"),
            };
        }

        private static Dictionary<int, string> Names(JsonElement root, string phase, string direction) =>
            root.GetProperty(phase).GetProperty(direction).EnumerateObject()
                .ToDictionary(packet => packet.Value.GetProperty("protocol_id").GetInt32(), packet => packet.Name);

        private static int Id(JsonElement root, string phase, string packet) =>
            root.GetProperty(phase).GetProperty("serverbound").GetProperty($"minecraft:{packet}")
                .GetProperty("protocol_id").GetInt32();
    }

    /// <summary>
    /// Block states by id, as full states (<c>id[name=value,...]</c> with every property, sorted by name), and blocks
    /// by registry id, from the blocks and registries reports.
    /// </summary>
    private sealed class ProtocolNames
    {
        private readonly Dictionary<int, string> states = [];
        private readonly Dictionary<int, string> blocks = [];

        public string State(int id) => this.states[id];

        public string Block(int id) => this.blocks[id];

        public static ProtocolNames Read(string blocksReport, string registriesReport)
        {
            var names = new ProtocolNames();
            using (var blocks = JsonDocument.Parse(File.ReadAllText(blocksReport)))
            {
                foreach (var block in blocks.RootElement.EnumerateObject())
                {
                    foreach (var state in block.Value.GetProperty("states").EnumerateArray())
                        names.states[state.GetProperty("id").GetInt32()] = FullState(block.Name, state);
                }
            }

            using var registries = JsonDocument.Parse(File.ReadAllText(registriesReport));
            var entries = registries.RootElement.GetProperty("minecraft:block").GetProperty("entries");
            foreach (var entry in entries.EnumerateObject())
                names.blocks[entry.Value.GetProperty("protocol_id").GetInt32()] = entry.Name;

            return names;
        }

        private static string FullState(string block, JsonElement state)
        {
            if (!state.TryGetProperty("properties", out var properties))
                return block;

            var sorted = properties.EnumerateObject()
                .OrderBy(property => property.Name, StringComparer.Ordinal)
                .Select(property => $"{property.Name}={property.Value.GetString()}");
            return $"{block}[{string.Join(",", sorted)}]";
        }
    }

    /// <summary>Writes the protocol's data types, big-endian.</summary>
    private sealed class PacketWriter
    {
        private byte[] buffer = new byte[64];
        private int length;

        public ReadOnlySpan<byte> Written => this.buffer.AsSpan(0, this.length);

        public void Write(ReadOnlySpan<byte> data) => data.CopyTo(this.Take(data.Length));

        public void WriteByte(byte value) => this.Take(1)[0] = value;

        public void WriteBoolean(bool value) => this.WriteByte(value ? (byte)1 : (byte)0);

        public void WriteInt(int value) => BinaryPrimitives.WriteInt32BigEndian(this.Take(4), value);

        public void WriteLong(long value) => BinaryPrimitives.WriteInt64BigEndian(this.Take(8), value);

        public void WriteFloat(float value) => BinaryPrimitives.WriteSingleBigEndian(this.Take(4), value);

        public void WriteDouble(double value) => BinaryPrimitives.WriteDoubleBigEndian(this.Take(8), value);

        public void WriteUnsignedShort(ushort value) => BinaryPrimitives.WriteUInt16BigEndian(this.Take(2), value);

        public void WriteVarInt(int value)
        {
            var unsigned = (uint)value;
            do
            {
                var next = (byte)(unsigned & 0x7F);
                unsigned >>= 7;
                this.WriteByte(unsigned != 0 ? (byte)(next | 0x80) : next);
            }
            while (unsigned != 0);
        }

        public void WriteString(string value)
        {
            var bytes = Encoding.UTF8.GetBytes(value);
            this.WriteVarInt(bytes.Length);
            this.Write(bytes);
        }

        // Java's UUID order: the most significant long first, both big-endian.
        public void WriteUuid(Guid value) => value.TryWriteBytes(this.Take(16), bigEndian: true, out _);

        private Span<byte> Take(int count)
        {
            if (this.length + count > this.buffer.Length)
                Array.Resize(ref this.buffer, Math.Max(this.buffer.Length * 2, this.length + count));

            var span = this.buffer.AsSpan(this.length, count);
            this.length += count;
            return span;
        }
    }

    /// <summary>Reads the protocol's data types, big-endian.</summary>
    private sealed class PacketReader(byte[] data)
    {
        public int Position { get; private set; }

        public byte[] Remaining() => data[this.Position..];

        public byte ReadByte() => data[this.Position++];

        public int ReadInt() => BinaryPrimitives.ReadInt32BigEndian(this.Take(4));

        public long ReadLong() => BinaryPrimitives.ReadInt64BigEndian(this.Take(8));

        public float ReadFloat() => BinaryPrimitives.ReadSingleBigEndian(this.Take(4));

        public double ReadDouble() => BinaryPrimitives.ReadDoubleBigEndian(this.Take(8));

        public int ReadVarInt() => (int)this.ReadVariable(35);

        public long ReadVarLong() => this.ReadVariable(70);

        public string ReadString() => Encoding.UTF8.GetString(this.Take(this.ReadVarInt()));

        public byte[] ReadBytes(int count) => this.Take(count).ToArray();

        private long ReadVariable(int maxBits)
        {
            var value = 0L;
            for (var shift = 0; shift < maxBits; shift += 7)
            {
                var next = this.ReadByte();
                value |= (long)(next & 0x7F) << shift;
                if ((next & 0x80) == 0)
                    return value;
            }

            throw new InvalidDataException("A variable-length number is too long.");
        }

        private ReadOnlySpan<byte> Take(int count)
        {
            var span = data.AsSpan(this.Position, count);
            this.Position += count;
            return span;
        }
    }
}
