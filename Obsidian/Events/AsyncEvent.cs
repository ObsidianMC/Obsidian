using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;

namespace Obsidian.Events;

public sealed class AsyncEvent : IEventRegistry
{
    public string? Name { get; } // Name must be set in order to be visible to plugins

    private readonly SemaphoreSlim semaphore = new(1);
    private readonly List<Hook> hooks = [];
    private readonly Action<AsyncEvent, Exception>? exceptionHandler;

    public AsyncEvent()
    {
    }

    public AsyncEvent(string? name, Action<AsyncEvent, Exception>? exceptionHandler)
    {
        Name = name;
        this.exceptionHandler = exceptionHandler;
    }

    public void Register(Hook handler)
    {
        semaphore.Wait();
        hooks.Add(handler);
        semaphore.Release();
    }

    public async Task RegisterAsync(Hook handler)
    {
        await semaphore.WaitAsync();
        hooks.Add(handler);
        semaphore.Release();
    }

    public void Unregister(Hook handler)
    {
        semaphore.Wait();
        for (int i = 0; i < hooks.Count; i++)
        {
            if (hooks[i] == handler)
            {
                hooks.RemoveAt(i);
                semaphore.Release();
                return;
            }
        }
        semaphore.Release();
    }

    public async Task UnregisterAsync(Hook handler)
    {
        await semaphore.WaitAsync();
        for (int i = 0; i < hooks.Count; i++)
        {
            if (hooks[i] == handler)
            {
                hooks.RemoveAt(i);
                semaphore.Release();
                return;
            }
        }
        semaphore.Release();
    }

    public async ValueTask InvokeAsync()
    {
        // This might block hook registration, but that is not expected to happen so often,
        // unlike invoking events. That's why it's better to just lock the collection instead
        // of copying all the elements at each invocation. Another solution could be keeping an
        // extra copy (and dirty flag), since we expect hooks to only contain few elements.
        // This could be a future subject to change.
        await semaphore.WaitAsync();

        for (int i = 0; i < hooks.Count; i++)
        {
            try
            {
                await hooks[i].InvokeAsync();
            }
            catch (Exception e)
            {
                exceptionHandler?.Invoke(this, e);
            }
        }
        semaphore.Release();
    }

    public bool TryRegisterEvent(MethodInfo method, object? instance, out Delegate? @delegate)
    {
        if (Hook.TryCreateHook(method, instance, out var hook))
        {
            Register(hook);
            @delegate = hook.Delegate;
            return true;
        }

        @delegate = null;
        return false;
    }

    public bool UnregisterEvent(Delegate @delegate)
    {
        semaphore.Wait();
        for (int i = 0; i < hooks.Count; i++)
        {
            if (hooks[i].Delegate == @delegate)
            {
                hooks.RemoveAt(i);
                semaphore.Release();
                return true;
            }
        }
        semaphore.Release();
        return false;
    }

    public static AsyncEvent operator +(AsyncEvent asyncEvent, Hook.VoidMethod method)
    {
        asyncEvent ??= new();
        asyncEvent.Register(new Hook(method));
        return asyncEvent;
    }

    public static AsyncEvent operator -(AsyncEvent asyncEvent, Hook.VoidMethod method)
    {
        if (asyncEvent is null)
        {
            return new AsyncEvent();
        }

        asyncEvent.Unregister(new Hook(method));
        return asyncEvent;
    }

    public static AsyncEvent operator +(AsyncEvent asyncEvent, Hook.ValueTaskMethod method)
    {
        asyncEvent ??= new();
        asyncEvent.Register(new Hook(method));
        return asyncEvent;
    }

    public static AsyncEvent operator -(AsyncEvent asyncEvent, Hook.ValueTaskMethod method)
    {
        if (asyncEvent is null)
        {
            return new AsyncEvent();
        }

        asyncEvent.Unregister(new Hook(method));
        return asyncEvent;
    }

    public static AsyncEvent operator +(AsyncEvent asyncEvent, Hook.TaskMethod method)
    {
        asyncEvent ??= new();
        asyncEvent.Register(new Hook(method));
        return asyncEvent;
    }

    public static AsyncEvent operator -(AsyncEvent asyncEvent, Hook.TaskMethod method)
    {
        if (asyncEvent is null)
        {
            return new AsyncEvent();
        }

        asyncEvent.Unregister(new Hook(method));
        return asyncEvent;
    }

    [StructLayout(LayoutKind.Explicit)]
    public readonly struct Hook
    {
        public delegate void VoidMethod();
        public delegate ValueTask ValueTaskMethod();
        public delegate Task TaskMethod();

        public Delegate? Delegate => GetDelegate();

        [FieldOffset(0)]
        private readonly VoidMethod voidMethod;

        [FieldOffset(0)]
        private readonly ValueTaskMethod valueTaskMethod;

        [FieldOffset(0)]
        private readonly TaskMethod taskMethod;

        [FieldOffset(8)]
        private readonly MethodType methodType;

        public Hook(VoidMethod method)
        {
            ArgumentNullException.ThrowIfNull(method);

            methodType = MethodType.Void;
            voidMethod = method;
            Unsafe.SkipInit(out valueTaskMethod);
            Unsafe.SkipInit(out taskMethod);
        }

        public Hook(ValueTaskMethod method)
        {
            ArgumentNullException.ThrowIfNull(method);

            methodType = MethodType.ValueTask;
            valueTaskMethod = method;
            Unsafe.SkipInit(out voidMethod);
            Unsafe.SkipInit(out taskMethod);
        }

        public Hook(TaskMethod method)
        {
            ArgumentNullException.ThrowIfNull(method);

            methodType = MethodType.Task;
            taskMethod = method;
            Unsafe.SkipInit(out voidMethod);
            Unsafe.SkipInit(out valueTaskMethod);
        }

        public async ValueTask InvokeAsync()
        {
            switch (methodType)
            {
                case MethodType.Void:
                    voidMethod();
                    break;

                case MethodType.ValueTask:
                    await valueTaskMethod();
                    break;

                case MethodType.Task:
                    await taskMethod().ConfigureAwait(false);
                    break;
            }
        }

        public static bool TryCreateHook(MethodInfo method, object? instance, out Hook hook)
        {
            var returnType = method.ReturnType;
            if (returnType == typeof(void))
            {
                if (Delegate.CreateDelegate(typeof(VoidMethod), instance, method, throwOnBindFailure: false) is VoidMethod @delegate)
                {
                    hook = new Hook(@delegate);
                    return true;
                }
            }
            else if (returnType == typeof(ValueTask))
            {
                if (Delegate.CreateDelegate(typeof(ValueTaskMethod), instance, method, throwOnBindFailure: false) is ValueTaskMethod @delegate)
                {
                    hook = new Hook(@delegate);
                    return true;
                }
            }
            else if (returnType == typeof(Task))
            {
                if (Delegate.CreateDelegate(typeof(TaskMethod), instance, method, throwOnBindFailure: false) is TaskMethod @delegate)
                {
                    hook = new Hook(@delegate);
                    return true;
                }
            }

            hook = default;
            return false;
        }

        private Delegate? GetDelegate()
        {
            return methodType switch
            {
                MethodType.Void => voidMethod,
                MethodType.ValueTask => valueTaskMethod,
                MethodType.Task => taskMethod,
                _ => null
            };
        }

        public override bool Equals(object? obj)
        {
            return obj is Hook other && this == other;
        }

        public override int GetHashCode()
        {
            return RuntimeHelpers.GetHashCode(Delegate);
        }

        public override string? ToString()
        {
            return $"{methodType} Hook";
        }

        public static bool operator ==(Hook a, Hook b)
        {
            return a.Delegate == b.Delegate;
        }

        public static bool operator !=(Hook a, Hook b)
        {
            return !(a == b);
        }

        private enum MethodType
        {
            Void = 0,
            ValueTask = 1,
            Task = 2
        }
    }
}
