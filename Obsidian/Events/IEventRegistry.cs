using System.Reflection;

namespace Obsidian.Events;

public interface IEventRegistry
{
    public string Name { get; }

    public bool TryRegisterEvent(MethodInfo method, object? instance, out Delegate? @delegate);
    public bool UnregisterEvent(Delegate @delegate);
}
