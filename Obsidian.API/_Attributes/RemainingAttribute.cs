namespace Obsidian.API;

/// <summary>
/// Marks a command method's last parameter as taking every remaining word of the command line, joined by spaces.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false)]
public sealed class RemainingAttribute : Attribute
{
}
