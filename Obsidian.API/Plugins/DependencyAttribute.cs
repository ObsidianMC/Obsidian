using System;

namespace Obsidian.API.Plugins;

/// <summary>
/// Indicates that the field/property should have it's value injected with a plugin.
/// </summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public class DependencyAttribute : Attribute
{
    public DependencyAttribute(string minVersion, bool optional)
    {
        Optional = optional;
        MinVersion = minVersion;
    }

    /// <summary>
    /// Indicates whether the plugin can run without this dependency.
    /// </summary>
    public bool Optional { get; set; }

    /// <summary>
    /// Minimal version of the dependency that can be used. The string should contain the major, minor, <i>[build]</i>, and <i>[revision]</i> numbers, split by a period character ('.').
    /// </summary>
    public string MinVersion { get; set; }

    /// <summary>
    /// Gets <see cref="MinVersion"/> parsed to <see cref="Version"/> if possible, otherwise returns <c>new Version()</c>.
    /// </summary>
    /// <returns><see cref="MinVersion"/> parsed to <see cref="Version"/> if possible, otherwise returns <c>new Version()</c>.</returns>
    public Version GetMinVersion()
    {
        Version.TryParse(MinVersion, out var result);
        return result ?? new Version();
    }
}
