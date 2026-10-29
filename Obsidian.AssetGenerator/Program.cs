using Obsidian.AssetGenerator;
using Obsidian.AssetGenerator.Datagen;
using System.Diagnostics;

// Usage: Obsidian.AssetGenerator <minecraft version> <legacy version> <work directory> <assets directory>
//        Obsidian.AssetGenerator capture-worldgen <minecraft version> <work directory> <fixture>
//
// Downloads the vanilla server jar of the version, runs its data generators and the Java dumper against it, and writes
// the assets the source generators and the server read into the assets directory (replacing what's there). The loot
// and worldgen assets come from the legacy version's data generators instead, into legacy-<legacy version> (temporary,
// until Phases 4 and 5 of docs/porting-26.3-plan.md; see DatagenAssets.WriteLegacy).
//
// capture-worldgen instead re-records the vanilla worldgen and structure references in the fixture (in place) with the
// version's server; see docs/vanilla-captures.md.
var timer = Stopwatch.StartNew();
using var httpClient = new HttpClient();

if (args is ["capture-worldgen", _, _, _])
{
    Console.Error.WriteLine(WorldgenCaptures.UnavailableMessage);
    return 1;
}

if (args.Length != 4)
{
    Console.Error.WriteLine("Usage: Obsidian.AssetGenerator <minecraft version> <legacy version> <work directory> <assets directory>");
    Console.Error.WriteLine("       Obsidian.AssetGenerator capture-worldgen <minecraft version> <work directory> <fixture>");
    return 1;
}

var (version, legacyVersion, workDirectory, assetsDirectory) = (args[0], args[1], Path.GetFullPath(args[2]), Path.GetFullPath(args[3]));

var server = await VanillaServer.PrepareAsync(httpClient, version, Path.Combine(workDirectory, version));
var legacy = await VanillaServer.PrepareAsync(httpClient, legacyVersion, Path.Combine(workDirectory, legacyVersion));

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
server.CopyVersionInfo(Path.Combine(staging, "version.json"));

await FixtureDumper.DumpServerAsync(server, staging);
await ClientModels.DumpAsync(server, staging);

if (Directory.Exists(assetsDirectory))
    Directory.Delete(assetsDirectory, recursive: true);
Directory.Move(staging, assetsDirectory);

Console.WriteLine($"Generated the {version} assets in {timer.Elapsed.TotalSeconds:0.0}s.");
return 0;
