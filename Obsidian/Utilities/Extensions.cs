using Obsidian.API;
using Obsidian.Entities;
using Obsidian.Items;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

#nullable enable

namespace Obsidian.Utilities
{
    public static partial class Extensions
    {
        public static bool IsAir(this ItemStack? item) => item == null || item.Type == Material.Air;

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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ref T MoveNext<T>(this ref T t) where T : struct
        {
            return ref Unsafe.Add(ref t, 1);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ref T GetRef<T>(this Span<T> span) where T : struct
        {
            return ref MemoryMarshal.GetReference(span);
        }
    }
}
