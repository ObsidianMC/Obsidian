using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Obsidian.SourceGenerators.Registry
{
    internal static class Extensions
    {
        public static string GetAsset(this GeneratorExecutionContext context, string assetName)
        {
            return context.AdditionalFiles
                .FirstOrDefault(asset => Path.GetFileName(asset.Path) == assetName)?
                .GetText()
                .ToString();
        }

        public static string RemoveNamespace(this string namespacedName)
        {
            return namespacedName.Substring(namespacedName.IndexOf(":") + 1);
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

        public static string ToTitleCase(this string snakeCase)
        {
            // Alternative implementation:
            // var textInfo = System.Globalization.CultureInfo.CurrentCulture.TextInfo;
            // return string.Join(" ", snakeCase.Split('_').Select(s => textInfo.ToTitleCase(s)));

            var result = new char[snakeCase.Length];

            bool wordStart = true;
            for (int i = 0; i < snakeCase.Length; i++)
            {
                char c = snakeCase[i];
                if (char.IsLetterOrDigit(c))
                {
                    result[i] = wordStart ? char.ToUpper(c) : char.ToLower(c);
                    wordStart = false;
                }
                else
                {
                    result[i] = ' ';
                    wordStart = true;
                }
            }

            return new string(result);
        }

        public static string ToCamelCase(this string pascalCase)
        {
            char[] result = pascalCase.ToCharArray();
            result[0] = char.ToLower(result[0]);
            return new string(result);
        }

        public static void Deconstruct<T, U>(this KeyValuePair<T, U> pair, out T t, out U u)
        {
            t = pair.Key;
            u = pair.Value;
        }

        public static void AddSource(this GeneratorExecutionContext context, string name, CodeBuilder content)
        {
            context.AddSource(name, SourceText.From(content.ToString(), Encoding.UTF8));
        }
    }
}
