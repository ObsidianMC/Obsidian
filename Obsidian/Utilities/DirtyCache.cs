using System.Runtime.CompilerServices;

namespace Obsidian.Utilities;

internal struct DirtyCache<T>
{
    private bool isDirty;
    private T value;
    private readonly Func<T> factory;

    public DirtyCache()
    {
        throw new NotSupportedException();
    }

    public DirtyCache(Func<T> factory)
    {
        this.factory = factory;
        isDirty = true;
        Unsafe.SkipInit(out value);
    }

    public void SetDirty()
    {
        isDirty = true;
    }

    public T GetValue()
    {
        if (isDirty)
        {
            value = factory();
            isDirty = false;
        }
        return value;
    }
}
