using Obsidian.AssetGenerator;
using Obsidian.AssetGenerator.Datagen;
using System.Diagnostics;

// Usage: Obsidian.AssetGenerator <minecraft version> <legacy version> <cache directory> <scratch directory>
//            <assets directory>
//        Obsidian.AssetGenerator capture-worldgen <minecraft version> <cache directory> <scratch directory>
//            <fixture directory>
//
// Downloads the vanilla server jar of the version, runs its data generators and the Java dumper against it, and writes
// the assets the source generators and the server read into the assets directory (replacing what's there). The worldgen
// assets come from the legacy version's data generators instead, into legacy-<legacy version> (temporary,
// until Phase 5 of docs/porting-26.3-plan.md; see DatagenAssets.WriteLegacy). The jars and data generator output are
// cached per version in the cache directory, which checkouts share; this checkout's runs work in the scratch directory.
//
// capture-worldgen instead re-records the vanilla worldgen references in the fixture directory (in place) with the
// version's server; see docs/vanilla-captures.md.
var timer = Stopwatch.StartNew();
using var httpClient = new HttpClient();

if (args is ["capture-worldgen", var captureVersion, var captureCache, var captureScratch, var fixture])
{
    var vanilla = await VanillaServer.PrepareAsync(httpClient, captureVersion,
        Path.Combine(Path.GetFullPath(captureCache), captureVersion),
        Path.Combine(Path.GetFullPath(captureScratch), captureVersion));
    await WorldgenCaptures.RunAsync(vanilla, Path.GetFullPath(fixture));

    var seconds = timer.Elapsed.TotalSeconds;
    Console.WriteLine($"Captured the {captureVersion} worldgen references into {fixture} in {seconds:0.0}s.");
    return 0;
}

if (args.Length != 5)
{
    Console.Error.WriteLine("Usage: Obsidian.AssetGenerator <minecraft version> <legacy version> <cache directory>");
    Console.Error.WriteLine("           <scratch directory> <assets directory>");
    Console.Error.WriteLine("       Obsidian.AssetGenerator capture-worldgen <minecraft version> <cache directory>");
    Console.Error.WriteLine("           <scratch directory> <fixture directory>");
    return 1;
}

var (version, legacyVersion) = (args[0], args[1]);
var cacheDirectory = Path.GetFullPath(args[2]);
var scratchDirectory = Path.GetFullPath(args[3]);
var assetsDirectory = Path.GetFullPath(args[4]);

var server = await VanillaServer.PrepareAsync(httpClient, version,
    Path.Combine(cacheDirectory, version), Path.Combine(scratchDirectory, version));
var legacy = await VanillaServer.PrepareAsync(httpClient, legacyVersion,
    Path.Combine(cacheDirectory, legacyVersion), Path.Combine(scratchDirectory, legacyVersion));

// Written next to the assets and moved in place once complete, so a failed run leaves the previous assets alone.
var staging = assetsDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + ".tmp";
if (Directory.Exists(staging))
    Directory.Delete(staging, recursive: true);
Directory.CreateDirectory(staging);

Console.WriteLine("Running the Java dumper...");
await JavaDumper.RunAsync(server, staging);

Console.WriteLine("Writing data generator assets...");
DatagenAssets.Write(server.GeneratedDirectory, JavaDumper.PacketFieldsPath(server), staging);
DatagenAssets.WriteLegacy(legacy.GeneratedDirectory, JavaDumper.WorldgenGroupsPath(server),
    Path.Combine(staging, $"legacy-{legacyVersion}"));
WorldgenAssets.WriteRouterData(
    Path.Combine(server.GeneratedDirectory, "data", "minecraft"),
    Path.Combine(staging, "worldgen_26_3"));
DatagenJson.CopyDirectory(
    Path.Combine(server.GeneratedDirectory, "reports", "biome_parameters", "minecraft"),
    Path.Combine(staging, "worldgen_26_3", "biome_parameters"));
DatagenJson.CopyDirectory(
    Path.Combine(server.GeneratedDirectory, "data", "minecraft", "dimension_type"),
    Path.Combine(staging, "worldgen_26_3", "dimension_type"));
server.CopyVersionInfo(Path.Combine(staging, "version.json"));

await FixtureDumper.DumpServerAsync(server, staging);
await ClientModels.DumpAsync(server, staging);

if (Directory.Exists(assetsDirectory))
    Directory.Delete(assetsDirectory, recursive: true);
Directory.Move(staging, assetsDirectory);

Console.WriteLine($"Generated the {version} assets in {timer.Elapsed.TotalSeconds:0.0}s.");
return 0;
