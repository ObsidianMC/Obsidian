using Obsidian.API.Inventory;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace Obsidian.API.Utilities;

public static partial class Extensions
{
    /// <remarks>
    /// This method is not and shouldn't be used in performance-critical sections.
    /// Source: https://stackoverflow.com/a/1415187
    /// </remarks>
    public static string GetDescription(this Enum value)
    {
        Type type = value.GetType();
        string name = Enum.GetName(type, value);
        if (name != null)
        {
            FieldInfo field = type.GetField(name);
            if (field != null)
            {
                if (Attribute.GetCustomAttribute(field, typeof(DescriptionAttribute)) is DescriptionAttribute attr)
                {
                    return attr.Description;
                }
            }
        }
        return null;
    }

    public static string ToPascalCase(this string snakeCase)
    {
        // Alternative implementation:
        // var textInfo = System.Globalization.CultureInfo.CurrentCulture.TextInfo;
        // return string.Join("", snakeCase.Split('_').Select(s => textInfo.ToTitleCase(s)));
        int spaceCount = 0;
        for (int i = 0; i < snakeCase.Length; i++)
        {
            if (!char.IsLetterOrDigit(snakeCase[i]))
                spaceCount++;
        }

        var result = new char[snakeCase.Length - spaceCount];

        int targetIndex = 0;
        bool wordStart = true;
        for (int i = 0; i < snakeCase.Length; i++)
        {
            char c = snakeCase[i];
            if (char.IsLetterOrDigit(c))
            {
                result[targetIndex++] = wordStart ? char.ToUpper(c) : char.ToLower(c);
                wordStart = false;
            }
            else
            {
                wordStart = true;
            }
        }

        return new string(result);
    }

    public static EnchantmentType ToEnchantType(this string source) => Enum.Parse<EnchantmentType>(source.Split(":")[1].Replace("_", ""), true);


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsNullOrEmpty([NotNullWhen(false)] this string? value) => string.IsNullOrEmpty(value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsNullOrWhitespace([NotNullWhen(false)] this string? value) => string.IsNullOrWhiteSpace(value);

    public static string Capitalize(this string? value)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (value.IsNullOrEmpty() || char.IsUpper(value[0]))
            return value;

        return string.Create(value.Length, value, (span, source) =>
        {
            source.AsSpan().CopyTo(span);
            span[0] = char.ToUpper(span[0]);
        });
    }

    public static bool EqualsIgnoreCase(this string a, string b) => a.Equals(b, StringComparison.OrdinalIgnoreCase);

    public static string ToSnakeCase(this string str) => JsonNamingPolicy.SnakeCaseLower.ConvertName(str);

    /// <summary>
    /// Trims resource tag from the start and removes '_' characters.
    /// </summary>
    public static string TrimResourceTag(this string value, bool keepUnderscores = false)
    {
        var values = value.Split(':');

        var resourceLocationLength = values[0].Length + 1;

        int length = value.Length - resourceLocationLength;

        if (!keepUnderscores)
            length -= value.Count(c => c == '_');

        return string.Create(length, value, (span, source) =>
        {
            int sourceIndex = resourceLocationLength;
            for (int i = 0; i < span.Length;)
            {
                char sourceChar = source[sourceIndex];

                if (keepUnderscores)
                {
                    span[i] = sourceChar;
                    i++;
                }
                else if (sourceChar != '_')
                {
                    span[i] = sourceChar;
                    i++;
                }
                sourceIndex++;
            }
        });
    }

    //[Obsolete("Do not use. Kept as a masterpiece.")]
    // This method is an absolute masterpiece. Its author must've entered
    // the highest plane of existance when writing it. The purpose of this
    // method is to make a string camelCase, but at all places where it was
    // used, ToLower immediately followed. Effectively it only removes '_'.
    //
    // What makes this method special is its mind-boggling implementation.
    // 1. At each call new CultureInfo is created.
    // 2. String is split into words using regex (split by uppercase letters).
    // 3. Words are joined together with whitespace.
    // 4. String is made lowercase.
    // 5. String is made Title Case.
    // 6. Whitespaces are removed.
    // 7. All characters are enumerated, each is checked for being the first
    //    and conditionally changed to lowercase.
    // 8. Char enumerable is turned into an array.
    // 9. New string is made from the array.
    //
    // The text appears 8 times in memory, in different forms (including the
    // returned string). That means that every call produces around 7 * str.Length
    // * sizeof(char) bytes of garbage.
    //public static string ToCamelCase(this string str)
    //{
    //    return new string(
    //      new CultureInfo("en-US", false)
    //        .TextInfo
    //        .ToTitleCase(string.Join(" ", pattern.Matches(str)).ToLower())
    //        .Replace(@" ", "")
    //        .Select((x, i) => i == 0 ? char.ToLower(x) : x)
    //        .ToArray()
    //    );
    //}
}
