import java.net.URL;
import java.net.URLClassLoader;
import java.nio.file.Path;
import java.util.Arrays;

/**
 * Runs BlockLootFixtures from a directory of compiled classes. Those classes are loaded by their own class loader: they
 * share the default package with the server's obfuscated classes, which the launcher's loader would otherwise reject.
 * Usage: BlockLootLauncher.java classesDir [BlockLootFixtures arguments...]
 */
public class BlockLootLauncher {
    public static void main(String[] args) throws Exception {
        var loader = new URLClassLoader(new URL[]{ Path.of(args[0]).toUri().toURL() }, ClassLoader.getSystemClassLoader());
        var main = Class.forName("BlockLootFixtures", true, loader).getMethod("main", String[].class);
        main.invoke(null, (Object) Arrays.copyOfRange(args, 1, args.length));
    }
}
