namespace Obsidian.API;
/// <summary>
/// Marks the property of a command module that the command handler sets to the running command's
/// <see cref="CommandContext"/>. <see cref="Commands.CommandModuleBase.CommandContext"/> already has it.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class CommandContextAttribute : Attribute
{
}
