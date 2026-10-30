using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Obsidian.AssetGenerator;

/// <summary>
/// A headless vanilla server that the scenario captures (<see cref="ScenarioCaptures"/>) drive tick by tick over RCON.
/// </summary>
/// <remarks>
/// The world is a void superflat (overworld and nether), so nothing but a scenario's own blocks is in it. A data pack
/// freezes the game in its load function, which runs in the first tick after every start: the game runs exactly one
/// tick after a start, then only the ticks a capture steps.
/// </remarks>
internal sealed class ScenarioServer : IAsyncDisposable
{
    public const string Overworld = "minecraft:overworld";
    public const string Nether = "minecraft:the_nether";

    // The world's generation: void superflat, so every block outside a scenario is air.
    private const string VoidLayers = """{"layers":[],"biome":"minecraft:the_void"}""";

    private static readonly TimeSpan StartTimeout = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan StopTimeout = TimeSpan.FromMinutes(1);

    private readonly VanillaServer server;
    private readonly string directory;
    private readonly int rconPort;
    private readonly string password = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
    private readonly IReadOnlyDictionary<string, BlockProperties> blocks;

    private Process? process;
    private StreamWriter? log;
    private RconClient? rcon;

    private ScenarioServer(VanillaServer server, string directory, IReadOnlyDictionary<string, BlockProperties> blocks)
    {
        this.server = server;
        this.directory = directory;
        this.blocks = blocks;
        this.rconPort = FreePort();
    }

    /// <summary>The block ids, in the block states report's order.</summary>
    public IEnumerable<string> BlockIds => this.blocks.Keys;

    /// <summary>Creates a new world in the scratch directory and starts the server on it.</summary>
    public static async Task<ScenarioServer> StartAsync(VanillaServer server)
    {
        var directory = Path.Combine(server.ScratchDirectory, "scenarios");
        if (Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);

        var blocks = BlockProperties.Read(Path.Combine(server.GeneratedDirectory, "reports", "blocks.json"));
        var scenarioServer = new ScenarioServer(server, directory, blocks);
        try
        {
            scenarioServer.WriteWorld();
            await scenarioServer.LaunchAsync();
        }
        catch
        {
            // The caller never gets the server to dispose, so a half-started one ends here.
            await scenarioServer.DisposeAsync();
            throw;
        }

        return scenarioServer;
    }

    /// <summary>Runs a command and returns its feedback.</summary>
    public async Task<string> RunAsync(string command)
    {
        var rcon = this.rcon ?? throw new InvalidOperationException("The server isn't running.");
        return await rcon.CommandAsync(command);
    }

    /// <summary>Runs a command in a dimension and fails unless vanilla reports something other than an error.</summary>
    public async Task<string> RunInAsync(string dimension, string command)
    {
        var feedback = await this.RunAsync($"execute in {dimension} run {command}");
        var failed = feedback.StartsWith("Unknown", StringComparison.Ordinal)
            || feedback.StartsWith("Incorrect", StringComparison.Ordinal)
            || feedback.Contains("<--[HERE]", StringComparison.Ordinal);
        if (failed)
            throw new InvalidOperationException($"{command}: {feedback}");

        return feedback;
    }

    /// <summary>
    /// The game time, which only advances in the ticks a capture steps and the first tick after a start.
    /// </summary>
    public async Task<long> GameTimeAsync()
    {
        var feedback = await this.RunAsync("time query gametime");
        return ReadLastNumber(feedback);
    }

    /// <summary>The time of the overworld's clock, which the day cycle follows.</summary>
    public async Task<long> ClockTimeAsync()
    {
        var feedback = await this.RunInAsync(Overworld, "time query time");
        return ReadLastNumber(feedback);
    }

    /// <summary>Runs one game tick and waits until it has run.</summary>
    public async Task StepAsync()
    {
        var before = await this.GameTimeAsync();
        await this.RunAsync("tick step 1");

        // The step runs in the server's next tick, after the command.
        while (await this.GameTimeAsync() == before)
            await Task.Yield();
    }

    /// <summary>Saves the world, stops the server and starts it again, which runs one game tick.</summary>
    public async Task RestartAsync()
    {
        await this.StopAsync();
        await this.LaunchAsync();
    }

    /// <summary>
    /// The block state at a position, as vanilla writes full states: <c>id[name=value,...]</c> with every property,
    /// sorted by name. Tries <paramref name="expected"/> first, then the ids in <paramref name="likelyIds"/>, then
    /// every block.
    /// </summary>
    public async Task<string> ReadStateAsync(string dimension, Position position, string? expected,
        IEnumerable<string> likelyIds)
    {
        if (expected is not null && await this.TestAsync(dimension, position, expected))
            return expected;

        string? id = null;
        foreach (var candidate in likelyIds.Concat(this.blocks.Keys).Distinct())
        {
            if (await this.TestAsync(dimension, position, candidate))
            {
                id = candidate;
                break;
            }
        }

        if (id is null)
            throw new InvalidOperationException($"No block matches {position} in {dimension}.");

        // Each property's value is the first that matches; the last needs no test.
        var values = new List<string>();
        foreach (var (name, options) in this.blocks[id].Properties)
        {
            var value = options[^1];
            foreach (var option in options.Take(options.Count - 1))
            {
                if (await this.TestAsync(dimension, position, $"{id}[{name}={option}]"))
                {
                    value = option;
                    break;
                }
            }

            values.Add($"{name}={value}");
        }

        return values.Count == 0 ? id : $"{id}[{string.Join(",", values)}]";
    }

    /// <summary>Stops the server, killing it if it doesn't stop cleanly.</summary>
    public async ValueTask DisposeAsync()
    {
        if (this.process is null)
            return;

        try
        {
            await this.StopAsync();
        }
        catch (Exception exception) when (exception is IOException or OperationCanceledException
            or InvalidOperationException or SocketException)
        {
            // StopAsync has still ended the process. Captures read every state before they dispose, so the failed save
            // loses nothing, and throwing here would hide the error that ended the capture.
        }
    }

    private async Task<bool> TestAsync(string dimension, Position position, string state)
    {
        var feedback = await this.RunAsync($"execute in {dimension} if block {position} {state}");
        if (feedback.StartsWith("Test passed", StringComparison.Ordinal))
            return true;

        if (feedback.StartsWith("Test failed", StringComparison.Ordinal))
            return false;

        throw new InvalidOperationException($"Testing {state} at {position}: {feedback}");
    }

    private void WriteWorld()
    {
        Directory.CreateDirectory(this.directory);
        File.WriteAllText(Path.Combine(this.directory, "eula.txt"), "eula=true\n");

        // Only reachable from this machine (RCON listens on the server's address too), no watchdog (a capture can wait
        // between ticks for as long as it likes), no pausing while nobody is online, and functions may run /tick.
        string[] properties =
        [
            "server-ip=127.0.0.1",
            "enable-rcon=true",
            $"rcon.port={this.rconPort}",
            $"rcon.password={this.password}",
            $"server-port={FreePort()}",
            "online-mode=false",
            "white-list=false",
            "level-type=minecraft\\:flat",
            $"generator-settings={VoidLayers.Replace(":", "\\:")}",
            "generate-structures=false",
            "spawn-protection=0",
            "max-tick-time=-1",
            "pause-when-empty-seconds=-1",
            "function-permission-level=4",
            "view-distance=2",
            "simulation-distance=2",
            "sync-chunk-writes=true",
            "enable-jmx-monitoring=false",
            "enable-query=false",
        ];
        File.WriteAllLines(Path.Combine(this.directory, "server.properties"), properties);

        // Found when the world is created, and enabled with it.
        var pack = Path.Combine(this.directory, "world", "datapacks", "scenarios");
        var format = this.server.DataPackFormat;
        WriteFile(pack, "pack.mcmeta", $$$"""
            {"pack": {"description": "Scenario captures", "min_format": {{{format}}}, "max_format": {{{format}}}}}
            """);
        WriteFile(pack, "data/scenarios/function/freeze.mcfunction", "tick freeze\n");
        WriteFile(pack, "data/minecraft/tags/function/load.json", """{"values": ["scenarios:freeze"]}""");

        // The nether keeps its dimension type (fast lava, evaporating water) but generates nothing, like the overworld.
        WriteFile(pack, "data/minecraft/dimension/the_nether.json", $$$"""
            {"type": "minecraft:the_nether", "generator": {"type": "minecraft:flat", "settings": {{{VoidLayers}}}}}
            """);
    }

    private static void WriteFile(string directory, string path, string text)
    {
        var file = Path.Combine(directory, path);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, text);
    }

    private async Task LaunchAsync()
    {
        this.log = new StreamWriter(Path.Combine(this.directory, "server.log"), append: true);
        var classPath = string.Join(Path.PathSeparator, this.server.ClassPath);
        var startInfo = new ProcessStartInfo(VanillaServer.JavaPath)
        {
            WorkingDirectory = this.directory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
        };
        string[] arguments = ["-Xmx2G", "-cp", classPath, "net.minecraft.server.Main", "--nogui"];
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        this.process = Process.Start(startInfo)!;
        this.process.OutputDataReceived += (_, line) => this.WriteLog(line.Data);
        this.process.ErrorDataReceived += (_, line) => this.WriteLog(line.Data);
        this.process.BeginOutputReadLine();
        this.process.BeginErrorReadLine();

        this.rcon = await this.ConnectAsync();

        // The load function freezes the game in the first tick.
        while (!(await this.RunAsync("tick query")).Contains("frozen", StringComparison.Ordinal))
            await Task.Delay(10);

        // Frozen, the server still waits for its tick rate between ticks, so a faster rate makes steps quicker.
        await this.RunAsync("tick rate 500");
    }

    private void WriteLog(string? line)
    {
        if (line is null)
            return;

        lock (this.log!)
            this.log.WriteLine(line);
    }

    private async Task<RconClient> ConnectAsync()
    {
        var deadline = Stopwatch.StartNew();
        while (true)
        {
            if (this.process!.HasExited)
                throw new InvalidOperationException($"The vanilla server exited; see {this.directory}/server.log.");

            try
            {
                return await RconClient.ConnectAsync(this.rconPort, this.password);
            }
            catch (SocketException) when (deadline.Elapsed < StartTimeout)
            {
                await Task.Delay(250);
            }
        }
    }

    /// <summary>
    /// Saves the world and stops the server. Throws if either fails, but the process has ended either way: one that
    /// doesn't stop in time is killed.
    /// </summary>
    private async Task StopAsync()
    {
        var process = this.process!;
        try
        {
            await this.RunAsync("save-all flush");
            await this.RunAsync("stop");

            using var timeout = new CancellationTokenSource(StopTimeout);
            await process.WaitForExitAsync(timeout.Token);
        }
        finally
        {
            this.rcon?.Dispose();
            this.rcon = null;

            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);

                // Also waits for the output readers, so nothing writes to the log after it closes.
                process.WaitForExit();
            }

            process.Dispose();
            this.process = null;

            lock (this.log!)
                this.log.Dispose();
        }
    }

    private static long ReadLastNumber(string text)
    {
        var digits = text.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Last(word => word.Length > 0 && char.IsAsciiDigit(word[0]));
        return long.Parse(new string(digits.TakeWhile(char.IsAsciiDigit).ToArray()));
    }

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    /// <summary>A block's properties with their values, sorted by name.</summary>
    private sealed record BlockProperties(IReadOnlyList<(string Name, IReadOnlyList<string> Values)> Properties)
    {
        public static Dictionary<string, BlockProperties> Read(string report)
        {
            using var document = JsonDocument.Parse(File.ReadAllText(report));
            var blocks = new Dictionary<string, BlockProperties>();
            foreach (var block in document.RootElement.EnumerateObject())
                blocks[block.Name] = Parse(block.Value);

            return blocks;
        }

        private static BlockProperties Parse(JsonElement block)
        {
            var properties = new List<(string, IReadOnlyList<string>)>();
            if (block.TryGetProperty("properties", out var listed))
            {
                var sorted = listed.EnumerateObject().OrderBy(property => property.Name, StringComparer.Ordinal);
                foreach (var property in sorted)
                {
                    var values = property.Value.EnumerateArray().Select(value => value.GetString()!).ToList();
                    properties.Add((property.Name, values));
                }
            }

            return new BlockProperties(properties);
        }
    }

    /// <summary>
    /// The RCON protocol: little-endian length, request id and type, then the body and two zero bytes. One request is
    /// in flight at a time, and every response the captures read fits in one packet.
    /// </summary>
    private sealed class RconClient : IDisposable
    {
        private const int CommandType = 2;
        private const int LoginType = 3;

        private readonly TcpClient client;
        private readonly NetworkStream stream;
        private int nextId;

        private RconClient(TcpClient client)
        {
            this.client = client;
            this.stream = client.GetStream();
        }

        public static async Task<RconClient> ConnectAsync(int port, string password)
        {
            var client = new TcpClient { NoDelay = true };
            await client.ConnectAsync(IPAddress.Loopback, port);

            var rcon = new RconClient(client);
            var (id, _) = await rcon.SendAsync(LoginType, password);
            if (id == -1)
                throw new InvalidOperationException("RCON refused the password.");

            return rcon;
        }

        public async Task<string> CommandAsync(string command)
        {
            var (_, body) = await this.SendAsync(CommandType, command);
            return body;
        }

        public void Dispose() => this.client.Dispose();

        private async Task<(int Id, string Body)> SendAsync(int type, string body)
        {
            var payload = Encoding.UTF8.GetBytes(body);
            var packet = new byte[14 + payload.Length];
            BinaryPrimitives.WriteInt32LittleEndian(packet, packet.Length - 4);
            BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(4), ++this.nextId);
            BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(8), type);
            payload.CopyTo(packet, 12);

            // A server that stops answering fails the capture instead of hanging it.
            using var timeout = new CancellationTokenSource(CommandTimeout);
            await this.stream.WriteAsync(packet, timeout.Token);

            var header = new byte[4];
            await this.stream.ReadExactlyAsync(header, timeout.Token);
            var response = new byte[BinaryPrimitives.ReadInt32LittleEndian(header)];
            await this.stream.ReadExactlyAsync(response, timeout.Token);

            var id = BinaryPrimitives.ReadInt32LittleEndian(response);
            var text = Encoding.UTF8.GetString(response, 8, response.Length - 10);
            return (id, text);
        }
    }
}

/// <summary>A block position.</summary>
internal readonly record struct Position(int X, int Y, int Z)
{
    public Position Offset(Position offset) => new(this.X + offset.X, this.Y + offset.Y, this.Z + offset.Z);

    public override string ToString() => $"{this.X} {this.Y} {this.Z}";
}
