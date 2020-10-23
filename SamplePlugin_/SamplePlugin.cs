// Note: some referenced assemblies might cause the Plugin not to be loaded for security reasons, for example System.IO, System.Net, System.Diagnostics or System.Reflection
// Permissions to use specific APIs are granted by the PluginManager
using Obsidian.API.Plugins;
using Obsidian.API.Plugins.Services;
using System;
using System.Threading.Tasks;

namespace SamplePlugin_
{
    [Plugin(Name = "Sample Plugin", Version = "1.0",
            Authors = "Obsidian Team", Description = "My sample plugin.",
            ProjectUrl = "https://github.com/Naamloos/Obsidian")]
    public class SamplePlugin : PluginBase
    {
        // Any interface from Obsidian.Plugins.Services can be injected into properties
        [Inject] public ILogger Logger { get; set; }
        [Inject] public IFileWriter FileWriter { get; set; }
        [Inject] public IFileReader FileReader { get; set; }
        [Inject] public INativeLoader NativeLoader { get; set; }

        // Dependencies will be injected automatically, if dependency class and field/property names match
        // Plugins won't load until all their required dependencies are added
        // Optional dependencies may be injected at any time, if at all
        [Dependency(Optional = true, MinVersion = "1.0")]
        private PluginBase OtherPlugin;

        // Hard dependencies (with known type) can't be compiled unless dependencies are added first
        [Dependency]
        public SamplePlugin OtherPlugin2 { get; set; }

        // Wrappers will have their delegate fields/properties injected with delegates from the dependency plugin
        [Dependency(Alias = "Other Plugin Name")]
        public MyWrapper OtherPlugin3 { get; set; }

        // One of server messages, called when an event occurs
        public void OnLoad()
        {
            Logger.Log("Plugin loaded!");

            if (FileReader.IsUsable)
            {
                var config = FileReader.ReadAllText("config.txt");
            }
            else
            {
                Logger.LogWarning("Configuration can't be loaded, because this Plugin is missing file reading permissions!");
            }

            if (OtherPlugin != null)
            {
                OtherPlugin.Invoke("OtherPluginMethod");
                int callResult = OtherPlugin.Invoke<int>("OtherPluginMethod2", "argument1", "argument2", 3, 4, "argument5");

                // You can obtain a delegate of a different plugin method
                oftenCalledMethod = OtherPlugin.GetMethod<Func<int>>("OtherPluginMethod3");
                // ...and for properties too
                Func<string> getProperty = OtherPlugin.GetPropertyGetter<string>("StringProperty");
                Action<string> setProperty = OtherPlugin.GetPropertySetter<string>("StringProperty");
            }
            else
            {
                Logger.LogWarning("OtherPluginMethod couldn't be invoked, because OtherPlugin dependency is missing.");
            }
        }

        // Caching dependency methods can be avoided by using plugin wrappers
        private Func<int> oftenCalledMethod;

        // One of server messages, called when an event occurs
        public void OnCreeperExplosion()
        {
            int? callResult;
            callResult = oftenCalledMethod?.Invoke();

            if (OtherPlugin3 != null)
            {
                // SomeMethod is automatically injected into the PluginWrapper
                callResult = OtherPlugin3.SomeMethod();

                // PluginWrappers can still be used as normal dependencies
                callResult = OtherPlugin3.Invoke<int>("AnotherMethod");
            }
        }

        public async Task OnServerTick()
        {
            await Task.Delay(0);
        }
    }

    public class MyWrapper : PluginWrapper
    {
        public Func<int> SomeMethod { get; set; }
        public Func<string> get_StringProperty { get; set; }
        public Action<string> set_StringProperty { get; set; }
    }
}
