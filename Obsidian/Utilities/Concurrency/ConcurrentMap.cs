using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace Obsidian.Utilities.Concurrency
{
    [DebuggerDisplay("Count = {Count}")]
    public sealed class ConcurrentMap<TKey, TValue> : IEnumerable<KeyValuePair<TKey, TValue>>, IDisposable where TKey : notnull
    {
        public int Count => map.Count;

        private readonly ReaderWriterLockSlim mapLock = new();
        private readonly Dictionary<TKey, TValue> map = new();

        public TValue this[TKey key]
        {
            get
            {
                mapLock.EnterReadLock();
                try
                {
                    return map[key];
                }
                finally
                {
                    mapLock.ExitReadLock();
                }
            }

            set
            {
                mapLock.EnterWriteLock();
                try
                {
                    map[key] = value;
                }
                finally
                {
                    mapLock.ExitWriteLock();
                }
            }
        }

        public bool TryAdd(TKey key, TValue value)
        {
            mapLock.EnterWriteLock();
            bool success = key is not null && map.TryAdd(key, value);
            mapLock.ExitWriteLock();
            return success;
        }

        public bool TryGet(TKey key, out TValue value)
        {
            value = default;
            mapLock.EnterReadLock();
            bool success = key is not null && map.TryGetValue(key, out value);
            mapLock.ExitReadLock();
            return success;
        }

        public bool TryRemove(TKey key)
        {
            mapLock.EnterWriteLock();
            bool success = key is not null && map.Remove(key);
            mapLock.ExitWriteLock();
            return success;
        }

        public void Dispose()
        {
            mapLock.Dispose();
            GC.SuppressFinalize(this);
        }

        public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator() => new Enumerator(this);

        IEnumerator IEnumerable.GetEnumerator() => new Enumerator(this);

        ~ConcurrentMap()
        {
            mapLock.Dispose();
        }

        private sealed class Enumerator : IEnumerator<KeyValuePair<TKey, TValue>>
        {
            public KeyValuePair<TKey, TValue> Current => enumerator.Current;
            object IEnumerator.Current => Current;

            private readonly ConcurrentMap<TKey, TValue> map;
            private readonly IEnumerator<KeyValuePair<TKey, TValue>> enumerator;
            private bool locked;

            public Enumerator(ConcurrentMap<TKey, TValue> map)
            {
                this.map = map;
                enumerator = map.GetEnumerator();
            }

            public bool MoveNext()
            {
                if (!locked)
                {
                    locked = true;
                    map.mapLock.EnterReadLock();
                }

                return enumerator.MoveNext();
            }

            public void Reset()
            {
                enumerator.Reset();
                if (locked)
                {
                    map.mapLock.ExitReadLock();
                    locked = false;
                }
            }

            public void Dispose()
            {
                if (locked)
                {
                    map.mapLock.ExitReadLock();
                    locked = false;
                }
            }
        }
    }
}
