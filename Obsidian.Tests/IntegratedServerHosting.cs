using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Obsidian.Hosting;
using Obsidian.Integrated;
using Obsidian.Nbt;
using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Xunit;

namespace Obsidian.Tests;

/// <summary>
/// Integrated servers hosted in the test process, in a fresh temporary root, with a flat world so they start quickly.
/// </summary>
[Collection(ServerPathsCollection.Name)]
public sealed class IntegratedServerHosting : IDisposable
{
    private const string LocalPlayerName = "Dev";
    private const string JoinSecret = "secret";

    private static readonly Guid LocalPlayerUuid = Guid.Parse("0f3c2b1a-1111-2222-3333-444455556666");

    // Mojang's structure templates, shared with the other tests that need them.
    private static readonly string SharedCache = Path.Combine(Path.GetTempPath(), "obsidian-tests", "cache");

    private readonly string root = Path.Combine(Path.GetTempPath(), $"obsidian-integrated-{Guid.NewGuid():N}");

    [Fact]
    public void PathsResolveUnderTheConfiguredRoot()
    {
        var cache = Path.Combine(this.root, "shared-cache");
        ServerConstants.ConfigurePaths(Configure(new()
        {
            ["Paths:Root"] = this.root,
            ["Paths:Cache"] = cache
        }));

        Assert.Equal(this.root, ServerConstants.RootPath);
        Assert.Equal(cache, ServerConstants.VanillaCachePath);
        Assert.Equal(Path.Combine(this.root, "logs"), ServerConstants.LogsPath);
        Assert.Equal(Path.Combine(this.root, "config"), ServerConstants.ConfigPath);
        Assert.Equal(Path.Combine(this.root, "worlds"), ServerConstants.WorldsPath);
        Assert.Equal(Path.Combine(this.root, "persistentdata"), ServerConstants.PersistentDataPath);
        Assert.Equal(Path.Combine(this.root, "plugins"), ServerConstants.PluginsPath);
    }

    /// <summary>
    /// Creates a world in a vanilla save folder, stops, and starts again: the save has vanilla's layout and level.dat,
    /// what Obsidian doesn't model in level.dat survives its saves, and only the local player with the join secret gets
    /// in until the world is opened to LAN.
    /// </summary>
    [Fact]
    public async Task VanillaWorldRoundTripsAndOnlyTheOwnerJoins()
    {
        var world = Path.Combine(this.root, "saves", "Test World");

        var firstRun = await this.RunUntilReadyAsync(world, (_, _) => Task.CompletedTask);

        Assert.Contains(firstRun, line => line.StartsWith("@obsidian:{\"event\":\"progress\"", StringComparison.Ordinal));
        Assert.Contains("@obsidian:{\"event\":\"stopping\"}", firstRun);

        var levelDat = Path.Combine(world, "level.dat");
        var data = ReadData(levelDat);
        Assert.Equal(4671, data.GetInt("DataVersion"));
        Assert.Equal("Test World", data.GetString("LevelName"));
        Assert.Equal(1, data.GetInt("GameType"));
        Assert.True(data.GetBool("allowCommands"));
        Assert.True(data.TryGetTag<NbtCompound>("Version", out var version) && version.GetString("Name") == "1.21.11");
        Assert.True(data.TryGetTag<NbtCompound>("spawn", out _));
        Assert.True(data.TryGetTag<NbtCompound>("game_rules", out var gameRules) && gameRules.GetBool("minecraft:keep_inventory"));
        Assert.True(data.TryGetTag<NbtCompound>("WorldGenSettings", out var worldGen));
        Assert.Equal(JavaHash("hello"), worldGen.GetLong("seed"));

        Assert.True(Directory.Exists(Path.Combine(world, "region")));
        Assert.True(Directory.Exists(Path.Combine(world, "DIM-1", "region")));
        Assert.True(Directory.Exists(Path.Combine(world, "DIM1", "region")));
        Assert.False(Directory.Exists(Path.Combine(world, "regions")));
        Assert.True(File.Exists(Path.Combine(world, "session.lock")));
        Assert.True(Directory.Exists(Path.Combine(this.root, "logs")));

        // A tag Obsidian doesn't model, like vanilla's dragon fight.
        data.Add(new NbtCompound("DragonFight") { new NbtTag<byte>("DragonKilled", 1) });
        WriteData(levelDat, data);

        var secondRun = await this.RunUntilReadyAsync(world, CheckLoginsAsync);

        Assert.Contains(secondRun, line => line.StartsWith("@obsidian:{\"event\":\"published\"", StringComparison.Ordinal));

        var saved = ReadData(levelDat);
        Assert.True(saved.TryGetTag<NbtCompound>("DragonFight", out var dragonFight) && dragonFight.GetBool("DragonKilled"));
        Assert.True(saved.TryGetTag<NbtCompound>("WorldGenSettings", out var savedWorldGen));
        Assert.Equal(worldGen.GetLong("seed"), savedWorldGen.GetLong("seed"));
        Assert.True(File.Exists(Path.Combine(world, "level.dat_old")));
    }

    /// <summary>
    /// A stop while a new world generates ends the generation promptly and stops gracefully, without writing level.dat,
    /// so the unfinished world isn't taken for a complete one.
    /// </summary>
    [Fact]
    public async Task StopWhileGeneratingLeavesNoLevelData()
    {
        var world = Path.Combine(this.root, "saves", "Unfinished");

        // Far more chunks than the test waits for.
        using var server = this.StartServer(world, "--PregenerateChunkRange=64");

        await server.WaitForEventAsync(line => line.Contains("\"stage\":\"generating\"", StringComparison.Ordinal));

        server.Input.Send("@obsidian:{\"command\":\"stop\"}");
        await server.Run.WaitAsync(TimeSpan.FromSeconds(30));

        var events = server.DrainEvents();
        Assert.Contains("@obsidian:{\"event\":\"stopping\"}", events);
        Assert.DoesNotContain(events, line => line.Contains("\"event\":\"crashed\"", StringComparison.Ordinal));
        Assert.False(File.Exists(Path.Combine(world, "level.dat")));
    }

    /// <summary>
    /// Logs in over the loopback listener: a stranger is turned away before the world is open to LAN, the local player
    /// is asked for the join secret, and only the right secret logs them in with their own UUID.
    /// </summary>
    private static async Task CheckLoginsAsync(int port, Action<string> sendCommand)
    {
        using (var stranger = await LoginConnection.StartAsync(port, "Steve"))
            Assert.True(await stranger.IsRejectedAsync());

        using (var impostor = await LoginConnection.StartAsync(port, LocalPlayerName))
        {
            var query = await impostor.ReadPacketAsync();
            Assert.Equal(LoginConnection.CustomQuery, query.Id);

            await impostor.AnswerQueryAsync(query, "wrong");
            Assert.True(await impostor.IsRejectedAsync());
        }

        using (var owner = await LoginConnection.StartAsync(port, LocalPlayerName))
        {
            var query = await owner.ReadPacketAsync();
            Assert.Equal(LoginConnection.CustomQuery, query.Id);

            await owner.AnswerQueryAsync(query, JoinSecret);

            var finished = await owner.ReadPacketAsync();
            Assert.Equal(LoginConnection.LoginFinished, finished.Id);
            Assert.Equal(LocalPlayerUuid, new Guid(finished.Data.AsSpan(0, 16), bigEndian: true));
        }

        sendCommand("@obsidian:{\"command\":\"publish\",\"port\":0,\"gameMode\":\"survival\",\"allowCommands\":false}");
    }

    /// <summary>
    /// Starts an integrated server on <paramref name="world"/>, waits until it's ready, runs <paramref name="whileReady"/>
    /// with its port and a way to send control lines, and stops it through the control channel.
    /// </summary>
    /// <returns>Every event line the server wrote.</returns>
    private async Task<List<string>> RunUntilReadyAsync(string world, Func<int, Action<string>, Task> whileReady)
    {
        using var server = this.StartServer(world);

        var ready = await server.WaitForEventAsync(line => line.StartsWith("@obsidian:{\"event\":\"ready\"", StringComparison.Ordinal));

        int port;
        using (var readyEvent = JsonDocument.Parse(ready["@obsidian:".Length..]))
            port = readyEvent.RootElement.GetProperty("port").GetInt32();

        Assert.True(port > 0);

        await whileReady(port, server.Input.Send);

        server.Input.Send("@obsidian:{\"command\":\"stop\"}");
        await server.Run.WaitAsync(TimeSpan.FromMinutes(1));

        return server.DrainEvents();
    }

    /// <summary>
    /// Starts an integrated server on <paramref name="world"/> with a small flat world; <paramref name="extraArgs"/>
    /// override its settings.
    /// </summary>
    private RunningServer StartServer(string world, params string[] extraArgs)
    {
        string[] args =
        [
            $"--Paths:Root={this.root}",
            $"--Paths:Cache={SharedCache}",
            "--Integrated:Enabled=true",
            $"--Integrated:WorldPath={world}",
            $"--Integrated:LocalPlayerName={LocalPlayerName}",
            $"--Integrated:LocalPlayerUuid={LocalPlayerUuid}",
            $"--Integrated:JoinSecret={JoinSecret}",
            "--Port=0",
            "--OnlineMode=false",
            "--PregenerateChunkRange=1",
            "--SpawnChunkRadius=1",
            "--NewWorld:WorldType=flat",
            "--NewWorld:Seed=hello",
            "--NewWorld:GameMode=creative",
            "--NewWorld:AllowCommands=true",
            "--NewWorld:GameRules:keep_inventory=true",
            ..extraArgs
        ];

        var input = new LineReader();
        var output = new EventLines();

        var builder = Host.CreateApplicationBuilder(args);
        builder.Services.AddSingleton<TextReader>(input);
        builder.ConfigureObsidian();
        builder.AddObsidian();
        builder.Services.AddSingleton(new IntegratedEventWriter(output));

        var host = builder.Build();

        return new RunningServer(host, host.RunAsync(), input, output);
    }

    public void Dispose()
    {
        ServerConstants.ConfigurePaths(Configure([]));

        try
        {
            Directory.Delete(this.root, recursive: true);
        }
        catch (IOException)
        {
            // A file still held by a finishing logger; the temporary folder goes eventually.
        }
    }

    private static IConfiguration Configure(Dictionary<string, string> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    // Java's String.hashCode, which vanilla turns text seeds into.
    private static long JavaHash(string text)
    {
        var hash = 0;
        foreach (var character in text)
            hash = unchecked(31 * hash + character);

        return hash;
    }

    private static NbtCompound ReadData(string path)
    {
        using var file = File.OpenRead(path);
        var root = (NbtCompound)new NbtReader(file, NbtCompression.GZip).ReadNextTag()!;

        return (NbtCompound)root["Data"];
    }

    private static void WriteData(string path, NbtCompound data)
    {
        using var file = File.Create(path);
        using var writer = new NbtWriterStream(file, NbtCompression.GZip, "");

        writer.WriteTag(data);
        writer.EndCompound();
        writer.TryFinish();
    }

    /// <summary>
    /// A server running in the test process, with its standard input and the event lines it wrote so far.
    /// </summary>
    private sealed class RunningServer(IHost host, Task run, LineReader input, EventLines output) : IDisposable
    {
        private readonly List<string> events = [];

        public Task Run { get; } = run;

        public LineReader Input { get; } = input;

        /// <summary>
        /// Reads events until one matches <paramref name="match"/>, failing on a crash.
        /// </summary>
        /// <returns>The matching event line.</returns>
        public async Task<string> WaitForEventAsync(Func<string, bool> match)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            while (true)
            {
                var line = await output.Lines.Reader.ReadAsync(timeout.Token);
                this.events.Add(line);

                Assert.DoesNotContain("\"event\":\"crashed\"", line);

                if (match(line))
                    return line;
            }
        }

        /// <summary>
        /// Every event line the server wrote.
        /// </summary>
        public List<string> DrainEvents()
        {
            while (output.Lines.Reader.TryRead(out var line))
                this.events.Add(line);

            return this.events;
        }

        public void Dispose()
        {
            host.Dispose();
            this.Input.Dispose();
        }
    }

    /// <summary>
    /// Standard input for the server: lines sent by the test, ending when it's disposed.
    /// </summary>
    private sealed class LineReader : TextReader
    {
        private readonly BlockingCollection<string> lines = [];

        public void Send(string line) => this.lines.Add(line);

        public override string ReadLine() => this.lines.TryTake(out var line, Timeout.Infinite) ? line : null;

        protected override void Dispose(bool disposing)
        {
            this.lines.CompleteAdding();
            base.Dispose(disposing);
        }
    }

    /// <summary>
    /// Standard output of the server, split into lines.
    /// </summary>
    private sealed class EventLines : TextWriter
    {
        private readonly StringBuilder line = new();

        public Channel<string> Lines { get; } = Channel.CreateUnbounded<string>();

        public override Encoding Encoding => Encoding.UTF8;

        public override void Write(char value)
        {
            if (value != '\n')
            {
                this.line.Append(value);
                return;
            }

            this.Lines.Writer.TryWrite(this.line.ToString());
            this.line.Clear();
        }
    }

    /// <summary>
    /// A client connection in the login state, speaking the uncompressed, unencrypted protocol.
    /// </summary>
    private sealed class LoginConnection : IDisposable
    {
        // Login packet ids of 1.21.11.
        public const int Disconnect = 0x00;
        public const int LoginFinished = 0x02;
        public const int CustomQuery = 0x04;
        private const int Hello = 0x00;
        private const int CustomQueryAnswer = 0x02;

        private readonly TcpClient client;
        private readonly NetworkStream stream;

        private LoginConnection(TcpClient client)
        {
            this.client = client;
            this.stream = client.GetStream();
            this.stream.ReadTimeout = 15_000;
        }

        /// <summary>
        /// Connects, and sends the handshake and the login hello for <paramref name="name"/>.
        /// </summary>
        public static async Task<LoginConnection> StartAsync(int port, string name)
        {
            var client = new TcpClient();
            await client.ConnectAsync("127.0.0.1", port);
            var connection = new LoginConnection(client);

            var handshake = new MemoryStream();
            WriteVarInt(handshake, (int)ServerConstants.DefaultProtocol);
            WriteString(handshake, "127.0.0.1");
            handshake.Write([(byte)(port >> 8), (byte)port]);
            WriteVarInt(handshake, 2);
            connection.Send(0x00, handshake.ToArray());

            var hello = new MemoryStream();
            WriteString(hello, name);
            hello.Write(Guid.Empty.ToByteArray(bigEndian: true));
            connection.Send(Hello, hello.ToArray());

            return connection;
        }

        public Task AnswerQueryAsync((int Id, byte[] Data) query, string secret)
        {
            var queryId = ReadVarInt(new MemoryStream(query.Data));

            var answer = new MemoryStream();
            WriteVarInt(answer, queryId);
            answer.WriteByte(1);
            answer.Write(Encoding.UTF8.GetBytes(secret));
            this.Send(CustomQueryAnswer, answer.ToArray());

            return Task.CompletedTask;
        }

        /// <summary>
        /// Whether the server turns the login away: it disconnects, with or without a disconnect packet.
        /// </summary>
        public async Task<bool> IsRejectedAsync()
        {
            try
            {
                return (await this.ReadPacketAsync()).Id == Disconnect;
            }
            catch (Exception ex) when (ex is EndOfStreamException or IOException)
            {
                return true;
            }
        }

        public async Task<(int Id, byte[] Data)> ReadPacketAsync()
        {
            var length = ReadVarInt(this.stream);
            var packet = new byte[length];
            await this.stream.ReadExactlyAsync(packet);

            var body = new MemoryStream(packet);
            var id = ReadVarInt(body);

            return (id, packet[(int)body.Position..]);
        }

        private void Send(int id, byte[] data)
        {
            var body = new MemoryStream();
            WriteVarInt(body, id);
            body.Write(data);

            var frame = new MemoryStream();
            WriteVarInt(frame, (int)body.Length);
            body.WriteTo(frame);

            this.stream.Write(frame.ToArray());
        }

        public void Dispose() => this.client.Dispose();

        private static void WriteString(Stream stream, string value)
        {
            var bytes = Encoding.UTF8.GetBytes(value);
            WriteVarInt(stream, bytes.Length);
            stream.Write(bytes);
        }

        private static void WriteVarInt(Stream stream, int value)
        {
            var remaining = (uint)value;
            while (remaining >= 0x80)
            {
                stream.WriteByte((byte)(remaining | 0x80));
                remaining >>= 7;
            }

            stream.WriteByte((byte)remaining);
        }

        private static int ReadVarInt(Stream stream)
        {
            var value = 0;
            for (var shift = 0; shift < 35; shift += 7)
            {
                var next = stream.ReadByte();
                if (next < 0)
                    throw new EndOfStreamException();

                value |= (next & 0x7F) << shift;

                if ((next & 0x80) == 0)
                    return value;
            }

            throw new InvalidDataException("VarInt too long.");
        }
    }
}
