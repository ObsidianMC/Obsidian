using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace Obsidian.AssetGenerator;

/// <summary>
/// A vanilla server of one version, downloaded from Mojang and with its data generators run.
/// </summary>
/// <param name="Directory">
/// The version's cached download, which the server's bundler extracts its jars into and the data generators write to.
/// Every checkout shares it, so nothing writes to it once it's prepared.
/// </param>
/// <param name="ScratchDirectory">
/// The version's directory for this checkout's runs: their working directory, intermediate files and worlds.
/// </param>
/// <param name="ClassPath">The unbundled server jar and its libraries.</param>
internal sealed record VanillaServer(
    string Version, string Directory, string ScratchDirectory, IReadOnlyList<string> ClassPath)
{
    private const string ManifestUrl = "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json";

    // The Java dumper runs as a multi-file source program and reads bytecode with java.lang.classfile, both of which
    // need Java 25 (the LTS release with both).
    private const int RequiredJavaVersion = 25;

    // Waits before each retry of a download. A dropped connection or a truncated download is usually gone a few
    // seconds later.
    private static readonly TimeSpan[] DownloadRetryDelays = [TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5)];

    /// <summary>The data generators' output (<c>data</c> and <c>reports</c>).</summary>
    public string GeneratedDirectory => Path.Combine(Directory, "generated");

    /// <summary>
    /// Downloads the server jar of <paramref name="version"/> into <paramref name="directory"/> and runs
    /// the data generators, unless an earlier run already did.
    /// </summary>
    /// <param name="directory">The version's cached download, which checkouts share.</param>
    /// <param name="scratchDirectory">The version's directory for this checkout's runs.</param>
    public static async Task<VanillaServer> PrepareAsync(HttpClient httpClient, string version, string directory,
        string scratchDirectory)
    {
        await CheckJavaVersionAsync();
        System.IO.Directory.CreateDirectory(directory);
        System.IO.Directory.CreateDirectory(scratchDirectory);

        var jarPath = Path.Combine(directory, "server.jar");

        // Written once the data generators finished, so an interrupted run starts over.
        var marker = Path.Combine(directory, "datagen.complete");
        if (!File.Exists(marker))
        {
            // Checkouts building at once share the directory. The first to take the lock downloads and runs the data
            // generators; the others wait for it and then find the marker.
            using var directoryLock = await LockAsync(Path.Combine(directory, "datagen.lock"), version);
            if (!File.Exists(marker))
                await DownloadAndGenerateAsync(httpClient, version, directory, jarPath, marker);
        }

        return new VanillaServer(version, directory, scratchDirectory, GetClassPath(jarPath, directory));
    }

    private static async Task DownloadAndGenerateAsync(HttpClient httpClient, string version, string directory,
        string jarPath, string marker)
    {
        await DownloadServerAsync(httpClient, version, jarPath);

        var generated = Path.Combine(directory, "generated");
        if (System.IO.Directory.Exists(generated))
            System.IO.Directory.Delete(generated, recursive: true);

        Console.WriteLine($"Running the {version} data generators...");
        await RunJavaAsync(directory, "-DbundlerMainClass=net.minecraft.data.Main", "-jar", "server.jar", "--all");
        await File.WriteAllTextAsync(marker, string.Empty);
    }

    /// <summary>
    /// Opens <paramref name="path"/> exclusively, which locks it across processes, waiting while another process holds
    /// it. Dispose the stream to release the lock. The file is left in place: deleting it on release would let a
    /// waiting process lock a file that a newcomer then recreates and locks too.
    /// </summary>
    /// <remarks>The same lock as <c>VanillaServerJar</c>'s, which the server's structure cache uses.</remarks>
    private static async Task<FileStream> LockAsync(string path, string version)
    {
        var directory = Path.GetDirectoryName(path);
        var waiting = false;
        while (true)
        {
            try
            {
                return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException)
            {
                // Held by another process.
                if (!waiting)
                    Console.WriteLine($"Waiting for another build preparing the {version} server in {directory}...");

                waiting = true;
                await Task.Delay(TimeSpan.FromMilliseconds(100));
            }
        }
    }

    /// <summary>
    /// Copies the server jar's <c>version.json</c> to <paramref name="path"/>. It holds the version's protocol, data
    /// version and pack formats, which a test checks Obsidian's own constants against.
    /// </summary>
    public void CopyVersionInfo(string path)
    {
        using var serverJar = this.OpenServerJar();
        VersionInfo(serverJar).ExtractToFile(path);
    }

    /// <summary>The version's data pack format: <c>pack_version.data_major</c> in its <c>version.json</c>.</summary>
    public int DataPackFormat
    {
        get
        {
            using var serverJar = this.OpenServerJar();
            using var stream = VersionInfo(serverJar).Open();
            using var info = JsonDocument.Parse(stream);
            return info.RootElement.GetProperty("pack_version").GetProperty("data_major").GetInt32();
        }
    }

    // The bundler lists the server jar first, before its libraries.
    private ZipArchive OpenServerJar() => ZipFile.OpenRead(ClassPath[0]);

    private ZipArchiveEntry VersionInfo(ZipArchive serverJar) =>
        serverJar.GetEntry("version.json") ?? throw new InvalidDataException($"{ClassPath[0]} has no version.json.");

    /// <summary>Runs <c>java</c> (from <c>JAVA_HOME</c> when set) in <paramref name="workingDirectory"/>.</summary>
    public static Task RunJavaAsync(string workingDirectory, params string[] arguments) =>
        RunJavaAsync(workingDirectory, Timeout.InfiniteTimeSpan, arguments);

    /// <summary>
    /// Runs <c>java</c> (from <c>JAVA_HOME</c> when set) in <paramref name="workingDirectory"/>, and kills it and fails
    /// if it hasn't exited within <paramref name="timeout"/>.
    /// </summary>
    public static async Task RunJavaAsync(string workingDirectory, TimeSpan timeout, params string[] arguments)
    {
        using var process = StartJava(new ProcessStartInfo(JavaPath, arguments) { WorkingDirectory = workingDirectory });
        using var cancellation = new CancellationTokenSource(timeout);
        try
        {
            await process.WaitForExitAsync(cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"java {string.Join(' ', arguments)} didn't exit within {timeout}.");
        }

        if (process.ExitCode != 0)
            throw new InvalidOperationException($"java {string.Join(' ', arguments)} exited with code {process.ExitCode}.");
    }

    public static string JavaPath
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

    // Downloads the server jar, retrying network errors, timeouts and a download that doesn't match its hash a couple
    // of times before giving up.
    private static async Task DownloadServerAsync(HttpClient httpClient, string version, string jarPath)
    {
        foreach (var delay in DownloadRetryDelays)
        {
            try
            {
                await DownloadServerOnceAsync(httpClient, version, jarPath);
                return;
            }
            catch (Exception exception) when (exception is HttpRequestException
                or IOException
                or InvalidDataException
                or TaskCanceledException)
            {
                Console.WriteLine($"Downloading the {version} server jar failed ({exception.Message}); retrying.");
                await Task.Delay(delay);
            }
        }

        await DownloadServerOnceAsync(httpClient, version, jarPath);
    }

    private static async Task DownloadServerOnceAsync(HttpClient httpClient, string version, string jarPath)
    {
        var downloads = await GetDownloadsAsync(httpClient, version);
        await DownloadAsync(httpClient, downloads.GetProperty("server"), jarPath);
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
