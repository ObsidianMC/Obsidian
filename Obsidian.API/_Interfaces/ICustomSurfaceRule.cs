namespace Obsidian.API;

/// <summary>
/// A surface rule type a plugin adds, like a NeoForge mod's <c>SurfaceRules.RuleSource</c>: noise settings' surface
/// rules name it in their <c>type</c>. Register it with
/// <c>IPluginRegistry.RegisterWorldgenType&lt;ISurfaceRule, TRule&gt;</c>.
/// </summary>
/// <remarks>
/// Surface building calls <see cref="Apply"/> for many blocks of every chunk, on several threads at once, so keep it
/// fast and don't change the rule's state in it.
/// </remarks>
public interface ICustomSurfaceRule : ISurfaceRule
{
    /// <summary>
    /// The block the rule puts at the context's block.
    /// </summary>
    /// <returns>The block, or null when the rule doesn't apply, so that the next rule of a sequence is tried.</returns>
    public IBlock? Apply(ISurfaceContext context);
}
