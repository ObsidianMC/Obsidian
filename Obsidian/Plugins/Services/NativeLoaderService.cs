using Obsidian.API.Plugins;
using Obsidian.API.Plugins.Services;
using Obsidian.Plugins.ServiceProviders;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;

namespace Obsidian.Plugins.Services;

internal class NativeLoaderService : SecuredServiceBase, INativeLoader, IDisposable
{
    internal override PluginPermissions NeededPermission => PluginPermissions.Interop;

    private readonly Dictionary<string, IntPtr> loadedLibraries = new Dictionary<string, IntPtr>();
    private readonly List<IDisposable> wrappers = new List<IDisposable>();

    private static readonly ConstructorInfo exceptionConstructor;
    private static readonly ConstructorInfo intPtrConstructor;
    private static readonly MethodInfo fromIntPtrMethod;
    private static readonly MethodInfo handleGetTarget;

    private static readonly MethodInfo getBytes;
    private static readonly MethodInfo getString;
    private static readonly MethodInfo getDefaultEncoding;
    private static readonly MethodInfo convertToTargetBytes;
    private static readonly Dictionary<Encoding, MethodInfo> getEncodingMethods;

    static NativeLoaderService()
    {
        exceptionConstructor = typeof(SecurityException).GetConstructor(new[] { typeof(string) });
        intPtrConstructor = typeof(IntPtr).GetConstructor(new[] { typeof(long) });
        fromIntPtrMethod = typeof(GCHandle).GetMethod(nameof(GCHandle.FromIntPtr), new[] { typeof(IntPtr) });
        handleGetTarget = typeof(GCHandle).GetMethod($"get_{nameof(GCHandle.Target)}");

        getBytes = typeof(Encoding).GetMethod("GetBytes", new[] { typeof(string) });
        getString = typeof(Encoding).GetMethod("GetString", new[] { typeof(byte[]) });
        convertToTargetBytes = typeof(Encoding).GetMethod("Convert", new[] { typeof(Encoding), typeof(Encoding), typeof(byte[]) });

        var staticPropertyFlags = BindingFlags.Public | BindingFlags.Static | BindingFlags.GetProperty;
        getEncodingMethods = new Dictionary<Encoding, MethodInfo>
            {
                { Encoding.UTF8, typeof(Encoding).GetMethod($"get_{nameof(Encoding.UTF8)}", staticPropertyFlags) },
                { Encoding.UTF32, typeof(Encoding).GetMethod($"get_{nameof(Encoding.UTF32)}", staticPropertyFlags) },
                { Encoding.Unicode, typeof(Encoding).GetMethod($"get_{nameof(Encoding.Unicode)}", staticPropertyFlags) },
                { Encoding.ASCII, typeof(Encoding).GetMethod($"get_{nameof(Encoding.ASCII)}", staticPropertyFlags) },
                { Encoding.BigEndianUnicode, typeof(Encoding).GetMethod($"get_{nameof(Encoding.BigEndianUnicode)}", staticPropertyFlags) }
            };
        getDefaultEncoding = typeof(Encoding).GetMethod($"get_{nameof(Encoding.Default)}", staticPropertyFlags);
    }

    public NativeLoaderService(PluginContainer plugin) : base(plugin)
    {
    }

    public T LoadMethod<T>(string libraryPath) where T : Delegate
    {
        return LoadMethod<T>(libraryPath, typeof(T).Name, Encoding.Default);
    }

    public T LoadMethod<T>(string libraryPath, Encoding stringEncoding) where T : Delegate
    {
        return LoadMethod<T>(libraryPath, typeof(T).Name, stringEncoding);
    }

    public T LoadMethod<T>(string libraryPath, string name) where T : Delegate
    {
        return LoadMethod<T>(libraryPath, name, Encoding.Default);
    }

    public T LoadMethod<T>(string libraryPath, string name, Encoding stringEncoding) where T : Delegate
    {
        if (!IsUsable)
            throw new SecurityException(INativeLoader.SecurityExceptionMessage);

        if (stringEncoding == Encoding.Default)
            stringEncoding = null;
        if (stringEncoding != null && !getEncodingMethods.ContainsKey(stringEncoding))
            throw new NotSupportedException($"Encoding '{stringEncoding.BodyName}' is not supported.");

        #region Getting native method
        if (!loadedLibraries.TryGetValue(libraryPath, out IntPtr library))
        {
            if (NativeLibrary.TryLoad(libraryPath, out library))
                loadedLibraries.Add(libraryPath, library);
            else
                return null;
        }

        if (!NativeLibrary.TryGetExport(library, name, out IntPtr pointer))
            return null;
        #endregion

        T function = Marshal.GetDelegateForFunctionPointer<T>(pointer);
        var wrapper = new NativeCall<T>(this, function);
        IntPtr wrapperPtr = GCHandle.ToIntPtr(wrapper.handle);
        wrappers.Add(wrapper);

        ParameterInfo[] parameters = function.Method.GetParameters();
        Type[] parameterTypes = parameters.Select(parameter => parameter.ParameterType).ToArray();
        Type type = typeof(NativeCall<T>);

        #region Generating secured wrapper method
        var dynamicMethod = new DynamicMethod(name, function.Method.ReturnType, parameterTypes, typeof(NativeCall<>).Module, skipVisibility: true);
        var il = dynamicMethod.GetILGenerator();
        Label nativeCall = il.DefineLabel();

        il.DeclareLocal(typeof(GCHandle));

        il.Emit(OpCodes.Ldc_I8, wrapperPtr.ToInt64());
        il.Emit(OpCodes.Newobj, intPtrConstructor);
        il.Emit(OpCodes.Call, fromIntPtrMethod);
        il.Emit(OpCodes.Stloc_0);
        il.Emit(OpCodes.Ldloca_S, 0);
        il.Emit(OpCodes.Call, handleGetTarget);
        il.Emit(OpCodes.Isinst, type);
        il.Emit(OpCodes.Dup);

        var getIsUsable = type.GetMethod($"get_{nameof(NativeCall<T>.IsUsable)}", BindingFlags.NonPublic | BindingFlags.Instance);
        il.Emit(OpCodes.Callvirt, getIsUsable);
        il.Emit(OpCodes.Brtrue_S, nativeCall);

        il.Emit(OpCodes.Ldstr, INativeLoader.SecurityExceptionMessage);
        il.Emit(OpCodes.Newobj, exceptionConstructor);
        il.Emit(OpCodes.Throw);

        il.MarkLabel(nativeCall);
        var nativeMethod = type.GetField(nameof(NativeCall<T>.nativeMethod), BindingFlags.NonPublic | BindingFlags.Instance);
        il.Emit(OpCodes.Ldfld, nativeMethod);
        for (int i = 0; i < parameters.Length; i++)
        {
            if (parameterTypes[i] == typeof(string) && stringEncoding != null)
            {
                il.Emit(OpCodes.Call, getDefaultEncoding);
                il.Emit(OpCodes.Call, getDefaultEncoding);
                il.Emit(OpCodes.Call, getEncodingMethods[stringEncoding]);
                il.Emit(OpCodes.Call, getDefaultEncoding);

                il.Emit(OpCodes.Ldarg_S, i);
                il.Emit(OpCodes.Call, getBytes);
                il.Emit(OpCodes.Call, convertToTargetBytes);
                il.Emit(OpCodes.Call, getString);
            }
            else
            {
                il.Emit(OpCodes.Ldarg_S, i);
            }
        }
        il.Emit(OpCodes.Callvirt, typeof(T).GetMethod(nameof(Action.Invoke)));
        il.Emit(OpCodes.Ret);
        #endregion

        var securedCall = (T)dynamicMethod.CreateDelegate(typeof(T));
        wrapper.method = securedCall;

        return securedCall;
    }

    public void Dispose()
    {
        foreach ((_, IntPtr value) in loadedLibraries)
        {
            NativeLibrary.Free(value);
        }

        foreach (var wrapper in wrappers)
        {
            wrapper.Dispose();
        }
    }

    private class NativeCall<T> : IDisposable where T : Delegate
    {
        internal T method;
        internal T nativeMethod;
        internal GCHandle handle;

        protected NativeLoaderService service;
        internal bool IsUsable => service.IsUsable;

        public NativeCall(NativeLoaderService service, T nativeMethod)
        {
            this.service = service;
            this.nativeMethod = nativeMethod;
            handle = GCHandle.Alloc(this);
        }

        public void Dispose()
        {
            handle.Free();
        }
    }
}
