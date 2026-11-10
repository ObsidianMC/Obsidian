namespace Obsidian.WorldData;

/// <summary>
/// Vanilla blocks' shape rules by registry id, split by where they run (<see cref="ShapeReach"/>). The generator binds
/// each block to the <see cref="ShapeUpdateBehaviors"/> member named after the vanilla class that declares its
/// <c>updateShape</c> (<c>BlocksRegistry.GetShapeUpdateBehavior</c>); these tables read that binding once, so a lookup
/// is an array read.
/// </summary>
internal static class ShapeDispatch
{
    private static readonly IShapeUpdateBehavior?[] liveOwners;

    private static readonly IShapeUpdateBehavior?[] sharedRules;

    private static readonly IShapeUpdateBehavior pluginRule = new ShapeRules.PluginSnowyShapeRule();

    static ShapeDispatch()
    {
        var count = BlocksRegistry.ResourceIds.Length;
        liveOwners = new IShapeUpdateBehavior?[count];
        sharedRules = new IShapeUpdateBehavior?[count];

        for (var registryId = 0; registryId < count; registryId++)
        {
            var behavior = BlocksRegistry.GetShapeUpdateBehavior(registryId);
            if (behavior is null)
                continue;

            if (behavior.Reach == ShapeReach.SharedPass)
            {
                sharedRules[registryId] = behavior;
                continue;
            }

            liveOwners[registryId] = behavior;
            sharedRules[registryId] = behavior.SharedPassRule;
        }
    }

    /// <summary>
    /// The rule the live engines run ahead of plugin behaviours and the shared pass, or null (plugin blocks, and
    /// vanilla blocks whose rule is only in the shared pass).
    /// </summary>
    public static IShapeUpdateBehavior? LiveOwner(int registryId) =>
        (uint)registryId < (uint)liveOwners.Length ? liveOwners[registryId] : null;

    /// <summary>
    /// The rule the shared pass (<see cref="Features.Tree.ShapeUpdater"/>) runs, or null. Plugin blocks, whose ids
    /// follow vanilla's, get the snowy rule the shared pass has always given blocks without one.
    /// </summary>
    public static IShapeUpdateBehavior? SharedRule(int registryId) =>
        (uint)registryId < (uint)sharedRules.Length ? sharedRules[registryId] : pluginRule;
}
