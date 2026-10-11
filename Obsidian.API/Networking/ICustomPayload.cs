namespace Obsidian.API.Networking;

/// <summary>
/// A custom payload sent over a plugin channel (a <c>minecraft:custom_payload</c> packet). Register it with
/// <see cref="Plugins.IPluginRegistry.MapPayload{TPayload}"/> and send it with
/// <see cref="IClient.SendPayloadAsync{TPayload}(TPayload)"/>.
/// </summary>
/// <remarks>
/// The channel and encoding are what a NeoForge mod declares for the same payload type, so an Obsidian plugin and a
/// NeoForge client mod can talk to each other when both use the same channel id, version and field encoding.
/// </remarks>
/// <typeparam name="TSelf">The payload type itself.</typeparam>
public interface ICustomPayload<TSelf> where TSelf : ICustomPayload<TSelf>
{
    /// <summary>
    /// The namespaced channel id, for example <c>examplemod:sync</c>.
    /// </summary>
    public static abstract string Channel { get; }

    /// <summary>
    /// Reads a payload from the bytes after the channel id.
    /// </summary>
    public static abstract TSelf Read(INetStreamReader reader);

    /// <summary>
    /// Writes this payload's fields; the channel id is written for you.
    /// </summary>
    public void Write(INetStreamWriter writer);
}
