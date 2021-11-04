using Obsidian.API.Plugins;
using Obsidian.API.Plugins.Events;
using Obsidian.Events;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace Obsidian.Plugins
{
    internal readonly struct EventContainer : IComparable<EventContainer>, IComparable
    {
        public EventPriority Priority { get; }
        public MethodInfo Method { get; }
        public object TargetObject { get; }

        public EventContainer(EventPriority priority, MethodInfo methodInfo, object targetObject)
        {
            Priority = priority;
            Method = methodInfo;
            TargetObject = targetObject;
        }

        public int CompareTo(EventContainer other) => Priority.CompareTo(other.Priority);

        public int CompareTo(object obj)
        {
            if (ReferenceEquals(null, obj)) return 1;
            return obj is EventContainer other ? CompareTo(other) : throw new ArgumentException($"Object must be of type {nameof(EventContainer)}");
        }
    }

    internal class EventContainerComparer : IComparer<EventContainer>
    {
        public int Compare(EventContainer x, EventContainer y)
        {
            return x.Priority.CompareTo(y.Priority);
        }
    }
}
