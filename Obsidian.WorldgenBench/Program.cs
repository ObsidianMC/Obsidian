using Microsoft.Extensions.Logging.Abstractions;
using Obsidian;
using Obsidian.Registries;
using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using WorldgenBench;

// Usage: Obsidian.WorldgenBench [serial] [options]
//
//   serial    Generates the (2 radius)^2 chunks around a centre to full on one thread, the way a server completes chunks,
//             with the live 26.3 pipeline (structures, current templates, post-processing and initial population), and
//             prints each stage's time and allocations and hashes of the output. With the default seed and radius, the
//             hashes are checked against the recorded ones and the exit code is 1 if any differs.
//
//   --dimension overworld|nether|end|all  (default all)
//   --radius N                            (default 16: 1,024 chunks)
//   --seed N                              (default 12345)
//   --cold                                skip the warmup
//
// Each dimension is centred where a server pregenerates it for seed 12345: the overworld on its spawn chunk (6, -2), the
// nether on (0, 0) and the end on its spawn platform's chunk (6, 0). Run a Release build.
var mode = "serial";
var optionArgs = args.AsSpan();
if (args.Length > 0 && !args[0].StartsWith("--"))
{
    mode = args[0];
    optionArgs = optionArgs[1..];
}

if (mode != "serial")
{
    Console.Error.WriteLine($"Unknown mode '{mode}'. Use serial.");
    return 2;
}

var options = BenchOptions.Parse(optionArgs);

// Shares the tests' template cache, so neither downloads the server jar again.
using (var httpClient = new HttpClient())
{
    var cache = Path.Combine(Path.GetTempPath(), "obsidian-tests", ServerConstants.VanillaCachePath);
    var current = await VanillaServerJar.ExtractStructuresAsync(httpClient, cache, ServerConstants.ProtocolDescription,
        NullLogger.Instance);
    StructureRegistry.InitializeCurrent(() => Task.FromResult(current));
}

var failed = false;
foreach (var dimension in options.Dimensions)
    failed |= !SerialRun.Run(dimension, options);

return failed ? 1 : 0;
