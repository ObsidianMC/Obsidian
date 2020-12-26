using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Xml;

namespace Obsidian.Plugins.PluginProviders
{
    public class RemotePluginProvider : IPluginProvider
    {
        private static HttpClient client;

        static RemotePluginProvider()
        {
            client = Globals.HttpClient;
            client.DefaultRequestHeaders.Add("User-Agent", "ObsidianServer");
        }

        public PluginContainer GetPlugin(string path, ILogger logger)
        {
            return GetPluginAsync(path, logger).GetAwaiter().GetResult();
        }

        public async Task<PluginContainer> GetPluginAsync(string path, ILogger logger)
        {
            if (!Uri.TryCreate(path, UriKind.Absolute, out Uri url))
                return Failed("'unknown'", path, logger, "Provided path is not a valid url");

            var repository = ParseUrl(url.AbsolutePath);

            return url.Host switch
            {
                "www.github.com" => await LoadGithubPluginAsync(path, logger, repository),
                "gist.github.com" => await LoadGistPluginAsync(path, logger, repository),
                _ => Failed("'unknown'", path, logger, $"Remote source {url.Host} is not supported")
            };
        }

        private async Task<PluginContainer> LoadGithubPluginAsync(string path, ILogger logger, (string owner, string name) repository)
        {
            string branch = "main";
            JsonDocument scan = await ScanDirectoryAsync(repository.owner, repository.name, branch);
            if (scan is null || scan.RootElement.TryGetProperty("message", out _))
            {
                scan?.Dispose();
                branch = "master";
                scan = await ScanDirectoryAsync(repository.owner, repository.name, branch);
                if (scan is null)
                {
                    return Failed(repository.name, path, logger, "GitHub servers are not accessible");
                }
                else if (scan.RootElement.TryGetProperty("message", out _))
                {
                    scan.Dispose();
                    return Failed(repository.name, path, logger, "Repository is missing master/main tree");
                }
            }

            JsonDocument obsidianFile = default;
            JsonElement tree = scan.RootElement.GetProperty("tree");
            foreach (JsonElement item in tree.EnumerateArray())
            {
                string itemPath = item.GetProperty("path").GetString();
                if (itemPath.EndsWith(".obsidian"))
                {
                    if (obsidianFile != default)
                        return Failed(repository.name, path, logger, "Repository contains multiple obsidian files");

                    obsidianFile = await GetJsonFileAsync(repository.owner, repository.name, branch, itemPath);
                }
            }

            (XmlDocument csproj, string csprojPath) = await GetCsprojAsync(obsidianFile, tree, repository, branch);

            if (csproj == null)
                return Failed(repository.name, path, logger, "Project not found inside plugin repository");

            string projectPath = Path.GetDirectoryName(csprojPath).Replace('\\', '/');

            var fileStreams = new List<Stream>();
            foreach (JsonElement item in tree.EnumerateArray())
            {
                string itemPath = item.GetProperty("path").GetString();
                string itemType = item.GetProperty("type").GetString();
                if (itemPath.StartsWith(projectPath) && itemPath.EndsWith(".cs") && itemType == "blob")
                {
                    fileStreams.Add(await GetFileStreamAsync(repository.owner, repository.name, branch, itemPath));
                }
            }

            scan.Dispose();
            obsidianFile?.Dispose();

            return CompilePluginFiles(repository, path, logger, fileStreams);
        }

        private async Task<PluginContainer> LoadGistPluginAsync(string path, ILogger logger, (string owner, string name) repository)
        {
            Stream stream = await GetFileStreamAsync(repository.owner, repository.name);
            return CompilePluginFiles(repository, path, logger, new[] { stream });
        }

        private PluginContainer CompilePluginFiles((string owner, string name) repository, string path, ILogger logger, IEnumerable<Stream> fileStreams)
        {
            var syntaxTrees = fileStreams.Select(fileStream => CSharpSyntaxTree.ParseText(SourceText.From(fileStream)));
            var compilation = CSharpCompilation.Create(repository.name,
                                                       syntaxTrees,
                                                       UncompiledPluginProvider.MetadataReferences,
                                                       UncompiledPluginProvider.CompilationOptions);
            using var memoryStream = new MemoryStream();
            EmitResult compilationResult = compilation.Emit(memoryStream);
            if (!compilationResult.Success)
            {
                if (logger != null)
                {
                    foreach (var diagnostic in compilationResult.Diagnostics)
                    {
                        if (diagnostic.Severity != DiagnosticSeverity.Error || diagnostic.IsWarningAsError)
                            continue;

                        logger.LogError($"Compilation failed: {diagnostic.Location} {diagnostic.GetMessage()}");
                    }
                }

                return Failed(repository.name, path);
            }
            else
            {
                memoryStream.Seek(0, SeekOrigin.Begin);

                var loadContext = new PluginLoadContext(repository.name + "LoadContext");
                var assembly = loadContext.LoadFromStream(memoryStream);
                memoryStream.Dispose();
                return PluginProviderSelector.CompiledPluginProvider.HandlePlugin(loadContext, assembly, path, logger);
            }
        }

        private async Task<(XmlDocument xml, string path)> GetCsprojAsync(JsonDocument obsidianFile, JsonElement tree, (string owner, string name) repository, string treeName)
        {
            JsonElement? csproj = null;
            string target = null;
            JsonElement targetElement = default;
            if (obsidianFile?.RootElement.TryGetProperty("target", out targetElement) != false)
                target = targetElement.GetString() + ".csproj";
            if (target != null)
            {
                csproj = tree.EnumerateArray().FirstOrDefault(item =>
                {
                    string str = item.GetProperty("path").GetString();
                    int slashIndex = str.LastIndexOf('/');
                    return slashIndex != -1 ? str[(slashIndex + 1)..] == target : str == target;
                });
            }
            else
            {
                foreach (var item in tree.EnumerateArray())
                {
                    string str = item.GetProperty("path").GetString();
                    int slashIndex = str.LastIndexOf('/');
                    if (slashIndex != -1 ? str[(slashIndex + 1)..] == target : str == target)
                    {
                        csproj = item;
                        break;
                    }
                }
            }

            if (!csproj.HasValue)
                return default;

            string csprojPath = csproj.Value.GetProperty("path").GetString();
            return (await GetXmlFileAsync(repository.owner, repository.name, treeName, csprojPath), csprojPath);
        }

        private async Task<JsonDocument> ScanDirectoryAsync(string owner, string name, string tree)
        {
            using var stream = await GetFileStreamAsync($"https://api.github.com/repos/{owner}/{name}/git/trees/{tree}?recursive=1");
            if (stream == null)
                return null;
            return await JsonDocument.ParseAsync(stream);
        }

        private async Task<JsonDocument> GetJsonFileAsync(string owner, string name, string tree, string path)
        {
            using var stream = await GetFileStreamAsync($"https://www.github.com/{owner}/{name}/raw/{tree}/{path}");
            if (stream == null)
                return null;
            return await JsonDocument.ParseAsync(stream);
        }

        private async Task<XmlDocument> GetXmlFileAsync(string owner, string name, string tree, string path)
        {
            using var stream = await GetFileStreamAsync($"https://www.github.com/{owner}/{name}/raw/{tree}/{path}");
            if (stream == null)
                return null;

            XmlDocument xml = new XmlDocument();
            xml.Load(stream);
            return xml;
        }

        private async Task<Stream> GetFileStreamAsync(string owner, string name, string tree, string path)
        {
            var response = await client.GetAsync($"https://www.github.com/{owner}/{name}/raw/{tree}/{path}");
            if (!response.IsSuccessStatusCode)
                return null;
            return await response.Content.ReadAsStreamAsync();
        }

        private async Task<Stream> GetFileStreamAsync(string owner, string hash)
        {
            var response = await client.GetAsync($"https://gist.githubusercontent.com/{owner}/{hash}/raw");
            if (!response.IsSuccessStatusCode)
                return null;
            return await response.Content.ReadAsStreamAsync();
        }

        private async Task<Stream> GetFileStreamAsync(string url)
        {
            var response = await client.GetAsync(url);
            if (!response.IsSuccessStatusCode)
                return null;

            return await response.Content.ReadAsStreamAsync();
        }

        private (string owner, string name) ParseUrl(string url)
        {
            int nameIndex = url.LastIndexOf('/');
            int ownerIndex = url.LastIndexOf('/', nameIndex - 1);
            return (url.Substring(ownerIndex + 1, nameIndex - ownerIndex - 1), url[(nameIndex + 1)..]);
        }

        private static PluginContainer Failed(string name, string path, ILogger logger = default, string reason = null)
        {
            logger?.LogError($"Loading plugin {name} failed with reason: {reason}");
            return new PluginContainer(new PluginInfo(name), path);
        }
    }
}
