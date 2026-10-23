namespace Obsidian.API;

/// <summary>
/// Marks a method as another overload of the <see cref="CommandAttribute"/> method with the same name in its module.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class CommandOverloadAttribute : Attribute
{
}
