using Obsidian.AssetGenerator;
using Obsidian.AssetGenerator.Datagen;
using System.Diagnostics;

// Usage: Obsidian.AssetGenerator <minecraft version> <work directory> <assets directory>
//
// Downloads the vanilla server jar of the version, runs its data generators and the Java dumper against it, and writes
// the assets the source generators and the server read into the assets directory (replacing what's there).
if (args.Length != 3)
{
    Console.Error.WriteLine("Usage: Obsidian.AssetGenerator <minecraft version> <work directory> <assets directory>");
    return 1;
}

var (version, workDirectory, assetsDirectory) = (args[0], Path.GetFullPath(args[1]), Path.GetFullPath(args[2]));
var timer = Stopwatch.StartNew();

using var httpClient = new HttpClient();
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

if (Directory.Exists(assetsDirectory))
    Directory.Delete(assetsDirectory, recursive: true);
Directory.Move(staging, assetsDirectory);

Console.WriteLine($"Generated the {version} assets in {timer.Elapsed.TotalSeconds:0.0}s.");
return 0;
