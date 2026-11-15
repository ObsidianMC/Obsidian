namespace Obsidian.API;
/// <summary>
/// A condition of a surface rule, like vanilla's <c>SurfaceRules.ConditionSource</c>, tested with
/// <see cref="ISurfaceContext.Test"/>.
/// </summary>
public interface ISurfaceCondition : IRegistryResource
{
    //public ValueTask<bool> TestAsync();
}
