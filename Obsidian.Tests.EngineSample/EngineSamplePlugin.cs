using EngineSample.Cable;
using EngineSample.Charms;
using EngineSample.Grinder;
using EngineSample.Moth;
using EngineSample.Portal;
using EngineSample.TickCounter;
using Obsidian.API;
using Obsidian.API.Client;
using Obsidian.API.Inventory;
using Obsidian.API.Plugins;
using Obsidian.API.World;
using System.Runtime.CompilerServices;

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
    /// <remarks>
    /// The ticket is the plugin's because its code takes it. Tests call this from outside any call into the plugin,
    /// where only the call stack shows that, so it's kept unoptimized: optimized, the call becomes a tail call and
    /// leaves no frame of the plugin's.
    /// </remarks>
    [MethodImpl(MethodImplOptions.NoOptimization)]
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

        // Phase 5: state shared by a network rather than owned by one block entity, a portal to the sample's
        // dimension, and charm slots each player keeps, whose charms the moth drops.
        this.SavedState = new SavedState(registry);
        DuskPortal.Register(registry);
        CharmSlots.Register(registry);
        registry.RegisterCreativeTab("sample:moths", new CreativeTabProperties
        {
            Title = "Engine sample",
            Icon = MothMob.Type.SpawnEgg!,
            Items = [MothMob.Type.SpawnEgg!, CharmSlots.Charm]
        });
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

        // Phase 5: a key opens the player's charm slots, shown with a screen in Razor. The dimension's sky and fog
        // come from its dimension type's data, so they need no code.
        CharmSlots.RegisterClient(registry);
    }

    public override ValueTask OnLoadedAsync(IServer server)
    {
        // Movement grants are transient and unload removes them; the saved worn items survive a hot reload.
        foreach (var player in server.OnlinePlayers.Values)
            CharmSlots.Apply(player);

        return ValueTask.CompletedTask;
    }
}
