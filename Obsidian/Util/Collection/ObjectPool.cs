using System;
using System.Collections.Concurrent;

namespace Obsidian.Util.Collection
{
    public abstract class ObjectPool<T> where T : new()
    {
        private static SharedPool<T> sharedPool;
        public static ObjectPool<T> Shared => sharedPool ??= new SharedPool<T>();
        
        public ObjectPool(Func<T> initializer)
        {
        }

        public abstract T Rent();
        public abstract void Return(T t);

        private sealed class SharedPool<TObject> : ObjectPool<TObject> where TObject : new()
        {
            private readonly ConcurrentStack<TObject> pool = new();
            
            public SharedPool() : base(() => new TObject())
            {
            }
            
            public override TObject Rent()
            {
                if (pool.TryPop(out var obj))
                {
                    return obj;
                }
                return new TObject();
            }

            public override void Return(TObject u)
            {
                pool.Push(u);
            }
        }
    }
}
