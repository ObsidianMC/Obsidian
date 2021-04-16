using Obsidian.API;
using System;
using System.Collections.Generic;
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
        public static bool IsNullOrEmpty(this string value) => string.IsNullOrEmpty(value);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsNullOrWhitespace(this string value) => string.IsNullOrWhiteSpace(value);

        public static string Capitalize(this string value)
        {
            if (value is null)
                throw new ArgumentNullException(nameof(value));

            if (value.Length == 0)
                return value;

            if (value.Length < 1024)
            {
                Span<char> span = stackalloc char[value.Length];
                span[0] = char.ToUpper(value[0]);

                var valueSpan = value.AsSpan(1);
                valueSpan.CopyTo(span[1..]);

                return new string(span);
            }

            return char.ToUpper(value[0]) + value[1..];
        }

        public static bool EqualsIgnoreCase(this string a, string b) => a.Equals(b, StringComparison.OrdinalIgnoreCase);

        public static string ToSnakeCase(this string str) => string.Join("_", pattern.Matches(str)).ToLower();

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
