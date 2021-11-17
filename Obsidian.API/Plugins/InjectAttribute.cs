namespace Obsidian.API.Plugins;

/// <summary>
/// Indicates that the property should be injected with a service.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public class InjectAttribute : Attribute
{
}
