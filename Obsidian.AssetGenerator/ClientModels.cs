using System.Security.Cryptography;
using System.Text.Json;

namespace Obsidian.AssetGenerator;

/// <summary>Evaluates model layers using a developer's installed client, never downloading a game jar.</summary>
internal static class ClientModels
{
    public static async Task DumpAsync(HttpClient http, VanillaServer server, string output)
    {
        var installation = Environment.GetEnvironmentVariable("MINECRAFT_DIR") ?? (OperatingSystem.IsWindows()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".minecraft")
            : OperatingSystem.IsMacOS()
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support", "minecraft")
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".minecraft"));
        var directory = Path.Combine(installation, "versions", server.Version);
        var jar = Path.Combine(directory, server.Version + ".jar");
        var metadata = Path.Combine(directory, server.Version + ".json");
        if (!File.Exists(jar) || !File.Exists(metadata))
        {
            Console.WriteLine($"Skipping client model dump: install Minecraft {server.Version} with the official launcher (or set MINECRAFT_DIR). No client jar will be downloaded.");
            return;
        }
        using var version = JsonDocument.Parse(await File.ReadAllTextAsync(metadata));
        await using (var input = File.OpenRead(jar))
        {
            var expected = version.RootElement.GetProperty("downloads").GetProperty("client").GetProperty("sha1").GetString();
            if (!Convert.ToHexStringLower(await SHA1.HashDataAsync(input)).Equals(expected, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The installed client jar does not match the launcher's SHA-1.");
        }
        var mappings = Path.Combine(server.Directory, "client.txt");
        var download = version.RootElement.GetProperty("downloads").GetProperty("client_mappings");
        var hash = download.GetProperty("sha1").GetString()!;
        if (!File.Exists(mappings) || !Convert.ToHexStringLower(SHA1.HashData(await File.ReadAllBytesAsync(mappings))).Equals(hash, StringComparison.OrdinalIgnoreCase))
        {
            var bytes = await http.GetByteArrayAsync(download.GetProperty("url").GetString()!);
            if (!Convert.ToHexStringLower(SHA1.HashData(bytes)).Equals(hash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Client mappings SHA-1 mismatch.");
            await File.WriteAllBytesAsync(mappings, bytes);
        }
        var classpath = new List<string> { jar };
        foreach (var library in version.RootElement.GetProperty("libraries").EnumerateArray())
        {
            if (library.TryGetProperty("downloads", out var downloads) && downloads.TryGetProperty("artifact", out var artifact))
            {
                var path = Path.Combine(installation, "libraries", artifact.GetProperty("path").GetString()!);
                if (File.Exists(path)) classpath.Add(path);
            }
        }
        Console.WriteLine("Dumping all installed-client model layers...");
        await VanillaServer.RunJavaAsync(server.Directory, "-Djava.awt.headless=true", "-cp", string.Join(Path.PathSeparator, classpath),
            Path.Combine(AppContext.BaseDirectory, "Java", "ClientModelDumper.java"), mappings, Path.Combine(output, "client_models.json"));
        await FixtureDumper.DumpClientAsync(server, classpath, mappings, output);
    }
}
