using System.Collections.Concurrent;

namespace Obsidian.API.Registry.Codecs;

public class CodecCollection<K, V> : ConcurrentDictionary<K, V> where K : notnull
{
    public string Name { get; }

    public CodecCollection(string name) : base(new Dictionary<K, V>()) { this.Name = name; }

    public bool Add(K key, V value) => this.TryAdd(key, value);
}
