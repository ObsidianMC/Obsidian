using Obsidian.API.Networking;

namespace Obsidian.API.Client;

/// <summary>
/// What a mod's <see cref="IClientPlugin.ConfigureClient"/> registers its client side with. Everything registered
/// stays until the client exits.
/// </summary>
public interface IClientRegistry
{
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
    public IClientRegistry HandlePayload<TPayload>(ClientPayloadHandler<TPayload> handler)
        where TPayload : ICustomPayload<TPayload>;

    /// <summary>
    /// Registers a key binding, listed in the key binds screen under its category and saved in <c>options.txt</c> like
    /// vanilla's.
    /// </summary>
    /// <param name="identifier">
    /// The binding's identifier, like <c>examplemod:open_map</c>; its name, and translation key, is
    /// <c>key.examplemod.open_map</c>.
    /// </param>
    /// <param name="defaultKey">
    /// The key or mouse button it's bound to until the player rebinds it, in vanilla's names: <c>key.keyboard.g</c>,
    /// <c>key.keyboard.left.alt</c>, <c>key.mouse.middle</c>, or <c>key.keyboard.unknown</c> for none.
    /// </param>
    /// <param name="category">
    /// The category's identifier, like <c>examplemod:main</c>; its translation key is
    /// <c>key.category.examplemod.main</c>.
    /// </param>
    /// <exception cref="ArgumentException">
    /// A binding with the identifier exists, or the key isn't a keyboard key or mouse button name.
    /// </exception>
    public IKeyBinding RegisterKeyBinding(string identifier, string defaultKey, string category);

    /// <summary>
    /// Tints a block: the faces of its models with a <c>tintindex</c> are multiplied by the tint's colour, and so are
    /// its item's when the item's definition (<c>assets/&lt;ns&gt;/items/...</c>) gives the model no tints of its
    /// own.
    /// </summary>
    /// <param name="block">The block's identifier, like <c>examplemod:mossy_bricks</c>.</param>
    /// <param name="tint">The tint.</param>
    /// <exception cref="ArgumentException">The identifier is a vanilla block's.</exception>
    public IClientRegistry RegisterBlockTint(string block, BlockTint tint);

    /// <summary>
    /// Draws a mod's entity type with a model and texture from its assets. Without one, the type's entities aren't
    /// drawn.
    /// </summary>
    /// <param name="entityType">The entity type's identifier, like <c>examplemod:golem</c>.</param>
    /// <param name="renderer">The model and texture.</param>
    /// <exception cref="ArgumentException">The identifier is a vanilla entity type's.</exception>
    public IClientRegistry RegisterEntityRenderer(string entityType, EntityRendererProperties renderer);
}
