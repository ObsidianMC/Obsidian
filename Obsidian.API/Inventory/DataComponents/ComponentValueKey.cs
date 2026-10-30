using Obsidian.Nbt;
using System.Collections;
using System.Runtime.CompilerServices;

namespace Obsidian.API.Inventory.DataComponents;

// Structural snapshots for component payloads. Hashes only reject mismatches; equality checks every value.
internal sealed class ComponentValueKey : IEquatable<ComponentValueKey>
{
    private readonly object? key;
    private readonly int hash;

    public ComponentValueKey(object? value)
    {
        this.key = Key(value);
        this.hash = StructuralComparisons.StructuralEqualityComparer.GetHashCode(this.key!);
    }

    public bool Equals(ComponentValueKey? other) => ReferenceEquals(this, other)
        || other is not null && this.hash == other.hash
            && StructuralComparisons.StructuralEqualityComparer.Equals(this.key, other.key);

    public override bool Equals(object? obj) => obj is ComponentValueKey other && this.Equals(other);

    public override int GetHashCode() => this.hash;

    // A default immutable array has no backing storage; compare it like an empty sequence without enumerating it.
    public static object Sequence<T>(ImmutableArray<T> values) => values.IsDefault ? ImmutableArray<T>.Empty : values;

    public static object? Sequence<T>(ImmutableArray<T>? values) => values.HasValue ? Sequence(values.Value) : null;

    private static object? Key(object? value) => value switch
    {
        null => null,
        string => value,
        ITuple tuple => Enumerable.Range(0, tuple.Length).Select(index => Key(tuple[index])).ToArray(),
        SkinProperty property => new object?[]
        {
            property.Name,
            property.Value,
            property.Signature
        },
        NbtCompound compound => new object?[]
        {
            compound.Type,
            compound.OrderBy(entry => entry.Key, StringComparer.Ordinal)
                .Select(entry => new object?[]
                {
                    entry.Key,
                    Key(entry.Value)
                }).ToArray()
        },
        NbtList list => new object?[]
        {
            list.Type,
            list.Select(tag => Key(tag)).ToArray()
        },
        NbtArray<byte> array => new object?[]
        {
            array.Type,
            array.GetArray()
        },
        NbtArray<int> array => new object?[]
        {
            array.Type,
            array.GetArray()
        },
        NbtArray<long> array => new object?[]
        {
            array.Type,
            array.GetArray()
        },
        NbtTag<bool> tag => (byte)(tag.Value ? 1 : 0),
        NbtTag<byte> tag => tag.Value,
        NbtTag<short> tag => tag.Value,
        NbtTag<int> tag => tag.Value,
        NbtTag<long> tag => tag.Value,
        NbtTag<float> tag => tag.Value,
        NbtTag<double> tag => tag.Value,
        NbtTag<string> tag => tag.Value,
        ChatMessage chat => new object?[]
        {
            chat.Text,
            chat.Translate,
            chat.Color,
            chat.Bold,
            chat.Italic,
            chat.Underlined,
            chat.Strikethrough,
            chat.Obfuscated,
            chat.ShadowColor,
            chat.Insertion,
            Key(chat.ClickEvent),
            Key(chat.HoverEvent),
            Key(chat.Extra),
            Key(chat.With)
        },
        ClickComponent click => new object?[]
        {
            click.Action,
            click.Value
        },
        HoverComponent hover => new object?[]
        {
            hover.Action,
            Key(hover.Contents)
        },
        HoverChatContent hover => Key(hover.ChatMessage),
        HoverItemContent hover => Key(hover.Item),
        ItemStack stack => new object?[]
        {
            stack.Count,
            stack
        },
        IdSet set => new object?[]
        {
            set.Type,
            set.TagName,
            Key(Sequence(set.Ids))
        },
        FireworkExplosion explosion => new object?[]
        {
            explosion.Shape,
            Key(Sequence(explosion.Colors)),
            Key(Sequence(explosion.FadeColors)),
            explosion.HasTrail,
            explosion.HasTwinkle
        },
        MobVisibilityData visibility => new object?[]
        {
            visibility.TargetingEntityTypes.TagName,
            visibility.TargetingEntityTypes.Ids?.ToArray(),
            visibility.Visibility
        },
        InstrumentData instrument => new object?[]
        {
            instrument.Identifier,
            instrument.SoundEvent,
            instrument.UseDuration,
            instrument.Range,
            instrument.DurabilityDamage,
            Key(instrument.Description)
        },
        ComponentConsumeEffect effect => new object?[]
        {
            effect.Id,
            Key(Sequence(effect.Effects)),
            effect.Probability,
            Key(effect.RemovedEffects),
            effect.Diameter,
            effect.DirectionalParticles,
            effect.Sound
        },
        IEnumerable values => values.Cast<object?>().Select(Key).ToArray(),
        _ => value
    };
}
