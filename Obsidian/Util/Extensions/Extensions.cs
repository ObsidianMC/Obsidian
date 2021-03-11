using Obsidian.API;
using Obsidian.Entities;
using Obsidian.Items;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Obsidian.Util.Extensions
{
    public static class Extensions
    {
        public static readonly Regex pattern = new Regex(@"[A-Z]{2,}(?=[A-Z][a-z]+[0-9]*|\b)|[A-Z]?[a-z]+[0-9]*|[A-Z]|[0-9]+");

        public static bool IsAir(this ItemStack item) => item == null || item.Type == Material.Air;
        // Source: https://stackoverflow.com/a/1415187
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

        /// <summary>
        /// Gets the new slot value from varying inventory sizes and transforms it to a local inventory slot value
        /// </summary>
        /// <returns>The local slot value for a player inventory</returns>
        public static (int slot, bool forPlayer) GetDifference(this short clickedSlot, int inventorySize)
        {
            inventorySize -= 1;

            int sub = clickedSlot switch
            {
                _ when clickedSlot > inventorySize && (clickedSlot >= 54 && clickedSlot <= 89) => 45,
                _ when clickedSlot > inventorySize && (clickedSlot >= 27 && clickedSlot <= 62) => 18,
                _ when clickedSlot > inventorySize && (clickedSlot >= 17 && clickedSlot <= 52) => 9,
                _ when clickedSlot > inventorySize && (clickedSlot >= 14 && clickedSlot <= 49) => 5,
                _ when clickedSlot > inventorySize && (clickedSlot >= 11 && clickedSlot <= 46) => 2,
                _ when clickedSlot > inventorySize && (clickedSlot >= 10 && clickedSlot <= 45) => 1,
                _ when clickedSlot <= inventorySize => 0,
                _ => 0,
            };

            int add = clickedSlot switch
            {
                _ when clickedSlot > inventorySize && (clickedSlot >= 8 && clickedSlot <= 43) => 1,
                _ when clickedSlot > inventorySize && (clickedSlot >= 5 && clickedSlot <= 40) => 4,
                _ when clickedSlot > inventorySize && (clickedSlot >= 4 && clickedSlot <= 39) => 3,
                _ when clickedSlot > inventorySize && (clickedSlot >= 3 && clickedSlot <= 38) => 6,
                _ when clickedSlot > inventorySize && (clickedSlot >= 2 && clickedSlot <= 37) => 7,
                _ when clickedSlot > inventorySize && (clickedSlot >= 1 && clickedSlot <= 36) => 8,
                _ when clickedSlot <= inventorySize => 0,
                _ => 0
            };

            return (add > 0 ? clickedSlot + add : clickedSlot - sub, sub > 0 || add > 0);
        }

        public static Item GetItem(this ItemStack itemStack)
        {
            return Registry.Registry.GetItem(itemStack.Type);
        }

        public static int ToChunkCoord(this double value) => (int)value >> 4;

        public static int ToChunkCoord(this int value) => value >> 4;

        public static (int x, int z) ToChunkCoord(this PositionF value) => ((int)value.X >> 4, (int)value.Z >> 4);

        public static EnchantmentType ToEnchantType(this string source) => Enum.Parse<EnchantmentType>(source.Split(":")[1].Replace("_", ""), true);

        // this is for ints
        public static int GetUnsignedRightShift(this int value, int s) => value >> s;

        // this is for longs
        public static long GetUnsignedRightShift(this long value, int s) => (long)((ulong)value >> s);

        public static bool IsNullOrEmpty(this string value) => string.IsNullOrEmpty(value);

        public static bool IsNullOrWhitespace(this string value) => string.IsNullOrWhiteSpace(value);

        public static string Capitalize(this string value)
        {
            if (string.IsNullOrEmpty(value))
                throw new NullReferenceException(nameof(value));

            return char.ToUpper(value[0]) + value[1..];
        }

        public static IEnumerable<KeyValuePair<Guid, Player>> Except(this ConcurrentDictionary<Guid, Player> source, params Guid[] uuids)
        {
            return source.Where(x => !uuids.Contains(x.Value.Uuid));
        }

        public static IEnumerable<KeyValuePair<Guid, Player>> Except(this ConcurrentDictionary<Guid, Player> source, params Player[] players)
        {
            var newDict = new Dictionary<Guid, Player>();
            foreach ((Guid uuid, Player player) in source)
            {
                if (players.Any(x => x.Uuid == uuid))
                    continue;

                newDict.Add(uuid, player);
            }

            return newDict;
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

        public static int GetVarIntLength(this int val)
        {
            int amount = 0;
            do
            {
                val >>= 7;
                amount++;
            } while (val != 0);

            return amount;
        }

        public static float NextSingle(this Random random)
        {
            return (float)random.NextDouble();
        }

        // https://gist.github.com/ammaraskar/7b4a3f73bee9dc4136539644a0f27e63
        public static string MinecraftShaDigest(this byte[] data)
        {
            var hash = new SHA1Managed().ComputeHash(data);
            // Reverse the bytes since BigInteger uses little endian
            Array.Reverse(hash);

            var b = new BigInteger(hash);
            // very annoyingly, BigInteger in C# tries to be smart and puts in
            // a leading 0 when formatting as a hex number to allow roundtripping 
            // of negative numbers, thus we have to trim it off.
            if (b < 0)
            {
                // toss in a negative sign if the interpreted number is negative
                return $"-{(-b).ToString("x").TrimStart('0')}";
            }
            else
            {
                return b.ToString("x").TrimStart('0');
            }
        }

        public static Task TryRunSynchronously(this Task task)
        {
            if (task.Status == TaskStatus.Created)
                task.RunSynchronously();
            return task;
        }

        #region Playing with coloring
        public static void RenderColoredConsoleMessage(this string message, bool AddNewLine = false)
        {
            message = message.Replace("&", "§");
            var msgLst = message.Contains("§") ? message.Split("§") : new string[] { $"r{message}" };
            if (message[0] != '§' && msgLst.Length > 1) msgLst[0] = $"r{msgLst[0]}";
            foreach (var msg in msgLst)
            {
                if (!string.IsNullOrEmpty(msg) && msg.Length > 1)
                {
                    var colorStr = msg[0].ToString().ToLower()[0];
                    var consoleColor = ChatColor.FromCode(colorStr).ConsoleColor;
                    if (colorStr.IsRealChatColor())
                    {
                        if (colorStr == 'r') Console.ResetColor();
                        else if (consoleColor.HasValue) Console.ForegroundColor = consoleColor.Value;
                    }
                    Console.Write(colorStr.IsRealChatColor() ? msg[1..] : msg);
                }
            }
            Console.ResetColor();
            if (AddNewLine) Console.WriteLine("");
        }
        public static bool IsRealChatColor(this string suspectedColor, bool skipPrefix = false) => (skipPrefix switch
        {
            true => new Regex("^([a-f]|r|o|m|n|k|l|[0-9])$"),
            false => new Regex("^([§|&])([a-f]|r|o|m|n|k|l|[0-9])$")
        }).IsMatch($"{suspectedColor.ToLower()}");
    
        public static bool IsRealChatColor(this char suspectedColor) => suspectedColor.ToString().ToLower().IsRealChatColor(true);
        #endregion
    }
}
