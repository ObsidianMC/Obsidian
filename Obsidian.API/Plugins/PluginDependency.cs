//     Obsidian.API/PluginDependency.cs
//     Copyright (C) 2021

using System;

namespace Obsidian.API.Plugins
{
    /// <summary>
    /// Defines a dependency for a plugin
    /// </summary>
    public readonly struct PluginDependency
    {
        /// <summary>
        /// Identifier of the dependent plugin
        /// </summary>
        public readonly string Identifier;
        
        /// <summary>
        /// Version of the dependent plugin
        /// </summary>
        public readonly Version Version;
        private readonly VersionMatch versionMatch;

        /// <summary>
        /// Creates a new plugin dependency definition
        /// </summary>
        /// <param name="identifier">Identifier of the plugin</param>
        /// <param name="version">Version of the plugin</param>
        /// <param name="match">How the plugin compatibility is defined</param>
        /// <exception cref="ArgumentNullException">Thrown if identifier is null</exception>
        public PluginDependency(string identifier, Version version, VersionMatch match = VersionMatch.Major)
        {
            Identifier = identifier ?? throw new ArgumentNullException(nameof(identifier), "Identifier cannot be null");
            Version = version;
            versionMatch = match;
        }

        /// <summary>
        /// Check whether the specified version matches the plugin version
        /// </summary>
        /// <param name="version">Target version of the plugin</param>
        /// <returns>If versions match</returns>
        public bool IsCorrectVersion(Version version)
        {
            return versionMatch switch {
                VersionMatch.Any => true,
                VersionMatch.Major => version.Major == Version.Major,
                VersionMatch.Minor => version.Major == Version.Major &&
                                      version.Minor == Version.Minor,
                VersionMatch.Build => version.Major == Version.Major &&
                                      version.Minor == Version.Minor &&
                                      version.Build == Version.Build,
                VersionMatch.Exact => version.Equals(Version),
                _ => false
            };
        }
    }

    /// <summary>
    /// Enum with types of version matching
    /// </summary>
    public enum VersionMatch : byte
    {
        /// <summary>
        /// Plugin is compatible with any version of the requested plugin (eg. x.x.x.x)
        /// </summary>
        Any,
        /// <summary>
        /// Plugin is compatible with any version with the same major version (eg. 1.x.x.x)
        /// </summary>
        Major,
        /// <summary>
        /// Plugin is compatible with any version with the same major and minor versions (eg. 1.2.x.x)
        /// </summary>
        Minor,
        /// <summary>
        /// Plugin is compatible with any version with the same major, minor and build versions (eg. 1.2.3.x)
        /// </summary>
        Build,
        /// <summary>
        /// Plugin version must match exactly (eg. 1.2.3.4) 
        /// </summary>
        Exact
    }
}