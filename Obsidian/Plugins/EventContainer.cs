using System.Reflection;

namespace Obsidian.Plugins
{
    internal class EventContainer
    {
        public string Name { get; }
        public EventInfo Event { get; }

        public EventContainer(string name, EventInfo @event)
        {
            Name = name;
            Event = @event;
        }
    }
}
