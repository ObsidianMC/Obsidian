using Microsoft.Extensions.Logging.Abstractions;
using Obsidian;
using Obsidian.Registries;
using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using WorldgenBench;

// Usage: Obsidian.WorldgenBench [serial|pipeline] [options]
//
//   serial    Generates the (2 radius)^2 chunks around a centre to full on one thread, the way a server completes chunks,
//             and prints each stage's time and allocations and hashes of the output. With the default seed and radius,
//             the hashes are checked against the recorded ones and the exit code is 1 if any differs.
//   pipeline  Generates the same chunks on every core as a staged pipeline that needs no locks, which bounds how fast the
//             server's pregeneration could be. Pin it to cores with `start /affinity <mask>` on Windows.
//
//   --dimension overworld|nether|end|all  (default all)
//   --radius N                            (default 16: 1,024 chunks)
//   --seed N                              (default 12345)
//   --cold                                serial: skip the warmup; pipeline: time the first pass rather than a second one
//   --generator 1.21.11|26.3              serial: the live generator (default) or the 26.3 pipeline (BiomeTerrain),
//                                         with its structures and 26.3 templates; it has no post-processing stage
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

if (mode is not ("serial" or "pipeline"))
{
    Console.Error.WriteLine($"Unknown mode '{mode}'. Use serial or pipeline.");
    return 2;
}

var options = BenchOptions.Parse(optionArgs);
if (mode == "pipeline" && options.Generator != BenchGeneratorKind.Legacy)
{
    // The 26.3 biome search keeps state per pipeline, which generating on several threads at once would share.
    Console.Error.WriteLine("The pipeline mode runs the live generator only.");
    return 2;
}

// Shares the tests' template cache, so neither downloads the server jar again.
using (var httpClient = new HttpClient())
{
    var cache = Path.Combine(Path.GetTempPath(), "obsidian-tests", ServerConstants.VanillaCachePath);
    StructureRegistry.Initialize(await VanillaServerJar.ExtractStructuresAsync(httpClient, cache,
        ServerConstants.LegacyVersion, NullLogger.Instance));
    var current = await VanillaServerJar.ExtractStructuresAsync(httpClient, cache, ServerConstants.ProtocolDescription,
        NullLogger.Instance);
    StructureRegistry.InitializeCurrent(() => Task.FromResult(current));
}

var failed = false;
foreach (var dimension in options.Dimensions)
{
    if (mode == "serial")
        failed |= !SerialRun.Run(dimension, options);
    else
        PipelineRun.Run(dimension, options);
}

return failed ? 1 : 0;
