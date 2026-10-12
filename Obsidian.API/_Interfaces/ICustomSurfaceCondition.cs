namespace Obsidian.API;

/// <summary>
/// A surface condition type a plugin adds, like a NeoForge mod's <c>SurfaceRules.ConditionSource</c>: surface rules'
/// conditions (<c>if_true</c>, <c>invert</c>) name it in their <c>type</c>. Register it with
/// <c>IPluginRegistry.RegisterWorldgenType&lt;ISurfaceCondition, TCondition&gt;</c>.
/// </summary>
/// <remarks>
/// Like <see cref="ICustomSurfaceRule.Apply"/>, <see cref="Test"/> runs for many blocks, on several threads at once.
/// </remarks>
public interface ICustomSurfaceCondition : ISurfaceCondition
{
    /// <summary>
    /// Whether the condition holds at the context's block.
    /// </summary>
    public bool Test(ISurfaceContext context);
}
