namespace Obsidian.API;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class EventPriorityAttribute : Attribute
{
    public Priority Priority { get; set; }
}
