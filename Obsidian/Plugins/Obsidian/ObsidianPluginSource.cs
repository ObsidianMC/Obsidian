using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace Obsidian.Plugins.Obsidian
{
    public class ObsidianPluginSource : PluginSource
    {
        private readonly DefaultContractResolver contractResolver = new DefaultContractResolver
        {
            NamingStrategy = new CamelCaseNamingStrategy()
        };


        public override IEnumerable<Plugin> GetPlugins(string path)
        {
            var files = Directory.GetFiles(path, "*.dll");
            foreach (var file in files) // don't touch pls
            {
                var assembly = Assembly.LoadFile(file);
                var classes = assembly.GetTypes().Where(type => typeof(ObsidianPluginClass).IsAssignableFrom(type) && type != typeof(ObsidianPluginClass));

                foreach (var type in classes)

                {
                    var pluginClass = (ObsidianPluginClass)Activator.CreateInstance(type);
                    var plugin = new ObsidianPlugin(this, file, pluginClass);

                    var name = assembly.GetName().Name;
                    using var sm = assembly.GetManifestResourceStream($"{name}.plugin.json") ?? assembly.GetManifestResourceStream($"{name}.Resources.plugin.json");
                    if (sm == null)
                        throw new FileNotFoundException("Failed to find plugin.json");//TODO lets just keep this for now...

                    using var sr = new StreamReader(sm);
                    var json = sr.ReadToEnd();

                    plugin.Info = JsonConvert.DeserializeObject<PluginInfo>(json, new JsonSerializerSettings
                    {
                        ContractResolver = this.contractResolver,
                        NullValueHandling = NullValueHandling.Ignore
                    });
                    yield return plugin;
                }
            }
        }

        public override Task<IEnumerable<Plugin>> GetPluginsAsync(string path) => Task.FromResult(GetPlugins(path));
    }
}