using Obsidian.API;
using Obsidian.API.Blocks;
using Obsidian.Chat;
using Obsidian.Utilities.Registry;

namespace Obsidian
{
    public static class ServerImplementationRegistry
    {
        private static bool registered = false;

        public static void RegisterServerImplementations()
        {
            if (registered)
                return;
            registered = true;

            IChatMessage.createNew = () => new ChatMessage();
            IClickComponent.createNew = () => new ClickComponent();
            IHoverComponent.createNew = () => new HoverComponent();

            Block.BlockNames = Registry.BlockNames;
            Block.NumericToBase = Registry.NumericToBase;
            Block.StateToMatch = Registry.StateToMatch;
        }
    }
}
