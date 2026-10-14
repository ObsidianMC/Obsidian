using Obsidian.API.Networking;
using Obsidian.API.Blocks;

namespace Obsidian.API.Client;

/// <summary>
/// What a mod's <see cref="IClientPlugin.ConfigureClient"/> registers its client side with.
/// </summary>
/// <remarks>
/// <para>
/// What the mod registers takes effect when <see cref="IClientPlugin.ConfigureClient"/> returns and stays until the
/// client exits or the player reloads its mods; if it throws, none of it does. The registry is sealed then, since the
/// client loads what depends on it afterwards: registering through it later throws
/// <see cref="InvalidOperationException"/>, while <see cref="Connection"/> stays usable.
/// </para>
/// <para>
/// Like the server's content, what the mod registers is its own: identifiers are in its namespace (its mod id) and
/// valid by vanilla's rules (lowercase letters, digits and <c>_-./</c>, one <c>:</c>).
/// </para>
/// </remarks>
public interface IClientRegistry
{
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
    /// The client's connection to the server it's playing on, to send the mod's payloads from outside a payload
    /// handler, like when one of its keys is pressed.
    /// </summary>
    public IClientConnection Connection { get; }

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
    /// own.
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
    /// <param name="renderer">The model and texture.</param>
    /// <exception cref="ArgumentException">
    /// The entity type isn't one the mod registered, or the mod gave it a renderer already.
    /// </exception>
    /// <exception cref="InvalidOperationException">The mod's <c>ConfigureClient</c> returned already.</exception>
    public IClientRegistry RegisterEntityRenderer(string entityType, EntityRendererProperties renderer);

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
}
