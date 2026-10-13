using System.Text;
using System.Text.RegularExpressions;

namespace Obsidian.Plugins;

/// <summary>
/// Reads Java regular expressions (<c>java.util.regex.Pattern</c>), like the patterns of data packs' filters, as .NET
/// ones.
/// </summary>
/// <remarks>
/// The two syntaxes mostly agree. This rewrites what Java has and .NET doesn't: <c>\Q...\E</c> quoting, whose text is
/// matched as is, and possessive quantifiers (<c>a*+</c>, <c>a++</c>, <c>a?+</c>, <c>a{1,2}+</c>), which become
/// atomic groups. Constructs .NET doesn't know either, like Java's POSIX classes (<c>\p{Lower}</c>) or class
/// intersections (<c>[a-z&amp;&amp;[^b]]</c>), are left as they are, so .NET may refuse them. Java's ASCII-only
/// <c>\w</c>, <c>\d</c> and <c>\s</c> match the same as .NET's on identifiers, which are ASCII.
/// </remarks>
internal static partial class JavaRegex
{
    /// <exception cref="ArgumentException">The pattern isn't valid for .NET.</exception>
    public static Regex Create(string pattern) => new(Translate(pattern), RegexOptions.CultureInvariant);

    /// <summary>
    /// Rewrites a Java pattern's Java-only syntax as .NET's.
    /// </summary>
    public static string Translate(string pattern)
    {
        var output = new StringBuilder(pattern.Length);

        // Where the atom a quantifier after it repeats starts in the output: a character, an escape, a class or a
        // group. -1 after an anchor, an alternation or a quantifier.
        var atom = -1;
        var groups = new Stack<int>();
        var i = 0;

        while (i < pattern.Length)
        {
            var c = pattern[i];

            if (c == '\\' && At(pattern, i + 1, 'Q'))
            {
                i = Quote(pattern, i + 2, output, ref atom);
                continue;
            }

            if (c == '\\')
            {
                var escape = EscapeLength(pattern, i);
                atom = output.Length;
                output.Append(pattern, i, escape);
                i += escape;
                continue;
            }

            if (c == '[')
            {
                atom = output.Length;
                i = CharacterClass(pattern, i, output);
                continue;
            }

            if (c is '*' or '+' or '?' or '{' && atom >= 0 && QuantifierLength(pattern, i) is int length and > 0)
            {
                output.Append(pattern, i, length);
                i += length;

                if (At(pattern, i, '+'))
                {
                    // Possessive: the atom and its quantifier as an atomic group, which .NET has.
                    output.Insert(atom, "(?>");
                    output.Append(')');
                    i++;
                }
                else if (At(pattern, i, '?'))
                {
                    output.Append('?');
                    i++;
                }

                atom = -1;
                continue;
            }

            switch (c)
            {
                case '(':
                    groups.Push(output.Length);
                    atom = -1;
                    break;
                case ')':
                    atom = groups.Count > 0 ? groups.Pop() : -1;
                    break;
                case '|' or '^' or '$':
                    atom = -1;
                    break;
                default:
                    atom = output.Length;
                    break;
            }

            output.Append(c);
            i++;
        }

        return output.ToString();
    }

    private static bool At(string pattern, int index, char c) => index < pattern.Length && pattern[index] == c;

    // \Q...\E (or up to the end): each character as a literal, escaped where it'd mean something. A quantifier after
    // it repeats the last character, as in Java.
    private static int Quote(string pattern, int start, StringBuilder output, ref int atom)
    {
        var end = pattern.IndexOf("\\E", start, StringComparison.Ordinal);
        var quoted = end < 0 ? pattern[start..] : pattern[start..end];

        foreach (var c in quoted)
        {
            atom = output.Length;

            if (!char.IsAsciiLetterOrDigit(c))
                output.Append('\\');

            output.Append(c);
        }

        return end < 0 ? pattern.Length : end + 2;
    }

    // A character class, up to its closing bracket: a bracket first in it is a literal, and \Q...\E is quoted in it
    // too.
    private static int CharacterClass(string pattern, int start, StringBuilder output)
    {
        var i = start + 1;
        output.Append('[');

        if (At(pattern, i, '^'))
        {
            output.Append('^');
            i++;
        }

        var first = true;

        while (i < pattern.Length)
        {
            var c = pattern[i];

            if (c == ']' && !first)
            {
                output.Append(']');
                return i + 1;
            }

            first = false;

            if (c == '\\' && At(pattern, i + 1, 'Q'))
            {
                var ignored = -1;
                i = Quote(pattern, i + 2, output, ref ignored);
                continue;
            }

            var length = c == '\\' ? EscapeLength(pattern, i) : 1;
            output.Append(pattern, i, length);
            i += length;
        }

        return i;
    }

    // An escape's length: \p{Name}, \x{h...h}, \k<name>, \uhhhh, \xhh, \cX, or a backslash and one character.
    private static int EscapeLength(string pattern, int start)
    {
        if (start + 1 >= pattern.Length)
            return pattern.Length - start;

        var next = pattern[start + 1];
        var closing = next switch
        {
            'p' or 'P' or 'x' when At(pattern, start + 2, '{') => '}',
            'k' when At(pattern, start + 2, '<') => '>',
            _ => '\0'
        };

        if (closing != '\0')
        {
            var end = pattern.IndexOf(closing, start + 3);
            return end < 0 ? pattern.Length - start : end - start + 1;
        }

        var length = next switch
        {
            'u' => 6,
            'x' => 4,
            'p' or 'P' or 'c' => 3,
            _ => 2
        };

        return Math.Min(length, pattern.Length - start);
    }

    // A quantifier's length, without a lazy or possessive suffix: *, +, ?, or {n}, {n,} or {n,m}; 0 when a brace
    // doesn't start one.
    private static int QuantifierLength(string pattern, int start)
    {
        if (pattern[start] != '{')
            return 1;

        var match = Bounds().Match(pattern, start);
        return match.Success ? match.Length : 0;
    }

    [GeneratedRegex(@"\G\{\d+(,\d*)?\}")]
    private static partial Regex Bounds();
}
