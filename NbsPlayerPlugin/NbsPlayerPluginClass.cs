using Obsidian;
using Obsidian.Plugins;

namespace NbsPlayerPlugin
{
    public class NbsPlayerPluginClass : IPluginClass
    {
        private Server server;

        public static int ServerTickStart = 0;

        public PluginInfo Initialize(Server server)
        {
            this.server = server;

            this.server.Commands.AddModule<NbsPlayerCommands>();

            return new PluginInfo(
                "NBS Player Plugin",
                "Craftplacer (Obsidian Team)",
                "0.1",
                "Plays back .NBS files stored on this server",
                "https://github.com/NaamloosDT/Obsidian"
            );
        }
    }
}