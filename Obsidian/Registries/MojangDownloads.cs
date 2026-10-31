using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;

namespace Obsidian.Registries;

/// <summary>Resolves official Minecraft downloads and verifies the SHA-1 published by Mojang.</summary>
internal static class MojangDownloads
{
    // The current manifest host, which the asset generator uses too.
    private const string ManifestUrl = "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json";

    // Waits before each retry. A dropped connection or a truncated download is usually gone a few seconds later.
    private static readonly TimeSpan[] RetryDelays = [TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5)];

    /// <summary>Downloads the version's named artifact (server or client) to a caller-owned temporary path.</summary>
    /// <remarks>
    /// The caller creates the directory and removes the file if downloading or verification fails. Network errors,
    /// timeouts and a download that doesn't match its hash are retried a couple of times before giving up.
    /// </remarks>
    public static async Task DownloadAsync(HttpClient httpClient, string version, string download, string path, CancellationToken cancellationToken)
    {
        foreach (var delay in RetryDelays)
        {
            try
            {
                await DownloadOnceAsync(httpClient, version, download, path, cancellationToken);
                return;
            }
            catch (Exception exception) when (IsTransient(exception, cancellationToken))
            {
                await Task.Delay(delay, cancellationToken);
            }
        }

        await DownloadOnceAsync(httpClient, version, download, path, cancellationToken);
    }

    private static bool IsTransient(Exception exception, CancellationToken cancellationToken)
    {
        // HttpClient reports its own timeout as a cancellation; the caller's cancellation isn't retried.
        var timedOut = exception is TaskCanceledException && !cancellationToken.IsCancellationRequested;

        return timedOut || exception is HttpRequestException or IOException or InvalidDataException;
    }

    private static async Task DownloadOnceAsync(HttpClient httpClient, string version, string download, string path,
        CancellationToken cancellationToken)
    {
        string? versionUrl = null;
        using (var manifest = await GetJsonAsync(httpClient, ManifestUrl, cancellationToken))
        {
            foreach (var entry in manifest.RootElement.GetProperty("versions").EnumerateArray())
            {
                if (entry.GetProperty("id").ValueEquals(version))
                {
                    versionUrl = entry.GetProperty("url").GetString();
                    break;
                }
            }
        }

        if (versionUrl is null)
        {
            throw new InvalidOperationException($"Mojang's version manifest has no version {version}.");
        }

        string url;
        string sha1;
        using (var versionJson = await GetJsonAsync(httpClient, versionUrl, cancellationToken))
        {
            var artifact = versionJson.RootElement.GetProperty("downloads").GetProperty(download);
            url = artifact.GetProperty("url").GetString()!;
            sha1 = artifact.GetProperty("sha1").GetString()!;
        }

        await using (var file = File.Create(path))
        {
            await using (var input = await httpClient.GetStreamAsync(url, cancellationToken))
            {
                await input.CopyToAsync(file, cancellationToken);
            }
        }

        // SHA-1 is the hash Mojang publishes for its downloads.
        await using var downloaded = File.OpenRead(path);
        var hash = Convert.ToHexString(await SHA1.HashDataAsync(downloaded, cancellationToken));
        if (!hash.Equals(sha1, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"The {download} jar of {version} doesn't match Mojang's SHA-1 {sha1}.");
        }
    }

    /// <summary>Reads a manifest document while keeping stream ownership inside the request.</summary>
    private static async Task<JsonDocument> GetJsonAsync(HttpClient httpClient, string url, CancellationToken cancellationToken)
    {
        await using var stream = await httpClient.GetStreamAsync(url, cancellationToken);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
    }
}
