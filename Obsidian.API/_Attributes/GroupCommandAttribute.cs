namespace Obsidian.API;

/// <summary>
/// Marks a method of a <see cref="CommandGroupAttribute"/> class as an overload that runs when the group's name is
/// typed without a subcommand.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class GroupCommandAttribute : Attribute
{
}
