using Obsidian.AssetGenerator;
using Obsidian.AssetGenerator.Datagen;
using System.Diagnostics;

// Usage: Obsidian.AssetGenerator <minecraft version> <work directory> <assets directory>
//        Obsidian.AssetGenerator capture-worldgen <minecraft version> <work directory> <fixture>
//
// Downloads the vanilla server jar of the version, runs its data generators and the Java dumper against it, and writes
// the assets the source generators and the server read into the assets directory (replacing what's there).
//
// capture-worldgen instead re-records the vanilla worldgen and structure references in the fixture (in place) with the
// version's server; see docs/vanilla-captures.md.
var timer = Stopwatch.StartNew();
using var httpClient = new HttpClient();

if (args is ["capture-worldgen", var captureVersion, var captureWorkDirectory, var fixture])
{
    var vanilla = await VanillaServer.PrepareAsync(httpClient, captureVersion, Path.Combine(Path.GetFullPath(captureWorkDirectory), captureVersion));
    await WorldgenCaptures.RunAsync(vanilla, Path.GetFullPath(fixture));
    Console.WriteLine($"Captured the {captureVersion} worldgen references into {fixture} in {timer.Elapsed.TotalSeconds:0.0}s.");
    return 0;
}

if (args.Length != 3)
{
    Console.Error.WriteLine("Usage: Obsidian.AssetGenerator <minecraft version> <work directory> <assets directory>");
    Console.Error.WriteLine("       Obsidian.AssetGenerator capture-worldgen <minecraft version> <work directory> <fixture>");
    return 1;
}

var (version, workDirectory, assetsDirectory) = (args[0], Path.GetFullPath(args[1]), Path.GetFullPath(args[2]));

var server = await VanillaServer.PrepareAsync(httpClient, version, Path.Combine(workDirectory, version));

// Written next to the assets and moved in place once complete, so a failed run leaves the previous assets alone.
var staging = assetsDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + ".tmp";
if (Directory.Exists(staging))
    Directory.Delete(staging, recursive: true);
Directory.CreateDirectory(staging);

Console.WriteLine("Running the Java dumper...");
await JavaDumper.RunAsync(server, staging);

Console.WriteLine("Writing data generator assets...");
DatagenAssets.Write(server.GeneratedDirectory, JavaDumper.WorldgenGroupsPath(server), JavaDumper.PacketFieldsPath(server), staging);
server.CopyVersionInfo(Path.Combine(staging, "version.json"));

await FixtureDumper.DumpServerAsync(server, staging);
await ClientModels.DumpAsync(server, staging);

if (Directory.Exists(assetsDirectory))
    Directory.Delete(assetsDirectory, recursive: true);
Directory.Move(staging, assetsDirectory);

Console.WriteLine($"Generated the {version} assets in {timer.Elapsed.TotalSeconds:0.0}s.");
return 0;
