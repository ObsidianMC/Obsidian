using System.Text.Json;

namespace Obsidian.SourceGenerators.Registry;

internal static class NameHelper
{
    public static string RemoveNamespace(this string namespacedName)
    {
        return namespacedName.Substring(namespacedName.IndexOf(":") + 1);
    }

    public static string ToPascalCase(this string value)
    {
        // Alternative implementation:
        // var textInfo = System.Globalization.CultureInfo.CurrentCulture.TextInfo;
        // return string.Join("", snakeCase.Split('_').Select(s => textInfo.ToTitleCase(s)));

        //Assume its camelCase
        if (!value.Contains("_"))
            return new string([char.ToUpper(value[0]), .. value.Substring(1)]);

        int spaceCount = 0;
        for (int i = 0; i < value.Length; i++)
        {
            if (!char.IsLetterOrDigit(value[i]))
                spaceCount++;
        }

        var result = new char[value.Length - spaceCount];

        int targetIndex = 0;
        bool wordStart = true;
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
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
}
