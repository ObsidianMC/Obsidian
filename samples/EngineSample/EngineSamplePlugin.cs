using EngineSample.Grinder;
using EngineSample.TickCounter;
using Obsidian.API.Plugins;

namespace EngineSample;

/// <summary>
/// Obsidian's engine sample: a plugin that uses what each phase of the engine plan (docs/engine-foundations.md) adds,
/// through the public API alone, as a mod would. Obsidian.Tests packs it into a plugin archive and loads it through the
/// plugin manager, so the sample keeps working as the engine changes. Each phase adds its content here.
/// </summary>
public sealed class EngineSamplePlugin : PluginBase
{
    public override void ConfigureRegistry(IPluginRegistry registry)
    {
        // Phase 1, blocks that do things: a block with a block entity that counts ticks while powered.
        TickCounterBlock.Register(registry);

        // Phase 2, items, inventories and recipes: a machine that grinds what it pulls from a chest by a recipe type
        // of its own, and pushes the results into a hopper.
        GrinderBlock.Register(registry);
    }
}
