using Obsidian.API.Inventory;
using Obsidian.Net.Packets.Play.Clientbound;
using System.Threading;
using Obsidian.Nbt;

namespace Obsidian.Entities;

/// <summary>
/// A modifier on one of an entity's attributes, like vanilla's <c>AttributeModifier</c>: its identifier, unique
/// among the attribute's modifiers, its amount and its operation.
/// </summary>
internal readonly record struct EntityAttributeModifier(string Id, double Amount, AttributeOperation Operation);

// The entity's attributes: base values (Attributes, which generated entity classes fill with their type's defaults)
// and modifiers on top, computed and clamped like vanilla's AttributeInstance, and the changes clients are sent.
public partial class Entity
{
    private readonly Lock attributeLock = new();

    // Modifiers by attribute, then by identifier; null until the entity has one.
    private Dictionary<string, Dictionary<string, EntityAttributeModifier>>? attributeModifiers;

    private readonly HashSet<(string Attribute, string Id)> savedModifiers = [];

    // Syncable attributes whose base or modifiers changed since clients were last sent them.
    private HashSet<string>? changedAttributes;

    protected virtual ConcurrentDictionary<string, float> Attributes { get; } = new();

    public bool TryAddAttribute(string attributeResourceName, float value)
    {
        var name = this.ResolveAttributeName(attributeResourceName);
        if (!this.Attributes.TryAdd(name, value))
            return false;

        this.OnAttributeChanged(name);
        return true;
    }

    public bool TryUpdateAttribute(string attributeResourceName, float newValue)
    {
        var name = this.ResolveAttributeName(attributeResourceName);
        if (!this.Attributes.TryGetValue(name, out var value))
            return false;

        if (!this.Attributes.TryUpdate(name, newValue, value))
            return false;

        this.OnAttributeChanged(name);
        return true;
    }

    public bool HasAttribute(string attributeResourceName) =>
        this.Attributes.ContainsKey(this.ResolveAttributeName(attributeResourceName));

    /// <summary>
    /// The attribute's value: its base with its modifiers applied and clamped to its range, like vanilla's
    /// <c>AttributeInstance.getValue</c>, or 0 if the entity doesn't have it.
    /// </summary>
    public float GetAttributeValue(string attributeResourceName)
    {
        var name = this.ResolveAttributeName(attributeResourceName);
        if (!this.Attributes.TryGetValue(name, out var baseValue))
            return 0;

        lock (this.attributeLock)
        {
            var modifiers = this.attributeModifiers?.GetValueOrDefault(name);

            return (float)Compute(VanillaName(name), baseValue, modifiers?.Values);
        }
    }

    public bool SetAttributeModifier(string attribute, string id, double amount, AttributeOperation operation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        if (!double.IsFinite(amount) || !Enum.IsDefined(operation))
            throw new ArgumentException("The modifier needs a finite amount and a defined operation.");

        var name = this.ResolveAttributeName(attribute);
        if (!this.Attributes.ContainsKey(name))
            return false;

        this.SetModifier(name, new EntityAttributeModifier(id, amount, operation), permanent: true);
        return true;
    }

    public bool RemoveAttributeModifier(string attribute, string id) =>
        this.RemoveModifier(this.ResolveAttributeName(attribute), id);

    /// <summary>
    /// Adds a modifier to an attribute the entity has, replacing the one with the same identifier, like vanilla's
    /// <c>addOrUpdateTransientModifier</c>.
    /// </summary>
    internal void SetModifier(string name, EntityAttributeModifier modifier, bool permanent = false)
    {
        if (!this.Attributes.ContainsKey(name))
            return;

        lock (this.attributeLock)
        {
            this.attributeModifiers ??= [];

            if (!this.attributeModifiers.TryGetValue(name, out var modifiers))
                this.attributeModifiers[name] = modifiers = [];

            if (permanent)
                this.savedModifiers.Add((name, modifier.Id));
            else
                this.savedModifiers.Remove((name, modifier.Id));

            if (modifiers.TryGetValue(modifier.Id, out var existing) && existing == modifier)
                return;

            modifiers[modifier.Id] = modifier;
        }

        this.OnAttributeChanged(name);
    }

    internal bool RemoveModifier(string name, string id)
    {
        lock (this.attributeLock)
        {
            this.savedModifiers.Remove((name, id));
            if (this.attributeModifiers?.GetValueOrDefault(name)?.Remove(id) != true)
                return false;
        }

        this.OnAttributeChanged(name);
        return true;
    }

    internal bool HasModifier(string name, string id)
    {
        lock (this.attributeLock)
            return this.attributeModifiers?.GetValueOrDefault(name)?.ContainsKey(id) == true;
    }

    // Vanilla 1.21.11 AttributeMap.pack/AttributeInstance.Packed: lower-case fields, named operations,
    // and permanent modifiers only. Equipment and sprint modifiers are rebuilt, never persisted twice.
    internal NbtList SaveAttributes()
    {
        var attributes = new NbtList(NbtTagType.Compound, "attributes");
        lock (this.attributeLock)
        {
            foreach (var (name, value) in this.Attributes.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                var entry = new NbtCompound(string.Empty)
                {
                    new NbtTag<string>("id", VanillaName(name)),
                    new NbtTag<double>("base", value)
                };
                var modifiers = new NbtList(NbtTagType.Compound, "modifiers");
                foreach (var modifier in this.attributeModifiers?.GetValueOrDefault(name)?.Values.AsEnumerable() ?? [])
                {
                    if (!this.savedModifiers.Contains((name, modifier.Id)))
                        continue;

                    modifiers.Add(new NbtCompound(string.Empty)
                    {
                        new NbtTag<string>("id", modifier.Id),
                        new NbtTag<double>("amount", modifier.Amount),
                        new NbtTag<string>("operation", modifier.Operation switch
                        {
                            AttributeOperation.Add => "add_value",
                            AttributeOperation.MulBase => "add_multiplied_base",
                            _ => "add_multiplied_total"
                        })
                    });
                }

                if (modifiers.Count > 0)
                    entry.Add(modifiers);

                attributes.Add(entry);
            }
        }

        return attributes;
    }

    internal void LoadAttributes(NbtCompound tag)
    {
        if (!tag.TryGetTag<NbtList>("attributes", out var attributes))
            return;

        foreach (var entry in attributes.OfType<NbtCompound>())
        {
            if (!entry.TryGetTagValue<string>("id", out var id))
                continue;

            var name = this.ResolveAttributeName(id);
            // AttributeMap.load accepts registered attributes even when this entity's defaults omit them.
            if (!VanillaAttributes.TryGetProtocolId(VanillaName(name), out _))
                continue;

            if (entry.TryGetTagValue<double>("base", out var value) && double.IsFinite(value))
                this.Attributes[name] = (float)value;

            if (!this.Attributes.ContainsKey(name))
                continue;

            lock (this.attributeLock)
            {
                foreach (var saved in this.savedModifiers.Where(pair => pair.Attribute == name).ToArray())
                {
                    this.attributeModifiers?.GetValueOrDefault(name)?.Remove(saved.Id);
                    this.savedModifiers.Remove(saved);
                }
            }

            if (entry.TryGetTag<NbtList>("modifiers", out var modifiers))
            {
                foreach (var modifier in modifiers.OfType<NbtCompound>())
                {
                    if (!modifier.TryGetTagValue<string>("id", out var modifierId)
                        || string.IsNullOrWhiteSpace(modifierId)
                        || !modifier.TryGetTagValue<double>("amount", out var amount)
                        || !double.IsFinite(amount)
                        || !modifier.TryGetTagValue<string>("operation", out var operation))
                    {
                        continue;
                    }

                    var kind = operation switch
                    {
                        "add_value" => AttributeOperation.Add,
                        "add_multiplied_base" => AttributeOperation.MulBase,
                        "add_multiplied_total" => AttributeOperation.MulTotal,
                        _ => (AttributeOperation)(-1)
                    };
                    if (Enum.IsDefined(kind))
                        this.SetModifier(name, new EntityAttributeModifier(modifierId, amount, kind), permanent: true);
                }
            }

            this.OnAttributeChanged(name);
        }
    }

    /// <summary>
    /// The entity's syncable attributes, all of them or only those changed since the last call with
    /// <paramref name="changedOnly"/>, as an <c>UpdateAttributes</c> packet; null when there are none.
    /// </summary>
    /// <remarks>
    /// Like vanilla, only attributes clients compute are sent (vanilla's <c>getSyncableAttributes</c> when an entity
    /// starts being tracked, and <c>getAttributesToSync</c> after changes).
    /// </remarks>
    internal UpdateAttributesPacket? CreateAttributesPacket(bool changedOnly)
    {
        var snapshots = new List<AttributeSnapshot>();

        lock (this.attributeLock)
        {
            IEnumerable<string> names = changedOnly ? this.changedAttributes ?? [] : this.Attributes.Keys;

            foreach (var name in names.Order(StringComparer.Ordinal))
            {
                var vanilla = VanillaName(name);
                if (!VanillaAttributes.TryGetRange(vanilla, out var range)
                    || !range.Syncable
                    || !VanillaAttributes.TryGetProtocolId(vanilla, out var id)
                    || !this.Attributes.TryGetValue(name, out var baseValue))
                {
                    continue;
                }

                var modifiers = this.attributeModifiers?.GetValueOrDefault(name)?.Values.ToImmutableArray() ?? [];
                snapshots.Add(new AttributeSnapshot(id, baseValue, modifiers));
            }

            if (changedOnly)
                this.changedAttributes = null;
        }

        return snapshots.Count == 0 ? null : new UpdateAttributesPacket(this.EntityId, snapshots);
    }

    /// <summary>
    /// Sends the players tracking the entity, and a player itself, the attributes that changed since the last time.
    /// </summary>
    internal void SendAttributeChanges()
    {
        if (this.changedAttributes is null)
            return;

        if (this.CreateAttributesPacket(changedOnly: true) is not UpdateAttributesPacket packet)
            return;

        this.PacketBroadcaster.QueuePacketToLevelInRange(this.Level, this.Position, packet);
    }

    /// <summary>
    /// Called after an attribute's base or modifiers changed; marks it for clients, and keeps health within a lower
    /// maximum like vanilla's <c>LivingEntity.onAttributeUpdated</c>.
    /// </summary>
    protected virtual void OnAttributeChanged(string name)
    {
        this.MarkAttributeChanged(name);

        if (VanillaName(name) == "minecraft:scale")
            this.BoundingBox = this.Dimension.CreateBBFromPosition(this.Position);

        if (VanillaName(name) == "minecraft:max_health")
        {
            var maximum = this.GetAttributeValue(name);
            if (this.Health > maximum)
                this.Health = maximum;
        }
    }

    private void MarkAttributeChanged(string name)
    {
        lock (this.attributeLock)
            (this.changedAttributes ??= []).Add(name);
    }

    // Vanilla's AttributeInstance.calculateValue: add_value modifiers are added to the base, add_multiplied_base ones
    // add multiples of that sum, then add_multiplied_total ones multiply the result, which is clamped to the
    // attribute's range. Clients compute the same from UpdateAttributes, so the order must match.
    private static double Compute(string name, double baseValue, IEnumerable<EntityAttributeModifier>? modifiers)
    {
        if (modifiers is null)
            return VanillaAttributes.Sanitize(name, baseValue);

        var sum = baseValue;
        foreach (var modifier in modifiers)
        {
            if (modifier.Operation == AttributeOperation.Add)
                sum += modifier.Amount;
        }

        var result = sum;
        foreach (var modifier in modifiers)
        {
            if (modifier.Operation == AttributeOperation.MulBase)
                result += sum * modifier.Amount;
        }

        foreach (var modifier in modifiers)
        {
            if (modifier.Operation == AttributeOperation.MulTotal)
                result *= 1 + modifier.Amount;
        }

        return VanillaAttributes.Sanitize(name, result);
    }

    // Older names already renamed, since entities ask for theirs (minecraft:generic.movement_speed, say) every tick.
    private static readonly ConcurrentDictionary<string, string> renamedAttributes = new(StringComparer.Ordinal);

    // The 1.21 name of an attribute saved or asked for by its older name.
    private static string VanillaName(string name)
    {
        const string genericPrefix = "minecraft:generic.";
        if (name.StartsWith(genericPrefix, StringComparison.Ordinal))
            return renamedAttributes.GetOrAdd(name, static old => "minecraft:" + old[genericPrefix.Length..]);

        return name == "minecraft:horse.jump_strength" ? "minecraft:jump_strength" : name;
    }

    private string ResolveAttributeName(string name)
    {
        // Keep saved legacy overrides, while allowing AI to use the regenerated vanilla attribute names.
        if (this.Attributes.ContainsKey(name))
            return name;

        return VanillaName(name);
    }
}
