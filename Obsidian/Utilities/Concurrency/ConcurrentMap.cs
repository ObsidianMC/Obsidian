using System;
using System.Collections.Generic;
using System.Threading;

namespace Obsidian.Utilities.Concurrency
{
    public sealed class ConcurrentMap<TKey, TValue> : IDisposable
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

        ~ConcurrentMap()
        {
            mapLock.Dispose();
        }
    }
}
