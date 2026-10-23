// Load-tests a running Obsidian server with headless bots.
//
//   dotnet run -c Release --project Obsidian.LoadTest -- [options]
//   dotnet run -c Release --project Obsidian.LoadTest -- ops <path to ops.json> [--bots N] [--name-prefix P]
//
// The server must be in offline mode with the connection throttle off (network.connectionThrottle 0) and room for the
// bots (maxPlayers). For creative bots (the default), write them into the server's config/ops.json first with the ops
// command. Turn on the server's tick statistics with the log level "Obsidian.TickStats": "Debug".
//
// Options (defaults in brackets):
//   --host [localhost]  --port [25565]  --bots [10]  --join-interval ms [250]  --duration s [120]  --report s [10]
//   --mode idle|roam|spread [roam]  --radius blocks [512]  --wander-radius blocks [16] (spread)
//   --speed blocks/s [5.6]  --altitude blocks [10]
//   --view-distance [10]  --nether-fraction 0..1 [0]  --creative true|false [true]  --name-prefix [Bot]

using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Obsidian.LoadTest;

if (args is ["ops", var opsPath, .. var opsArgs])
{
    var opsOptions = BotOptions.Parse(opsArgs);
    var operators = Enumerable.Range(0, opsOptions.Bots).Select(index => $"{opsOptions.NamePrefix}{index}").Select(name => new
    {
        username = name,
        uuid = Bot.OfflineUuid(name),
        level = 4,
        bypassesPlayerLimit = true
    });
    await File.WriteAllTextAsync(opsPath, JsonSerializer.Serialize(operators, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine($"Wrote {opsOptions.Bots} operators to {opsPath}");
    return;
}

var options = BotOptions.Parse(args);
var stats = new LoadStats();
using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(options.DurationSeconds));
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    stop.Cancel();
};

Console.WriteLine($"{options.Bots} bots to {options.Host}:{options.Port}, {options.Mode}, view distance {options.ViewDistance}, "
    + $"{options.NetherFraction:P0} to the nether, for {options.DurationSeconds} s");

var started = Stopwatch.StartNew();
var reporter = ReportAsync(stop.Token);
var bots = new List<Task>();
for (var index = 0; index < options.Bots && !stop.IsCancellationRequested; index++)
{
    bots.Add(Task.Run(() => new Bot(index, options, stats).RunAsync(stop.Token)));
    try
    {
        await Task.Delay(options.JoinIntervalMilliseconds, stop.Token);
    }
    catch (OperationCanceledException)
    {
    }
}

await Task.WhenAll(bots);
await reporter;
Console.WriteLine($"[{started.Elapsed.TotalSeconds,5:0}s] stopped: {stats.Report()}");

async Task ReportAsync(CancellationToken cancellationToken)
{
    using var timer = new PeriodicTimer(TimeSpan.FromSeconds(options.ReportSeconds));
    try
    {
        while (await timer.WaitForNextTickAsync(cancellationToken))
            Console.WriteLine($"[{started.Elapsed.TotalSeconds,5:0}s] {stats.Report()}");
    }
    catch (OperationCanceledException)
    {
    }
}

namespace Obsidian.LoadTest
{
    /// <summary>
    /// How bots move: idle at the spawn; roam around it, flying over new terrain; or spread, each settling at its own
    /// home within the radius and wandering slowly around it, like players at their bases.
    /// </summary>
    internal enum BotMode { Idle, Roam, Spread }

    /// <summary>The load test's settings, from the command line (see Program.cs).</summary>
    internal sealed record BotOptions
    {
        public string Host { get; init; } = "localhost";
        public int Port { get; init; } = 25565;
        public int Bots { get; init; } = 10;
        public int JoinIntervalMilliseconds { get; init; } = 250;
        public int DurationSeconds { get; init; } = 120;
        public int ReportSeconds { get; init; } = 10;
        public BotMode Mode { get; init; } = BotMode.Roam;
        public double Radius { get; init; } = 512;
        public double WanderRadius { get; init; } = 16;
        public double Speed { get; init; } = 5.6;
        public double Altitude { get; init; } = 10;
        public int ViewDistance { get; init; } = 10;
        public double NetherFraction { get; init; }
        public bool Creative { get; init; } = true;
        public string NamePrefix { get; init; } = "Bot";

        public static BotOptions Parse(string[] args)
        {
            var options = new BotOptions();
            for (var i = 0; i + 1 < args.Length; i += 2)
            {
                var value = args[i + 1];
                options = args[i] switch
                {
                    "--host" => options with { Host = value },
                    "--port" => options with { Port = int.Parse(value, CultureInfo.InvariantCulture) },
                    "--bots" => options with { Bots = int.Parse(value, CultureInfo.InvariantCulture) },
                    "--join-interval" => options with { JoinIntervalMilliseconds = int.Parse(value, CultureInfo.InvariantCulture) },
                    "--duration" => options with { DurationSeconds = int.Parse(value, CultureInfo.InvariantCulture) },
                    "--report" => options with { ReportSeconds = int.Parse(value, CultureInfo.InvariantCulture) },
                    "--mode" => options with { Mode = Enum.Parse<BotMode>(value, ignoreCase: true) },
                    "--radius" => options with { Radius = double.Parse(value, CultureInfo.InvariantCulture) },
                    "--wander-radius" => options with { WanderRadius = double.Parse(value, CultureInfo.InvariantCulture) },
                    "--speed" => options with { Speed = double.Parse(value, CultureInfo.InvariantCulture) },
                    "--altitude" => options with { Altitude = double.Parse(value, CultureInfo.InvariantCulture) },
                    "--view-distance" => options with { ViewDistance = int.Parse(value, CultureInfo.InvariantCulture) },
                    "--nether-fraction" => options with { NetherFraction = double.Parse(value, CultureInfo.InvariantCulture) },
                    "--creative" => options with { Creative = bool.Parse(value) },
                    "--name-prefix" => options with { NamePrefix = value },
                    var unknown => throw new ArgumentException($"Unknown option {unknown}")
                };
            }

            return options;
        }
    }
}
