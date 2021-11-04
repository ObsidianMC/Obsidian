//     Obsidian.API/EventHandlerAttribute.cs
//     Copyright (C) 2021

using System;

namespace Obsidian.API.Plugins.Events
{
    /// <summary>
    /// Defines method as an event handler
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, Inherited = false)]
    public class EventHandlerAttribute : Attribute
    {
        /// <summary>
        /// Event
        /// </summary>
        public string Event { get; }
        
        /// <summary>
        /// Event execution priority
        /// </summary>
        public EventPriority Priority { get; }

        /// <summary>
        /// Created a new event handler attribute
        /// </summary>
        /// <param name="event">Event to listen for</param>
        /// <param name="priority">Event execution priority</param>
        public EventHandlerAttribute(string @event, EventPriority priority = EventPriority.Normal)
        {
            Event = @event;
            Priority = priority;
        }
    }
}