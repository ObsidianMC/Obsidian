namespace Obsidian.API.Client;

/// <summary>
/// A mod's key binding, which players rebind in the key binds screen like vanilla's. Register it with
/// <see cref="IClientRegistry.RegisterKeyBinding"/>.
/// </summary>
public interface IKeyBinding
{
    /// <summary>
    /// The binding's identifier, for example <c>examplemod:open_map</c>.
    /// </summary>
    public string Identifier { get; }

    /// <summary>
    /// The binding's name, which is also its translation key and, after <c>key_</c>, its key in <c>options.txt</c>:
    /// <c>key.examplemod.open_map</c> for <c>examplemod:open_map</c>.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// The key or mouse button it's bound to, in vanilla's names: <c>key.keyboard.g</c>, <c>key.mouse.right</c>, or
    /// <c>key.keyboard.unknown</c> when unbound.
    /// </summary>
    public string Key { get; }

    /// <summary>
    /// Whether the key is held down after being pressed in game.
    /// </summary>
    public bool IsDown { get; }

    /// <summary>
    /// Raised on the game's thread when the key is pressed in game, with no screen open.
    /// </summary>
    public event Action? Pressed;
}
