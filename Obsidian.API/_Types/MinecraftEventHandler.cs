namespace Obsidian.API;

/// <summary>A module of event handlers marked with <see cref="EventPriorityAttribute"/>.</summary>
/// <remarks>
/// Handlers can run concurrently across levels and connections. Make state shared across events thread-safe;
/// see <see cref="IEventDispatcher"/> for the execution contexts and threading contract.
/// </remarks>
public abstract class MinecraftEventHandler
{
}
