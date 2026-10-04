using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace Obsidian.AssetGenerator;

/// <summary>
/// A vanilla server of one version, downloaded from Mojang with its mappings and with its data generators run.
/// </summary>
/// <param name="Directory">The version's work directory, which the server's bundler extracts its jars into.</param>
/// <param name="ClassPath">The unbundled server jar and its libraries.</param>
/// <param name="MappingsPath">Mojang's ProGuard mappings of the server (from Mojang names to obfuscated ones).</param>
internal sealed record VanillaServer(string Version, string Directory, IReadOnlyList<string> ClassPath, string MappingsPath)
{
    private const string ManifestUrl = "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json";

    // The Java dumper runs as a multi-file source program and reads bytecode with java.lang.classfile, both of which
    // need Java 25 (the LTS release with both).
    private const int RequiredJavaVersion = 25;

    /// <summary>The data generators' output (<c>data</c> and <c>reports</c>).</summary>
    public string GeneratedDirectory => Path.Combine(Directory, "generated");

    /// <summary>
    /// Downloads the server jar and mappings of <paramref name="version"/> into <paramref name="directory"/> and runs
    /// the data generators, unless an earlier run already did.
    /// </summary>
    public static async Task<VanillaServer> PrepareAsync(HttpClient httpClient, string version, string directory)
    {
        await CheckJavaVersionAsync();
        System.IO.Directory.CreateDirectory(directory);

        var jarPath = Path.Combine(directory, "server.jar");
        var mappingsPath = Path.Combine(directory, "server.txt");

        // Written once the data generators finished, so an interrupted run starts over.
        var marker = Path.Combine(directory, "datagen.complete");
        if (!File.Exists(marker))
        {
            var downloads = await GetDownloadsAsync(httpClient, version);
            await DownloadAsync(httpClient, downloads.GetProperty("server"), jarPath);
            await DownloadAsync(httpClient, downloads.GetProperty("server_mappings"), mappingsPath);

            var generated = Path.Combine(directory, "generated");
            if (System.IO.Directory.Exists(generated))
                System.IO.Directory.Delete(generated, recursive: true);

            Console.WriteLine($"Running the {version} data generators...");
            await RunJavaAsync(directory, "-DbundlerMainClass=net.minecraft.data.Main", "-jar", "server.jar", "--all");
            await File.WriteAllTextAsync(marker, string.Empty);
        }

        return new VanillaServer(version, directory, GetClassPath(jarPath, directory), mappingsPath);
    }

    /// <summary>Runs <c>java</c> (from <c>JAVA_HOME</c> when set) in <paramref name="workingDirectory"/>.</summary>
    public static async Task RunJavaAsync(string workingDirectory, params string[] arguments)
    {
        using var process = StartJava(new ProcessStartInfo(JavaPath, arguments) { WorkingDirectory = workingDirectory });
        await process.WaitForExitAsync();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"java {string.Join(' ', arguments)} exited with code {process.ExitCode}.");
    }

    private static string JavaPath
    {
        get
        {
            var javaHome = Environment.GetEnvironmentVariable("JAVA_HOME");
            return string.IsNullOrEmpty(javaHome) ? "java" : Path.Combine(javaHome, "bin", "java");
        }
    }

    private static Process StartJava(ProcessStartInfo startInfo)
    {
        try
        {
            return Process.Start(startInfo)!;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Generating Obsidian's assets needs Java {RequiredJavaVersion} or newer, set JAVA_HOME or put java on the PATH.", ex);
        }
    }

    // `java --version` prints e.g. "openjdk 25.0.3 2026-04-21 LTS" first; Java 8 doesn't know the option and fails.
    private static async Task CheckJavaVersionAsync()
    {
        using var process = StartJava(new ProcessStartInfo(JavaPath, "--version") { RedirectStandardOutput = true, RedirectStandardError = true });
        var output = await process.StandardOutput.ReadToEndAsync();
        await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        var found = output.Split('\n')[0].Trim();
        var words = found.Split(' ');
        var major = words.Length > 1 && int.TryParse(words[1].Split('.', '-', '+')[0], out var number) ? number : 0;
        if (process.ExitCode != 0 || major < RequiredJavaVersion)
        {
            throw new InvalidOperationException(
                $"Generating Obsidian's assets needs Java {RequiredJavaVersion} or newer, but {JavaPath} is {(found.Length > 0 ? found : "older")}.");
        }
    }

    private static async Task<JsonElement> GetDownloadsAsync(HttpClient httpClient, string version)
    {
        using var manifest = JsonDocument.Parse(await httpClient.GetStringAsync(ManifestUrl));

        var versionUrl = manifest.RootElement.GetProperty("versions").EnumerateArray()
            .Where(entry => entry.GetProperty("id").ValueEquals(version))
            .Select(entry => entry.GetProperty("url").GetString())
            .FirstOrDefault()
            ?? throw new InvalidOperationException($"Mojang's version manifest has no version {version}.");

        using var versionJson = JsonDocument.Parse(await httpClient.GetStringAsync(versionUrl));
        return versionJson.RootElement.GetProperty("downloads").Clone();
    }

    // Downloads a file of a version's downloads and checks it against the SHA-1 Mojang publishes for it.
    private static async Task DownloadAsync(HttpClient httpClient, JsonElement download, string path)
    {
        var url = download.GetProperty("url").GetString()!;
        var sha1 = download.GetProperty("sha1").GetString()!;

        if (File.Exists(path) && await HashAsync(path) == sha1)
            return;

        Console.WriteLine($"Downloading {url}...");
        await using (var file = File.Create(path))
        await using (var stream = await httpClient.GetStreamAsync(url))
            await stream.CopyToAsync(file);

        if (await HashAsync(path) != sha1)
            throw new InvalidDataException($"{url} doesn't match Mojang's SHA-1 {sha1}.");
    }

    private static async Task<string> HashAsync(string path)
    {
        await using var file = File.OpenRead(path);
        return Convert.ToHexStringLower(await SHA1.HashDataAsync(file));
    }

    /// <summary>
    /// The jars the bundler extracted when running the data generators: the server and libraries it lists in
    /// <c>META-INF/versions.list</c> and <c>META-INF/libraries.list</c> as <c>sha256 \t id \t path</c>.
    /// </summary>
    private static List<string> GetClassPath(string jarPath, string directory)
    {
        using var bundle = ZipFile.OpenRead(jarPath);

        var classPath = new List<string>();
        foreach (var (list, folder) in new[] { ("versions", "versions"), ("libraries", "libraries") })
        {
            var entry = bundle.GetEntry($"META-INF/{list}.list")
                ?? throw new InvalidDataException($"{jarPath} isn't a bundler jar.");

            using var reader = new StreamReader(entry.Open());
            while (reader.ReadLine() is string line)
            {
                var fields = line.Split('\t');
                if (fields.Length == 3)
                    classPath.Add(Path.Combine(directory, folder, fields[2]));
            }
        }

        return classPath;
    }
}
