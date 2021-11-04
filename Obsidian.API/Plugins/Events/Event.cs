//     Obsidian.API/Event.cs
//     Copyright (C) 2021

using Obsidian.API.Events;

namespace Obsidian.API.Plugins.Events
{
    /// <summary>
    /// Class containing definitions for all events
    /// </summary>
    public class Event
    {
        private readonly string name;

        private Event(string name)
        {
            this.name = name;
        }

        /// <summary>
        /// Converts event to its string representation
        /// </summary>
        /// <param name="e">Event to convert</param>
        /// <returns>String representation</returns>
        public static implicit operator string(Event e) => e.name;

        // TODO Convert to source generator (who would want to type all those manually)

        /// <summary>
        /// Called when player joins
        /// </summary>
        /// <seealso cref="PlayerJoinEventArgs"/>
        public const string PlayerJoin = "PlayerJoin";
        public const string PacketReceived = "PacketReceived";
        public const string QueuePacket = "QueuePacket";
        public const string PlayerLeave = "PlayerLeave";
        public const string ServerTick = "ServerTick";
        public const string PermissionGranted = "PermissionGranted";
        public const string PermissionRevoked = "PermissionRevoked";
        public const string InventoryClick = "InventoryClick";
        public const string BlockBreak = "BlockBreak";
        public const string IncomingChatMessage = "IncomingChatMessage";
        public const string PlayerTeleported = "PlayerTeleported";
        public const string ServerStatusRequest = "ServerStatusRequest";
        public const string EntityInteract = "EntityInteract";
        public const string PlayerAttackEntity = "PlayerAttackEntity";
        public const string PlayerInteract = "PlayerInteract";
    }

    /// <summary>
    /// Priority of execution for event handlers
    /// </summary>
    public enum EventPriority
    {
        /// <summary>
        /// Gets executed first
        /// </summary>
        Lowest,
        /// <summary>
        /// Gets executed second
        /// </summary>
        Low,
        /// <summary>
        /// Gets executed third, default priority
        /// </summary>
        Normal,
        /// <summary>
        /// Get executed fourth
        /// </summary>
        High,
        /// <summary>
        /// Gets executed fifth
        /// </summary>
        Highest,
        /// <summary>
        /// Gets executed sixth (last), has the last word on the value of event (eg. cancellation)
        /// </summary>
        Critical
    }
}