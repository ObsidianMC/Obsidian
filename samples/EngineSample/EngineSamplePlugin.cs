using EngineSample.Cable;
using EngineSample.Grinder;
using EngineSample.Moth;
using EngineSample.TickCounter;
using Obsidian.API.Client;
using Obsidian.API.Plugins;
using Obsidian.API.World;

namespace EngineSample;

/// <summary>
/// Obsidian's engine sample: a mod that uses what each phase of the engine plan (docs/engine-foundations.md) adds,
/// through the public API alone. Obsidian.Tests packs it into a plugin archive and loads it through the plugin manager,
/// so the sample keeps working as the engine changes. Each phase adds its content here.
/// </summary>
/// <remarks>
/// Its side is <c>both</c>: servers call <see cref="ConfigureRegistry"/>, and the Obsidian client calls it too, then
/// <see cref="ConfigureClient"/> for its client side.
/// </remarks>
public sealed class EngineSamplePlugin : PluginBase, IClientPlugin
{
    public SavedState SavedState { get; private set; } = null!;

    /// <summary>
    /// Phase 5: keep a machine's chunks ticking without players. Dispose the handle when the machine stops needing
    /// them; unloading this plugin releases it even if the caller forgets.
    /// </summary>
    public IDisposable KeepChunks(ILevel level, int chunkX, int chunkZ, int radius = 0, bool ticking = true) =>
        level.AddChunkTicket(chunkX, chunkZ, radius, ticking);

    public override void ConfigureRegistry(IPluginRegistry registry)
    {
        // Phase 1, blocks that do things: a block with a block entity that counts ticks while powered.
        TickCounterBlock.Register(registry);

        // Phase 2, items, inventories and recipes: a machine that grinds what it pulls from a chest by a recipe type
        // of its own, and pushes the results into a hopper. Phase 3 gives it a menu.
        GrinderBlock.Register(registry);

        // Phase 4, rendering: a cable whose model follows its neighbours through its block state, and a mob that
        // flies, can be ridden and has glowing eyes. The grinder's wheel spins on the client (see ConfigureClient).
        CableBlock.Register(registry);
        MothMob.Register(registry);

        // Phase 5: state shared by a network rather than owned by one block entity.
        this.SavedState = new SavedState(registry);
    }

    public void ConfigureClient(IClientRegistry registry)
    {
        // Phase 3, menus and screens: the grinder's menu shows with a screen built in Razor. GrinderScreen is the same
        // screen built in code, which registers with RegisterMenuScreen(GrinderMenu.Identifier, menu => new
        // GrinderScreen(menu)).
        registry.RegisterMenuScreen<GrinderView>(GrinderMenu.Identifier);

        // Phase 4, rendering: the grinder's wheel spins while it grinds, and the moth is drawn with glowing eyes. The
        // cable needs no code: its multipart blockstate draws it from its state.
        GrinderBlock.RegisterClient(registry);
        MothMob.RegisterClient(registry);
    }
}
