using Obsidian.Events;

namespace Obsidian.Plugins
{
    internal class EventContainer
    {
        public string Name { get; }
        public IEventRegistry EventRegistry { get; }

        public EventContainer(string name, IEventRegistry eventRegistry)
        {
            Name = name;
            EventRegistry = eventRegistry;
        }
    }
}
