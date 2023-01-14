using Obsidian.Entities;
using Obsidian.Registries;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq.Expressions;
using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;

#nullable enable

namespace Obsidian.Utilities;

public static partial class Extensions
{
    internal readonly static EntityType[] nonLiving = new[] { EntityType.Arrow,
                EntityType.SpectralArrow,
                EntityType.Boat,
                EntityType.DragonFireball,
                EntityType.AreaEffectCloud,
                EntityType.EndCrystal,
                EntityType.EvokerFangs,
                EntityType.ExperienceOrb,
                EntityType.FireworkRocket,
                EntityType.FallingBlock,
                EntityType.Item,
                EntityType.ItemFrame,
                EntityType.Fireball,
                EntityType.LeashKnot,
                EntityType.LightningBolt,
                EntityType.LlamaSpit,
                EntityType.Minecart,
                EntityType.ChestMinecart,
                EntityType.CommandBlockMinecart,
                EntityType.FurnaceMinecart,
                EntityType.HopperMinecart,
                EntityType.SpawnerMinecart,
                EntityType.TntMinecart,
                EntityType.Painting,
                EntityType.Tnt,
                EntityType.ShulkerBullet,
                EntityType.EnderPearl,
                EntityType.Snowball,
                EntityType.SmallFireball,
                EntityType.Egg,
                EntityType.ExperienceBottle,
                EntityType.Potion,
                EntityType.Trident,
                EntityType.FishingBobber,
                EntityType.EyeOfEnder};



    public static ParameterExpression[] GetParamExpressions(this Type[] types) => types.Select((t, i) => Expression.Parameter(t, $"param{i}")).ToArray();

    internal static bool IsNonLiving(this EntityType type) => nonLiving.Contains(type);

    public static Item AsItem(this ItemStack itemStack) => ItemsRegistry.Get(itemStack.Type);

    public static IEnumerable<KeyValuePair<Guid, Player>> Except(this ConcurrentDictionary<Guid, Player> source, params Guid[] uuids) =>
        source.Where(x => !uuids.Contains(x.Value.Uuid));

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

    public static void ForEach<T>(this IEnumerable<T> collection, Action<T> action)
    {
        foreach (T t in collection)
        {
            action(t);
        }
    }

    // Derived from https://gist.github.com/ammaraskar/7b4a3f73bee9dc4136539644a0f27e63
    [SuppressMessage("Roslyn", "CA5350", Justification = "SHA1 is required by the Minecraft protocol.")]
    public static string MinecraftShaDigest(this IEnumerable<byte> data)
    {
        var hash = SHA1.HashData(data.ToArray());
        // Reverse the bytes since BigInteger uses little endian
        Array.Reverse(hash);

        var b = new BigInteger(hash);
        // very annoyingly, BigInteger in C# tries to be smart and puts in
        // a leading 0 when formatting as a hex number to allow roundtripping
        // of negative numbers, thus we have to trim it off.
        if (b < 0)
        {
            // toss in a negative sign if the interpreted number is negative
            return $"-{(-b).ToString("x", CultureInfo.InvariantCulture).TrimStart('0')}";
        }
        else
        {
            return b.ToString("x", CultureInfo.InvariantCulture).TrimStart('0');
        }
    }

    public static string ToJson(this object? value, JsonSerializerOptions? options = null) => JsonSerializer.Serialize(value, options ?? Globals.JsonOptions);
    public static T? FromJson<T>(this string value) => JsonSerializer.Deserialize<T>(value, Globals.JsonOptions);

    public static ValueTask<TValue?> FromJsonAsync<TValue>(this Stream stream, JsonSerializerOptions? options = null, CancellationToken cancellationToken = default) =>
        JsonSerializer.DeserializeAsync<TValue>(stream, options ?? Globals.JsonOptions, cancellationToken);
    public static Task ToJsonAsync(this object? value, Stream stream, CancellationToken cancellationToken = default) =>
        JsonSerializer.SerializeAsync(stream, value, Globals.JsonOptions, cancellationToken);
}
