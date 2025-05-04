using Microsoft.Extensions.ObjectPool;

namespace Obsidian.Utilities.Collections;
public sealed class SimpleObjectPool<T> : ObjectPool<T> where T : class, new()
{
    private readonly ConcurrentQueue<T> queue;

    private static ObjectPool<T> sharedPool;
    public static ObjectPool<T> Shared => sharedPool ??= new SimpleObjectPool<T>();

    public SimpleObjectPool(int initialCapacity)
    {
        this.queue = new ConcurrentQueue<T>();

        for (int i = 0; i < initialCapacity; i++)
        {
            this.queue.Enqueue(new T());
        }
    }

    public SimpleObjectPool()
    {
        this.queue = new ConcurrentQueue<T>();
    }

    public override T Get() => this.queue.TryDequeue(out var obj) ? obj : new T();
    public override void Return(T obj) => this.queue.Enqueue(obj);
}
