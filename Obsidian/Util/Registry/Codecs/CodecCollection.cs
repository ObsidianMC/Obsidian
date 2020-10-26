using System.Collections.Concurrent;
using System.Collections.Generic;

namespace Obsidian.Util.Registry.Codecs
{
    public class CodecCollection<K, V> : ConcurrentDictionary<K, V>
    {
        public string Name { get; }

        public CodecCollection(string name) : base(new Dictionary<K, V>()) { this.Name = name; }
    }
}
