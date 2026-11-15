namespace Obsidian.API;
/// <summary>
/// A surface rule, like vanilla's <c>SurfaceRules.RuleSource</c>, which picks the block for a position near the
/// surface; see <see cref="ISurfaceContext.Apply"/>.
/// </summary>
public interface ISurfaceRule : IRegistryResource { }
