import java.nio.file.*;
import java.util.*;
import java.util.regex.*;

/** Resolves explicit symbols in our fixture probes against the already-used Mojang mappings.
 * The resulting Java source lives only in the ignored generator work directory. No game code is transformed.
 * Each directive is: // @map TOKEN fully.qualified.Type[#field or #method(parameter,types)]
 */
class FixtureSource {
    public static void main(String[] args) throws Exception {
        var mappings = VanillaDumper.Mojang.read(Path.of(args[0]));
        var source = Files.readString(Path.of(args[1]));
        var replacements = new HashMap<String, String>();
        var directives = Pattern.compile("(?m)^// @map (\\w+) (.+)$").matcher(source);
        while (directives.find()) {
            String symbol = directives.group(2).trim();
            int member = symbol.indexOf('#');
            String value;
            if (member < 0) value = mappings.type(symbol).getCanonicalName();
            else {
                String owner = symbol.substring(0, member), name = symbol.substring(member + 1);
                int parameters = name.indexOf('(');
                if (parameters < 0) value = mappings.field(owner, name).getName();
                else {
                    String list = name.substring(parameters + 1, name.length() - 1);
                    value = mappings.method(owner, name.substring(0, parameters), list.isEmpty() ? new String[0] : list.split(",")).getName();
                }
            }
            replacements.put(directives.group(1), value);
        }
        source = source.replaceAll("(?m)^// @map .+\\R?", "");
        var tokens = Pattern.compile("\\bMC_\\w+\\b").matcher(source);
        var resolved = new StringBuilder();
        while (tokens.find()) {
            String value = replacements.get(tokens.group());
            if (value == null) throw new IllegalArgumentException("Unmapped fixture symbol: " + tokens.group());
            tokens.appendReplacement(resolved, Matcher.quoteReplacement(value));
        }
        tokens.appendTail(resolved);
        var output = Path.of(args[2]);
        Files.createDirectories(output.getParent());
        Files.writeString(output, resolved);
    }
}
