using Obsidian.API.Utilities;
using System.Diagnostics.CodeAnalysis;

namespace Obsidian.API.Inventory.DataComponents;

/// <summary>
/// How an item is worn (<c>minecraft:equippable</c>): its slot, its equip sound and model, and who can wear it. Start
/// from <see cref="ComponentBuilder.Equippable"/>, which has vanilla's defaults.
/// </summary>
public sealed record class EquippableDataComponent : DataComponent
{
    private ComponentValueKey Comparison => new(new object?[]
    {
        this.slot,
        this.equipSound,
        this.assetIdentifier,
        this.allowedEntitySet,
        this.equipOnInteract,
        this.canBeSheared,
        this.shearingSound,
        this.assetId,
        this.cameraOverlay,
        ComponentValueKey.Sequence(this.allowedEntities),
        this.dispensable,
        this.swappable,
        this.damageOnHurt
    });

    /// <inheritdoc/>
    public bool Equals(EquippableDataComponent? other) => ReferenceEquals(this, other)
        || other is not null && base.Equals(other) && this.Comparison.Equals(other.Comparison);

    /// <inheritdoc/>
    public override int GetHashCode() =>
        HashCode.Combine(base.GetHashCode(), this.Comparison);

    /// <inheritdoc/>
    public override DataComponentType Type => DataComponentType.Equippable;

    /// <inheritdoc/>
    public override string Identifier => "minecraft:equippable";

    private EquipmentSlot slot;

    /// <summary>
    /// Gets the slot the item is worn in.
    /// </summary>
    public required EquipmentSlot Slot
    {
        get => this.slot;
        init => this.slot = value;
    }

    private SoundEvent equipSound;

    /// <summary>
    /// Gets the sound played when the item is equipped.
    /// </summary>
    public required SoundEvent EquipSound
    {
        get => this.equipSound;
        init => this.equipSound = value;
    }

    private string? assetIdentifier;

    /// <summary>
    /// Gets the id of the equipment model drawn on the wearer, like <c>minecraft:diamond</c>; when null,
    /// <see cref="AssetId"/> names it.
    /// </summary>
    public string? AssetIdentifier
    {
        get => this.assetIdentifier;
        init => this.assetIdentifier = value;
    }

    private IdSet? allowedEntitySet;

    /// <summary>
    /// Gets the entity types that can wear the item, or null for any.
    /// </summary>
    public IdSet? AllowedEntitySet
    {
        get => this.allowedEntitySet;
        init => this.allowedEntitySet = value;
    }

    private bool equipOnInteract;

    /// <summary>
    /// Gets whether using the item on an entity that can wear it equips it.
    /// </summary>
    public bool EquipOnInteract
    {
        get => this.equipOnInteract;
        init => this.equipOnInteract = value;
    }

    private bool canBeSheared;

    /// <summary>
    /// Gets whether shears can take the item off its wearer.
    /// </summary>
    public bool CanBeSheared
    {
        get => this.canBeSheared;
        init => this.canBeSheared = value;
    }

    private SoundEvent shearingSound;

    /// <summary>
    /// Gets the sound played when shears take the item off.
    /// </summary>
    public SoundEvent ShearingSound
    {
        get => this.shearingSound;
        init => this.shearingSound = value;
    }

    private EquipmentAssets? assetId;

    /// <summary>
    /// Gets a vanilla equipment model, sent when <see cref="AssetIdentifier"/> isn't set.
    /// </summary>
    public EquipmentAssets? AssetId
    {
        get => this.assetId;
        init => this.assetId = value;
    }

    private string? cameraOverlay;

    /// <summary>
    /// Gets the id of the texture drawn over the wearer's screen, like a carved pumpkin's, or null for none.
    /// </summary>
    public string? CameraOverlay
    {
        get => this.cameraOverlay;
        init => this.cameraOverlay = value;
    }

    private ImmutableArray<int>? allowedEntities;

    /// <summary>
    /// Gets the ids of the entity types that can wear the item, or null for any; read from
    /// <see cref="AllowedEntitySet"/> or sent when that isn't set.
    /// </summary>
    public ImmutableArray<int>? AllowedEntities
    {
        get => this.allowedEntities;
        init => this.allowedEntities = value;
    }

    private bool dispensable;

    /// <summary>
    /// Gets whether a dispenser can put the item on an entity.
    /// </summary>
    public bool Dispensable
    {
        get => this.dispensable;
        init => this.dispensable = value;
    }

    private bool swappable;

    /// <summary>
    /// Gets whether using the item swaps it with what's in its slot.
    /// </summary>
    public bool Swappable
    {
        get => this.swappable;
        init => this.swappable = value;
    }

    private bool damageOnHurt;

    /// <summary>
    /// Gets whether the item takes damage when its wearer is hurt.
    /// </summary>
    public bool DamageOnHurt
    {
        get => this.damageOnHurt;
        init => this.damageOnHurt = value;
    }

    [SetsRequiredMembers]
    internal EquippableDataComponent() { }

    /// <inheritdoc/>
    protected override void ReadCore(INetStreamReader reader)
    {
        // EquipmentSlot.STREAM_CODEC uses its wire id, not its declaration order: offhand follows head.
        this.slot = reader.ReadVarInt() switch
        {
            0 => EquipmentSlot.MainHand,
            1 => EquipmentSlot.Boots,
            2 => EquipmentSlot.Leggings,
            3 => EquipmentSlot.Chestplate,
            4 => EquipmentSlot.Helmet,
            5 => EquipmentSlot.OffHand,
            6 => EquipmentSlot.Body,
            7 => EquipmentSlot.Saddle,
            _ => throw new System.IO.InvalidDataException("Unknown equipment slot.")
        };
        this.equipSound = ComponentValueCodecs.ReadSoundHolder(reader);
        this.assetIdentifier = reader.ReadOptionalString();
        this.assetId = this.AssetIdentifier is string value
            && Enum.TryParse<EquipmentAssets>(value.TrimResourceTag().ToPascalCase(), true, out var asset)
                ? asset
                : null;
        this.cameraOverlay = reader.ReadOptionalString();

        this.allowedEntitySet = null;
        this.allowedEntities = null;
        var hasEntities = reader.ReadBoolean();
        if (hasEntities)
        {
            this.allowedEntitySet = reader.ReadIdSet(ComponentValueCodecs.EntityTypes);
            this.allowedEntities = this.AllowedEntitySet.Value.Ids;
        }

        this.dispensable = reader.ReadBoolean();
        this.swappable = reader.ReadBoolean();
        this.damageOnHurt = reader.ReadBoolean();
        this.equipOnInteract = reader.ReadBoolean();
        this.canBeSheared = reader.ReadBoolean();
        this.shearingSound = ComponentValueCodecs.ReadSoundHolder(reader);
    }

    /// <inheritdoc/>
    public override void Write(INetStreamWriter writer)
    {
        writer.WriteVarInt(this.Slot switch
        {
            EquipmentSlot.MainHand => 0,
            EquipmentSlot.Boots => 1,
            EquipmentSlot.Leggings => 2,
            EquipmentSlot.Chestplate => 3,
            EquipmentSlot.Helmet => 4,
            EquipmentSlot.OffHand => 5,
            EquipmentSlot.Body => 6,
            EquipmentSlot.Saddle => 7,
            _ => throw new ArgumentOutOfRangeException(nameof(this.Slot))
        });
        ComponentValueCodecs.WriteSoundHolder(this.EquipSound, writer);
        writer.WriteOptional(this.AssetIdentifier ?? (this.AssetId.HasValue ? "minecraft:" + this.AssetId.Value.ToString().ToSnakeCase() : null));
        writer.WriteOptional(this.CameraOverlay);

        var hasEntities = this.AllowedEntitySet.HasValue || this.AllowedEntities.HasValue;
        writer.WriteBoolean(hasEntities);

        if (hasEntities)
        {
            var entities = this.AllowedEntitySet ?? new IdSet { Type = 1, Ids = this.AllowedEntities };
            IdSet.Write(entities, writer, ComponentValueCodecs.EntityTypes);
        }

        writer.WriteBoolean(this.Dispensable);
        writer.WriteBoolean(this.Swappable);
        writer.WriteBoolean(this.DamageOnHurt);
        writer.WriteBoolean(this.EquipOnInteract);
        writer.WriteBoolean(this.CanBeSheared);
        ComponentValueCodecs.WriteSoundHolder(this.ShearingSound, writer);
    }
}


/// <summary>
/// Vanilla's equipment models, sent as their snake case id (see <see cref="EquippableDataComponent.AssetId"/>).
/// </summary>
public enum EquipmentAssets
{
    /// <summary>
    /// The <c>minecraft:leather</c> equipment model.
    /// </summary>
    Leather,
    /// <summary>
    /// The <c>minecraft:chainmail</c> equipment model.
    /// </summary>
    Chainmail,
    /// <summary>
    /// The <c>minecraft:iron</c> equipment model.
    /// </summary>
    Iron,
    /// <summary>
    /// The <c>minecraft:gold</c> equipment model.
    /// </summary>
    Gold,
    /// <summary>
    /// The <c>minecraft:diamond</c> equipment model.
    /// </summary>
    Diamond,
    /// <summary>
    /// The turtle scute equipment model, sent as <c>minecraft:tutle_scute</c> because of the member's spelling, which
    /// vanilla clients don't know.
    /// </summary>
    TutleScute,
    /// <summary>
    /// The <c>minecraft:netherite</c> equipment model.
    /// </summary>
    Netherite,
    /// <summary>
    /// The <c>minecraft:armadillo_scute</c> equipment model.
    /// </summary>
    ArmadilloScute,
    /// <summary>
    /// The <c>minecraft:elytra</c> equipment model.
    /// </summary>
    Elytra,

    /// <summary>
    /// The <c>minecraft:white_carpet</c> equipment model.
    /// </summary>
    WhiteCarpet,
    /// <summary>
    /// The <c>minecraft:orange_carpet</c> equipment model.
    /// </summary>
    OrangeCarpet,
    /// <summary>
    /// The <c>minecraft:magenta_carpet</c> equipment model.
    /// </summary>
    MagentaCarpet,
    /// <summary>
    /// The <c>minecraft:light_blue_carpet</c> equipment model.
    /// </summary>
    LightBlueCarpet,
    /// <summary>
    /// The <c>minecraft:yellow_carpet</c> equipment model.
    /// </summary>
    YellowCarpet,
    /// <summary>
    /// The <c>minecraft:lime_carpet</c> equipment model.
    /// </summary>
    LimeCarpet,
    /// <summary>
    /// The <c>minecraft:pink_carpet</c> equipment model.
    /// </summary>
    PinkCarpet,
    /// <summary>
    /// The <c>minecraft:gray_carpet</c> equipment model.
    /// </summary>
    GrayCarpet,
    /// <summary>
    /// The <c>minecraft:light_gray_carpet</c> equipment model.
    /// </summary>
    LightGrayCarpet,
    /// <summary>
    /// The <c>minecraft:cyan_carpet</c> equipment model.
    /// </summary>
    CyanCarpet,
    /// <summary>
    /// The <c>minecraft:purple_carpet</c> equipment model.
    /// </summary>
    PurpleCarpet,
    /// <summary>
    /// The <c>minecraft:blue_carpet</c> equipment model.
    /// </summary>
    BlueCarpet,
    /// <summary>
    /// The <c>minecraft:brown_carpet</c> equipment model.
    /// </summary>
    BrownCarpet,
    /// <summary>
    /// The <c>minecraft:green_carpet</c> equipment model.
    /// </summary>
    GreenCarpet,
    /// <summary>
    /// The <c>minecraft:red_carpet</c> equipment model.
    /// </summary>
    RedCarpet,
    /// <summary>
    /// The <c>minecraft:black_carpet</c> equipment model.
    /// </summary>
    BlackCarpet,

    /// <summary>
    /// The <c>minecraft:trader_llama</c> equipment model.
    /// </summary>
    TraderLlama
}
