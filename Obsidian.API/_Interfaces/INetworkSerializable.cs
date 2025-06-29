namespace Obsidian.API;

/// <summary>
/// Interface for network serializable objects. Due to the nature of Obsidian being a server-side only software,
/// both <see cref="Write"/> and <see cref="Read"/> methods are not always necessary to implement. In that case,
/// a <see cref="NotImplementedException"/> is thrown when the method is unintendedly called.
/// </summary>
/// <typeparam name="TValue">Type of the value to be serialized.</typeparam>
public interface INetworkSerializable<TValue>
{
    /// <summary>
    /// Writes the value to the network stream.
    /// </summary>
    /// <param name="value">Value to be serialized.</param>
    /// <param name="writer">Network stream writer.</param>
    public static abstract void Write(TValue value, INetStreamWriter writer);

    /// <summary>
    /// Reads the value from the network stream.
    /// </summary>
    /// <param name="reader">Network stream reader.</param>
    /// <returns>Deserialized value.</returns>
    public static abstract TValue Read(INetStreamReader reader);
}
