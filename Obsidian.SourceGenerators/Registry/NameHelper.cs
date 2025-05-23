using System.Linq;
using System.Text.Json;

namespace Obsidian.SourceGenerators.Registry;

internal static class NameHelper
{
    private static readonly string[] invalidBlockNames = ["TrialSpawner", "Vault", "Obsidian", "CreakingHeart"];

    public static string SanitizeBlockName(this string blockName) => 
        invalidBlockNames.Contains(blockName) ? $"{blockName}Block" : blockName;

    public static string RemoveNamespace(this string namespacedName)
    {
        return namespacedName.Substring(namespacedName.IndexOf(":") + 1);
    }

    public static string ToPascalCase(this string value)
    {
        int spaceCount = 0;
        for (int i = 0; i < value.Length; i++)
        {
            if (!char.IsLetterOrDigit(value[i]))
                spaceCount++;

            //Checks if any of the chars is upper as if they are its assumed the string is in camelCase format.
            if (char.IsUpper(value[i]))
                return char.ToUpper(value[0]) + value.Substring(1);
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
