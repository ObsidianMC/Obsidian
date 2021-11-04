//     Obsidian.API/PluginInfo.cs
//     Copyright (C) 2021

using System;

namespace Obsidian.API.Plugins
{
    /// <summary>
    /// Structure containing plugin information
    /// </summary>
    public readonly struct PluginInfo : IEquatable<PluginInfo>
    {
        /// <summary>
        /// Plugin human-friendly name
        /// </summary>
        public string Name { get; init; }
        
        /// <summary>
        /// Plugin's version
        /// </summary>
        public Version Version { get; init; }
        
        /// <summary>
        /// Plugin's description
        /// </summary>
        public string Description { get; init; }
        
        /// <summary>
        /// Plugin's authors
        /// </summary>
        public string[] Authors { get; init; }
        
        /// <summary>
        /// Plugin's project address
        /// </summary>
        public Uri? ProjectUrl { get; init; }

        /// <inheritdoc />
        public bool Equals(PluginInfo other) => Name == other.Name && Version.Equals(other.Version);

        /// <inheritdoc />
        public override bool Equals(object? obj) => obj is PluginInfo other && Equals(other);

        /// <inheritdoc />
        public override int GetHashCode() => HashCode.Combine(Name, Version);

        /// <summary>
        /// Tests the equality of structs
        /// </summary>
        /// <param name="left">Left struct</param>
        /// <param name="right">Right struct</param>
        /// <returns>If structs are equal</returns>
        public static bool operator ==(PluginInfo left, PluginInfo right) => left.Equals(right);

        /// <summary>
        /// Tests the inequality of structs
        /// </summary>
        /// <param name="left">Left struct</param>
        /// <param name="right">Right struct</param>
        /// <returns>If structs are not equal</returns>
        public static bool operator !=(PluginInfo left, PluginInfo right) => !left.Equals(right);
    }
}