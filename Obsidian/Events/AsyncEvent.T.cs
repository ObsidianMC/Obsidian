using Obsidian.API.Events;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Obsidian.Events;

public sealed class AsyncEvent<T> where T : BaseMinecraftEventArgs, INamedEvent
{
    public string Name { get; } // Name must be set in order to be visible to plugins

    private readonly SemaphoreSlim semaphore = new(1);
    private readonly List<Hook<T>> hooks = [];
    private readonly Action<AsyncEvent<T>, Exception>? exceptionHandler;

    public AsyncEvent()
    {
        this.Name = T.Name;
    }

    public AsyncEvent(Action<AsyncEvent<T>, Exception>? exceptionHandler)
    {
        Name = T.Name;
        this.exceptionHandler = exceptionHandler;
    }

    public AsyncEvent(string name, Action<AsyncEvent<T>, Exception>? exceptionHandler)
    {
        Name = name;
        this.exceptionHandler = exceptionHandler;
    }

    public void Register(Hook<T> handler)
    {
        semaphore.Wait();
        hooks.Add(handler);
        semaphore.Release();
    }

    public async Task RegisterAsync(Hook<T> handler)
    {
        await semaphore.WaitAsync();
        hooks.Add(handler);
        semaphore.Release();
    }

    public void Unregister(Hook<T> handler)
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

    public async Task UnregisterAsync(Hook<T> handler)
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

    public async ValueTask<T> InvokeAsync(T args)
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
                await hooks[i].InvokeAsync(args);
            }
            catch (Exception e)
            {
                exceptionHandler?.Invoke(this, e);
            }
        }
        semaphore.Release();

        return args;
    }

    public bool TryRegisterEvent(MethodInfo method, object? instance, out Delegate? @delegate)
    {
        if (Hook<T>.TryCreateHook(method, instance, out var hook))
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

    public static AsyncEvent<T> operator +(AsyncEvent<T> asyncEvent, Hook<T>.VoidMethod method)
    {
        asyncEvent ??= new();
        asyncEvent.Register(new Hook<T>(method));
        return asyncEvent;
    }

    public static AsyncEvent<T> operator -(AsyncEvent<T> asyncEvent, Hook<T>.VoidMethod method)
    {
        if (asyncEvent is null)
        {
            return new AsyncEvent<T>();
        }

        asyncEvent.Unregister(new Hook<T>(method));
        return asyncEvent;
    }

    public static AsyncEvent<T> operator +(AsyncEvent<T> asyncEvent, Hook<T>.ValueTaskMethod method)
    {
        asyncEvent ??= new();
        asyncEvent.Register(new Hook<T>(method));
        return asyncEvent;
    }

    public static AsyncEvent<T> operator -(AsyncEvent<T> asyncEvent, Hook<T>.ValueTaskMethod method)
    {
        if (asyncEvent is null)
        {
            return new AsyncEvent<T>();
        }

        asyncEvent.Unregister(new Hook<T>(method));
        return asyncEvent;
    }

    public static AsyncEvent<T> operator +(AsyncEvent<T> asyncEvent, Hook<T>.TaskMethod method)
    {
        asyncEvent ??= new();
        asyncEvent.Register(new Hook<T>(method));
        return asyncEvent;
    }

    public static AsyncEvent<T> operator -(AsyncEvent<T> asyncEvent, Hook<T>.TaskMethod method)
    {
        if (asyncEvent is null)
        {
            return new AsyncEvent<T>();
        }

        asyncEvent.Unregister(new Hook<T>(method));
        return asyncEvent;
    }

    public readonly struct Hook<TArgs>
    {
        public delegate void VoidMethod(TArgs args);
        public delegate ValueTask ValueTaskMethod(TArgs args);
        public delegate Task TaskMethod(TArgs args);

        public Delegate? Delegate => GetDelegate();

        private readonly VoidMethod voidMethod;
        private readonly ValueTaskMethod valueTaskMethod;
        private readonly TaskMethod taskMethod;
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

        public async ValueTask InvokeAsync(TArgs args)
        {
            switch (methodType)
            {
                case MethodType.Void:
                    voidMethod(args);
                    break;

                case MethodType.ValueTask:
                    await valueTaskMethod(args);
                    break;

                case MethodType.Task:
                    await taskMethod(args).ConfigureAwait(false);
                    break;
            }
        }

        public static bool TryCreateHook(MethodInfo method, object? instance, out Hook<TArgs> hook)
        {
            var returnType = method.ReturnType;
            if (returnType == typeof(void))
            {
                if (Delegate.CreateDelegate(typeof(VoidMethod), instance, method, throwOnBindFailure: false) is VoidMethod @delegate)
                {
                    hook = new Hook<TArgs>(@delegate);
                    return true;
                }
            }
            else if (returnType == typeof(ValueTask))
            {
                if (Delegate.CreateDelegate(typeof(ValueTaskMethod), instance, method, throwOnBindFailure: false) is ValueTaskMethod @delegate)
                {
                    hook = new Hook<TArgs>(@delegate);
                    return true;
                }
            }
            else if (returnType == typeof(Task))
            {
                if (Delegate.CreateDelegate(typeof(TaskMethod), instance, method, throwOnBindFailure: false) is TaskMethod @delegate)
                {
                    hook = new Hook<TArgs>(@delegate);
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
            return obj is Hook<TArgs> other && this == other;
        }

        public override int GetHashCode()
        {
            return RuntimeHelpers.GetHashCode(Delegate);
        }

        public override string? ToString()
        {
            return $"{methodType} Hook<T>";
        }

        public static bool operator ==(Hook<TArgs> a, Hook<TArgs> b)
        {
            return a.Delegate == b.Delegate;
        }

        public static bool operator !=(Hook<TArgs> a, Hook<TArgs> b)
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
