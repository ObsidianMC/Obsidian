using System.Collections.Concurrent;

namespace Obsidian.API.Registry.Codecs;

/// <summary>
/// Provides a named concurrent dictionary for registry codec entries.
/// </summary>
public class CodecCollection<K, V> : ConcurrentDictionary<K, V> where K : notnull
{
    /// <summary>
    /// Gets the name associated with this codec collection.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Creates an empty codec collection with the supplied name.
    /// </summary>
    public CodecCollection(string name) : base(new Dictionary<K, V>()) { this.Name = name; }

    /// <summary>
    /// Attempts to insert an entry, returning <c>false</c> when the key already exists.
    /// </summary>
    public bool Add(K key, V value) => this.TryAdd(key, value);
}
