using Obsidian.API;
using System;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace Obsidian.Utilities
{
    public static partial class Extensions
    {
        public static readonly Regex pattern = new(@"[A-Z]{2,}(?=[A-Z][a-z]+[0-9]*|\b)|[A-Z]?[a-z]+[0-9]*|[A-Z]|[0-9]+");

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
                    DescriptionAttribute attr = Attribute.GetCustomAttribute(field, typeof(DescriptionAttribute)) as DescriptionAttribute;
                    if (attr != null)
                    {
                        return attr.Description;
                    }
                }
            }
            return null;
        }

        public static EnchantmentType ToEnchantType(this string source) => Enum.Parse<EnchantmentType>(source.Split(":")[1].Replace("_", ""), true);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsEmpty(this string value) => value.Length == 0;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsNullOrEmpty(this string value) => string.IsNullOrEmpty(value);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsNullOrWhitespace(this string value) => string.IsNullOrWhiteSpace(value);

        public static string Capitalize(this string value)
        {
            if (value is null)
                throw new ArgumentNullException(nameof(value));

            if (value.IsEmpty())
                return value;

            if (char.IsUpper(value[0]))
                return value;

            var result = new string(value);
            ref char firstChar = ref Unsafe.AsRef(result.GetPinnableReference());
            firstChar = char.ToUpper(firstChar);

            return result;
        }

        public static bool EqualsIgnoreCase(this string a, string b) => a.Equals(b, StringComparison.OrdinalIgnoreCase);

        public static string ToSnakeCase(this string str) => string.Join("_", pattern.Matches(str)).ToLower();

        // Trims "minecraft:" from the start and removes '_' characters
        public static string TrimMinecraftTag(this string value)
        {
            const int minecraftTagLength = 10; // "minecraft:"
            ReadOnlySpan<char> source = value.AsSpan(start: minecraftTagLength);
            Span<char> result = stackalloc char[source.Length];
            ref char targetChar = ref result.GetRef();
            int resultLength = 0;
            for (int i = 0; i < source.Length; i++)
            {
                char sourceChar = source[i];
                if (sourceChar != '_')
                {
                    targetChar = sourceChar;
                    targetChar = ref targetChar.MoveNext();
                    resultLength++;
                }
            }
            return new string(result.Slice(0, resultLength));
        }

        [Obsolete("Do not use. Kept as a masterpiece.")]
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
        public static string ToCamelCase(this string str)
        {
            return new string(
              new CultureInfo("en-US", false)
                .TextInfo
                .ToTitleCase(string.Join(" ", pattern.Matches(str)).ToLower())
                .Replace(@" ", "")
                .Select((x, i) => i == 0 ? char.ToLower(x) : x)
                .ToArray()
            );
        }
    }
}
