using System.Collections.Concurrent;

namespace Obsidian.API.Registry.Codecs;

public class CodecCollection<K, V> : ConcurrentDictionary<K, V>
{
    public string Name { get; }

    public CodecCollection(string name) : base(new Dictionary<K, V>()) { this.Name = name; }
}
