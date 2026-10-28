import java.io.IOException;
import java.io.UncheckedIOException;
import java.lang.reflect.Constructor;
import java.lang.reflect.Field;
import java.lang.reflect.InvocationTargetException;
import java.lang.reflect.Method;
import java.net.URISyntaxException;
import java.nio.file.Path;
import java.util.List;
import java.util.Map;
import java.util.function.Predicate;
import java.util.jar.JarFile;

/**
 * Reflection over the vanilla jar on the class path (the server's or the installed client's), by vanilla's own names:
 * Mojang's jars aren't obfuscated since 26.1. The dumpers reach vanilla through these instead of compiling against it,
 * so that a class or member that a version renames fails with its name rather than the whole dumper failing to compile.
 *
 * <p>Types are given as Java names ({@code net.minecraft.core.BlockPos}, nested classes after {@code $}), primitives
 * ({@code int}) or arrays ({@code byte[]}).
 */
final class Vanilla {
    private static final Map<String, Class<?>> PRIMITIVES = Map.of("int", int.class, "boolean", boolean.class,
        "long", long.class, "double", double.class, "float", float.class, "byte", byte.class, "short", short.class,
        "char", char.class);

    // The vanilla jar and its class names (sorted), found when first needed.
    private static Path vanillaJar;
    private static List<String> classNames;

    private Vanilla() {
    }

    /** A class by name, or a Java type name ({@code int}, {@code java.lang.Object}, arrays with {@code []}). */
    static Class<?> type(String name) {
        if (name.endsWith("[]"))
            return type(name.substring(0, name.length() - 2)).arrayType();
        if (PRIMITIVES.containsKey(name))
            return PRIMITIVES.get(name);
        try {
            return Class.forName(name, false, Vanilla.class.getClassLoader());
        } catch (ClassNotFoundException e) {
            throw new IllegalStateException("Missing class " + name, e);
        }
    }

    /** The names of the vanilla classes directly in a package (not its subpackages), sorted. */
    static List<String> classesIn(String packageName) {
        return classes(name -> name.lastIndexOf('.') == packageName.length() && name.startsWith(packageName + ".")
            && !name.endsWith(".package-info"));
    }

    /** The names of the vanilla classes matching a filter, sorted. */
    static List<String> classes(Predicate<String> filter) {
        return classNames().stream().filter(filter).toList();
    }

    /** Whether a class is vanilla's own (from the vanilla jar), rather than the JDK's or a library's. */
    static boolean isVanilla(Class<?> type) {
        return type.getClassLoader() != null && type.getProtectionDomain().getCodeSource() != null
            && vanillaJar().equals(jarOf(type));
    }

    /** The simple name of a class, like {@code StairBlock} (the part after the last '.' or '$'). */
    static String simpleName(Class<?> type) {
        var name = type.getName();
        return name.substring(Math.max(name.lastIndexOf('.'), name.lastIndexOf('$')) + 1);
    }

    /** The name of a class without its package, like {@code BossEvent.BossBarColor} for a nested class. */
    static String nestedName(Class<?> type) {
        var name = type.getName();
        return name.substring(name.lastIndexOf('.') + 1).replace('$', '.');
    }

    static Field field(String owner, String name) {
        try {
            var field = type(owner).getDeclaredField(name);
            field.setAccessible(true);
            return field;
        } catch (NoSuchFieldException e) {
            throw new IllegalStateException("Missing field " + owner + "." + name, e);
        }
    }

    static Method method(String owner, String name, String... parameterTypes) {
        try {
            var method = type(owner).getDeclaredMethod(name, types(parameterTypes));
            method.setAccessible(true);
            return method;
        } catch (NoSuchMethodException e) {
            throw new IllegalStateException("Missing method " + owner + "." + name + "(" + String.join(",", parameterTypes) + ")", e);
        }
    }

    static Constructor<?> constructor(String owner, String... parameterTypes) {
        try {
            var constructor = type(owner).getDeclaredConstructor(types(parameterTypes));
            constructor.setAccessible(true);
            return constructor;
        } catch (NoSuchMethodException e) {
            throw new IllegalStateException("Missing constructor " + owner + "(" + String.join(",", parameterTypes) + ")", e);
        }
    }

    /** A static field's value. */
    static Object get(String owner, String name) {
        return getStatic(field(owner, name));
    }

    static Object call(Method method, Object target, Object... arguments) {
        try {
            return method.invoke(target, arguments);
        } catch (InvocationTargetException e) {
            throw new IllegalStateException("Vanilla's " + method + " failed", e.getCause());
        } catch (IllegalAccessException e) {
            throw new IllegalStateException(e);
        }
    }

    static Object create(Constructor<?> constructor, Object... arguments) {
        try {
            return constructor.newInstance(arguments);
        } catch (InvocationTargetException e) {
            throw new IllegalStateException("Vanilla's " + constructor + " failed", e.getCause());
        } catch (ReflectiveOperationException e) {
            throw new IllegalStateException(e);
        }
    }

    static Object getStatic(Field field) {
        field.setAccessible(true);
        return get(field, null);
    }

    static Object get(Field field, Object target) {
        try {
            return field.get(target);
        } catch (IllegalAccessException e) {
            throw new IllegalStateException(e);
        }
    }

    static int getInt(Field field, Object target) {
        try {
            return field.getInt(target);
        } catch (IllegalAccessException e) {
            throw new IllegalStateException(e);
        }
    }

    static double getDouble(Field field, Object target) {
        try {
            return field.getDouble(target);
        } catch (IllegalAccessException e) {
            throw new IllegalStateException(e);
        }
    }

    private static Class<?>[] types(String[] names) {
        var types = new Class<?>[names.length];
        for (var i = 0; i < types.length; i++)
            types[i] = type(names[i]);
        return types;
    }

    // The jar on the class path that SharedConstants, which every server and client jar has, comes from.
    private static synchronized Path vanillaJar() {
        if (vanillaJar == null)
            vanillaJar = jarOf(type("net.minecraft.SharedConstants"));
        return vanillaJar;
    }

    private static Path jarOf(Class<?> type) {
        try {
            return Path.of(type.getProtectionDomain().getCodeSource().getLocation().toURI());
        } catch (URISyntaxException e) {
            throw new IllegalStateException(e);
        }
    }

    private static synchronized List<String> classNames() {
        if (classNames == null) {
            try (var jar = new JarFile(vanillaJar().toFile())) {
                classNames = jar.stream()
                    .map(entry -> entry.getName())
                    .filter(name -> name.endsWith(".class") && !name.startsWith("META-INF/"))
                    .map(name -> name.substring(0, name.length() - ".class".length()).replace('/', '.'))
                    .sorted()
                    .toList();
            } catch (IOException e) {
                throw new UncheckedIOException(e);
            }
        }
        return classNames;
    }
}
