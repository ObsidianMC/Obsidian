using Obsidian.API.Inventory;
using Obsidian.API.Networking;
using Obsidian.API.Blocks;
using Obsidian.API.Client.Screens;

namespace Obsidian.API.Client;

/// <summary>
/// What a mod's <see cref="IClientPlugin.ConfigureClient"/> registers its client side with.
/// </summary>
/// <remarks>
/// <para>
/// What the mod registers takes effect when <see cref="IClientPlugin.ConfigureClient"/> returns and stays until the
/// client exits or the player reloads its mods; if it throws, none of it does. The registry is sealed then, since the
/// client loads what depends on it afterwards: registering through it later throws
/// <see cref="InvalidOperationException"/>, while <see cref="Connection"/> and <see cref="Screens"/> stay usable.
/// </para>
/// <para>
/// Like the server's content, what the mod registers is its own: identifiers are in its namespace (its mod id) and
/// valid by vanilla's rules (lowercase letters, digits and <c>_-./</c>, one <c>:</c>).
/// </para>
/// </remarks>
public interface IClientRegistry
{
    /// <summary>Local audio for this mod; usable after ConfigureClient returns, until the mod unloads.</summary>
    public IClientSounds Sounds { get; }

    /// <summary>
    /// Supplies collision and outline shapes for a block entity type this mod registered. The provider receives
    /// its latest synced update tag as plain NBT values (nested compounds are dictionaries, lists are lists).
    /// Return null to use the block state's shapes. Runs at most once per snapshot on a reading thread: keep it
    /// pure and short, and do not mutate nested values. Registration is staged until ConfigureClient returns and
    /// removed when the mod unloads.
    /// </summary>
    public IClientRegistry RegisterBlockEntityShapes(
        string blockEntityType,
        Func<IReadOnlyDictionary<string, object?>, BlockShapes?> provider);

    /// <summary>
    /// Adds model parts from a block entity type's synced data to its blocks' models as the world is meshed, like
    /// cable connections and facades. A new update meshes the block again.
    /// </summary>
    /// <param name="blockEntityType">
    /// The identifier of a block entity type the mod registered, like <c>examplemod:cable_bus</c>.
    /// </param>
    /// <param name="model">The models the parts may name, and the provider choosing them.</param>
    /// <exception cref="ArgumentException">
    /// The block entity type isn't one the mod registered, a model isn't a valid identifier, or the mod gave the type
    /// a model already.
    /// </exception>
    /// <exception cref="InvalidOperationException">The mod's <c>ConfigureClient</c> returned already.</exception>
    public IClientRegistry RegisterBlockEntityModel(string blockEntityType, BlockEntityModelProperties model);

    /// <summary>
    /// Draws a block entity type's moving parts each frame, like vanilla's block entity renderers: parts its synced
    /// data describes, which the client animates, or code that draws them.
    /// </summary>
    /// <param name="blockEntityType">
    /// The identifier of a block entity type the mod registered, like <c>examplemod:grinder</c>.
    /// </param>
    /// <param name="renderer">The models the parts may name, and the provider or code drawing them.</param>
    /// <exception cref="ArgumentException">
    /// The block entity type isn't one the mod registered, a model isn't a valid identifier, the renderer has neither
    /// a provider nor code, or the mod gave the type a renderer already.
    /// </exception>
    /// <exception cref="InvalidOperationException">The mod's <c>ConfigureClient</c> returned already.</exception>
    public IClientRegistry RegisterBlockEntityRenderer(string blockEntityType, BlockEntityRendererProperties renderer);

    /// <summary>
    /// The client's connection to the server it's playing on, to send the mod's payloads from outside a payload
    /// handler, like when one of its keys is pressed.
    /// </summary>
    public IClientConnection Connection { get; }

    /// <summary>
    /// Opens the mod's screens, like a settings screen opened with one of its keys. Like <see cref="Connection"/>, it
    /// stays usable after <see cref="IClientPlugin.ConfigureClient"/>.
    /// </summary>
    public IClientScreens Screens { get; }

    /// <summary>
    /// Handles the payloads the server sends on <typeparamref name="TPayload"/>'s channel, in the phases the mod
    /// declared it for with <see cref="Plugins.IPluginRegistry.MapPayload{TPayload}"/>; the server's handler given
    /// there is the server's, and isn't called on the client.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// The mod didn't declare the channel, or declared it only for payloads sent to the server.
    /// </exception>
    /// <exception cref="InvalidOperationException">The mod's <c>ConfigureClient</c> returned already.</exception>
    public IClientRegistry HandlePayload<TPayload>(ClientPayloadHandler<TPayload> handler)
        where TPayload : ICustomPayload<TPayload>;

    /// <summary>
    /// Runs <paramref name="handler"/> on the game's thread every client tick, 20 times a second, in menus as in game,
    /// for the mod's own client logic, like polling <see cref="IKeyBinding.IsDown"/>. <see cref="Connection"/>'s
    /// <see cref="IClientConnection.Phase"/> says whether the client is playing on a server. A handler that throws is
    /// logged and not run again.
    /// </summary>
    /// <exception cref="InvalidOperationException">The mod's <c>ConfigureClient</c> returned already.</exception>
    public IClientRegistry OnTick(Action handler);

    /// <summary>
    /// Registers a key binding, listed in the key binds screen under its category and saved in <c>options.txt</c> like
    /// vanilla's.
    /// </summary>
    /// <param name="identifier">
    /// The binding's identifier, in the mod's namespace, like <c>examplemod:open_map</c>; its name, and translation
    /// key, is <c>key.examplemod.open_map</c>.
    /// </param>
    /// <param name="defaultKey">
    /// The key or mouse button it's bound to until the player rebinds it, in vanilla's names: <c>key.keyboard.g</c>,
    /// <c>key.keyboard.left.alt</c>, <c>key.mouse.middle</c>, or <c>key.keyboard.unknown</c> for none.
    /// </param>
    /// <param name="category">
    /// The category's identifier, like <c>examplemod:main</c>, in any namespace so mods can share one; its
    /// translation key is <c>key.category.examplemod.main</c>.
    /// </param>
    /// <exception cref="ArgumentException">
    /// The identifier isn't valid or in the mod's namespace, the category isn't valid, a binding with the identifier
    /// exists, or the key isn't a keyboard key or mouse button name.
    /// </exception>
    /// <exception cref="InvalidOperationException">The mod's <c>ConfigureClient</c> returned already.</exception>
    public IKeyBinding RegisterKeyBinding(string identifier, string defaultKey, string category);

    /// <summary>
    /// Tints a block: the faces of its models with a <c>tintindex</c> are multiplied by the tint's colour, and so are
    /// its item's when the item's definition (<c>assets/&lt;ns&gt;/items/...</c>) gives the model no tints of its
    /// own. Use <see cref="BlockTint.FixedByIndex"/> for a separate colour per tint index.
    /// </summary>
    /// <param name="block">The identifier of a block the mod registered, like <c>examplemod:mossy_bricks</c>.</param>
    /// <param name="tint">The tint.</param>
    /// <exception cref="ArgumentException">
    /// The block isn't one the mod registered, or the mod tinted it already.
    /// </exception>
    /// <exception cref="InvalidOperationException">The mod's <c>ConfigureClient</c> returned already.</exception>
    public IClientRegistry RegisterBlockTint(string block, BlockTint tint);

    /// <summary>
    /// Draws a mod's entity type with a model and texture from its assets. Without one, the type's entities aren't
    /// drawn.
    /// </summary>
    /// <param name="entityType">
    /// The identifier of an entity type the mod registered, like <c>examplemod:golem</c>.
    /// </param>
    /// <param name="renderer">The model and texture, and any layers and code.</param>
    /// <exception cref="ArgumentException">
    /// The entity type isn't one the mod registered, the mod gave it a renderer already, or the renderer is missing its
    /// model, its texture or a layer's texture.
    /// </exception>
    /// <exception cref="InvalidOperationException">The mod's <c>ConfigureClient</c> returned already.</exception>
    public IClientRegistry RegisterEntityRenderer(string entityType, EntityRendererProperties renderer);

    /// <summary>
    /// Registers a property an item model definition's <c>minecraft:condition</c> model can test, like NeoForge's
    /// conditional item model properties: <c>{"type":"minecraft:condition","property":"examplemod:charged",
    /// "on_true":...,"on_false":...}</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The property is a function of the stack, read-only with its components (including the mod's own, read with
    /// <c>ItemStack.TryGet</c>), and where it's drawn. It runs on the client's game thread inside a call into the mod,
    /// when a stack drawn with the definition changes, or its context does (a new tick, another holder or display
    /// context): its value is kept per stack and display context in between, so keep it pure. If it throws, the error
    /// is logged, and it's false for the stack until the stack changes.
    /// </para>
    /// <para>
    /// Condition, select and range properties are separate, like vanilla's: one identifier can name one of each.
    /// </para>
    /// </remarks>
    /// <param name="property">
    /// The property's identifier, in the mod's namespace, like <c>examplemod:charged</c>.
    /// </param>
    /// <param name="condition">Whether the stack takes the <c>on_true</c> model.</param>
    /// <exception cref="ArgumentException">
    /// The identifier isn't valid or in the mod's namespace, or the mod registered the condition already.
    /// </exception>
    /// <exception cref="InvalidOperationException">The mod's <c>ConfigureClient</c> returned already.</exception>
    public IClientRegistry RegisterItemCondition(string property, Func<ItemStack, ItemModelContext, bool> condition);

    /// <summary>
    /// Registers a property an item model definition's <c>minecraft:select</c> model can choose by, like NeoForge's
    /// select item model properties: the case whose <c>when</c> is the value (or lists it) is drawn, else the
    /// <c>fallback</c>.
    /// </summary>
    /// <remarks>
    /// It runs, is kept and fails like <see cref="RegisterItemCondition"/>'s properties; a failure, or null, chooses
    /// the fallback.
    /// </remarks>
    /// <param name="property">The property's identifier, in the mod's namespace, like <c>examplemod:gem</c>.</param>
    /// <param name="select">
    /// The stack's value, compared with the cases' <c>when</c> strings exactly; null for none.
    /// </param>
    /// <exception cref="ArgumentException">
    /// The identifier isn't valid or in the mod's namespace, or the mod registered the select property already.
    /// </exception>
    /// <exception cref="InvalidOperationException">The mod's <c>ConfigureClient</c> returned already.</exception>
    public IClientRegistry RegisterItemSelect(string property, Func<ItemStack, ItemModelContext, string?> select);

    /// <summary>
    /// Registers a property an item model definition's <c>minecraft:range_dispatch</c> model can choose by, like
    /// NeoForge's range item model properties: the value, times the model's <c>scale</c>, chooses the entry with the
    /// highest <c>threshold</c> at or below it, else the <c>fallback</c>.
    /// </summary>
    /// <remarks>
    /// It runs, is kept and fails like <see cref="RegisterItemCondition"/>'s properties; a failure gives 0.
    /// </remarks>
    /// <param name="property">The property's identifier, in the mod's namespace, like <c>examplemod:fill</c>.</param>
    /// <param name="range">The stack's value.</param>
    /// <exception cref="ArgumentException">
    /// The identifier isn't valid or in the mod's namespace, or the mod registered the range property already.
    /// </exception>
    /// <exception cref="InvalidOperationException">The mod's <c>ConfigureClient</c> returned already.</exception>
    public IClientRegistry RegisterItemRange(string property, Func<ItemStack, ItemModelContext, float> range);

    /// <summary>
    /// Registers lines a mod's item adds to its tooltip under its name, like an item's <c>appendHoverText</c> in
    /// NeoForge: a storage cell's fill, say, read from its components.
    /// </summary>
    /// <remarks>
    /// The lines take vanilla's legacy <c>§</c> formatting codes, like <see cref="Screens.Widget.Tooltip"/>; start a
    /// line with <c>§7</c> for vanilla's grey. They're asked for each time the tooltip is drawn, on the client's game
    /// thread inside a call into the mod, so keep it short and pure. If it throws, the error is logged and the tooltip
    /// shows none of its lines.
    /// </remarks>
    /// <param name="item">The item, one of the mod's, like <c>examplemod:battery</c>.</param>
    /// <param name="lines">Adds the stack's lines to the list it's given.</param>
    /// <exception cref="ArgumentException">
    /// The identifier isn't one of the mod's items, or the mod registered the item's tooltip already.
    /// </exception>
    /// <exception cref="InvalidOperationException">The mod's <c>ConfigureClient</c> returned already.</exception>
    public IClientRegistry RegisterItemTooltip(string item, Action<ItemStack, IList<string>> lines);

    /// <summary>
    /// Draws a special item model type with code, like NeoForge's special model renderers: items whose definitions
    /// have a <c>minecraft:special</c> model of the type draw with the renderer in every display context.
    /// </summary>
    /// <param name="specialModelType">
    /// The type's identifier, in the mod's namespace, like <c>examplemod:gem_display</c>.
    /// </param>
    /// <param name="renderer">The models it may draw, and its code.</param>
    /// <exception cref="ArgumentException">
    /// The identifier isn't valid or in the mod's namespace, a model isn't a valid identifier, or the mod registered
    /// the type already.
    /// </exception>
    /// <exception cref="InvalidOperationException">The mod's <c>ConfigureClient</c> returned already.</exception>
    public IClientRegistry RegisterItemRenderer(string specialModelType, ItemRendererProperties renderer);

    /// <summary>
    /// Sets how a mod's particle type's particles move and how long they live. They're drawn with the sprites of its
    /// definition, <c>assets/&lt;ns&gt;/particles/&lt;name&gt;.json</c>, with or without a provider.
    /// </summary>
    /// <param name="particleType">
    /// The identifier of a particle type the mod registered, like <c>examplemod:spark</c>.
    /// </param>
    /// <param name="provider">How its particles move and live.</param>
    /// <exception cref="ArgumentException">
    /// The particle type isn't one the mod registered, or the mod gave it a provider already.
    /// </exception>
    /// <exception cref="InvalidOperationException">The mod's <c>ConfigureClient</c> returned already.</exception>
    public IClientRegistry RegisterParticleProvider(string particleType, ParticleProviderProperties provider);

    /// <summary>
    /// Shows a mod's menu type with a background and slot positions from its assets. Without a screen, the menu shows
    /// like a chest.
    /// </summary>
    /// <param name="menuType">The identifier of a menu type the mod registered, like <c>examplemod:pouch</c>.</param>
    /// <param name="screen">The background and where the slots go.</param>
    /// <exception cref="ArgumentException">
    /// The menu type isn't one the mod registered, the screen places a different number of slots than the menu has,
    /// or the mod gave it a screen already.
    /// </exception>
    /// <exception cref="InvalidOperationException">The mod's <c>ConfigureClient</c> returned already.</exception>
    public IClientRegistry RegisterMenuScreen(string menuType, MenuScreenProperties screen);

    /// <summary>
    /// Shows a mod's menu type with a screen built in code: <paramref name="screen"/> makes one for the menu each time
    /// the server opens it, for the widgets its <see cref="ModScreen.Init"/> adds. Use it rather than
    /// <see cref="MenuScreenProperties"/> for a menu with more than slots on a background: progress, buttons, text
    /// fields, lists.
    /// </summary>
    /// <param name="menuType">
    /// The identifier of a menu type the mod registered, like <c>examplemod:grinder</c>.
    /// </param>
    /// <param name="screen">Makes the screen for an opened menu.</param>
    /// <exception cref="ArgumentException">
    /// The menu type isn't one the mod registered, or the mod gave it a screen already.
    /// </exception>
    /// <exception cref="InvalidOperationException">The mod's <c>ConfigureClient</c> returned already.</exception>
    public IClientRegistry RegisterMenuScreen(string menuType, Func<IScreenMenu, ModMenuScreen> screen);

    /// <summary>
    /// Shows a mod's menu type with a screen built in Razor: the client renders <typeparamref name="TComponent"/>
    /// in the menu's container screen each time the server opens the menu, with the menu in its
    /// <see cref="Razor.ScreenContext"/>. It's the Razor form of
    /// <see cref="RegisterMenuScreen(string, Func{IScreenMenu, ModMenuScreen})"/>, hosted the same way.
    /// </summary>
    /// <typeparam name="TComponent">The screen's component.</typeparam>
    /// <param name="menuType">
    /// The identifier of a menu type the mod registered, like <c>examplemod:grinder</c>.
    /// </param>
    /// <exception cref="ArgumentException">
    /// The menu type isn't one the mod registered, or the mod gave it a screen already.
    /// </exception>
    /// <exception cref="InvalidOperationException">The mod's <c>ConfigureClient</c> returned already.</exception>
    public IClientRegistry RegisterMenuScreen<TComponent>(string menuType)
        where TComponent : Razor.ModComponent =>
        this.RegisterMenuScreen(menuType, menu => new Razor.ComponentMenuScreen(menu, typeof(TComponent)));

    /// <summary>
    /// Draws an overlay on the HUD, like a minimap or a mana bar, from when <see cref="IClientPlugin.ConfigureClient"/>
    /// returns until the mod unloads; its <see cref="ModOverlay.Tick"/> updates it while the player is in a world.
    /// </summary>
    /// <param name="overlay">The overlay, with the widgets it draws.</param>
    /// <exception cref="ArgumentException">The mod registered the overlay already.</exception>
    /// <exception cref="InvalidOperationException">The mod's <c>ConfigureClient</c> returned already.</exception>
    public IClientRegistry RegisterOverlay(ModOverlay overlay);

    /// <summary>
    /// Draws a mod's dimension type's clouds with its own texture, or adjusts and draws its sky, fog and clouds in
    /// code, beyond what the type's data says (see <see cref="DimensionEffectsProperties"/>).
    /// </summary>
    /// <param name="dimensionType">
    /// The identifier of a dimension type in the mod's namespace, like <c>examplemod:skylands</c> for the server's
    /// <c>data/examplemod/dimension_type/skylands.json</c>.
    /// </param>
    /// <param name="effects">The cloud texture, sky textures and code.</param>
    /// <exception cref="ArgumentException">
    /// The identifier isn't valid or in the mod's namespace, a texture isn't a valid identifier, or the mod gave the
    /// type effects already.
    /// </exception>
    /// <exception cref="InvalidOperationException">The mod's <c>ConfigureClient</c> returned already.</exception>
    public IClientRegistry RegisterDimensionEffects(string dimensionType, DimensionEffectsProperties effects);
}
