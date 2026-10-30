using Obsidian.API.Utilities;
using System.Diagnostics.CodeAnalysis;

namespace Obsidian.API.Inventory.DataComponents;

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

    public bool Equals(EquippableDataComponent? other) => ReferenceEquals(this, other)
        || other is not null && base.Equals(other) && this.Comparison.Equals(other.Comparison);

    public override int GetHashCode() =>
        HashCode.Combine(base.GetHashCode(), this.Comparison);

    public override DataComponentType Type => DataComponentType.Equippable;

    public override string Identifier => "minecraft:equippable";

    private EquipmentSlot slot;

    public required EquipmentSlot Slot
    {
        get => this.slot;
        init => this.slot = value;
    }

    private SoundEvent equipSound;

    public required SoundEvent EquipSound
    {
        get => this.equipSound;
        init => this.equipSound = value;
    }

    private string? assetIdentifier;

    public string? AssetIdentifier
    {
        get => this.assetIdentifier;
        init => this.assetIdentifier = value;
    }

    private IdSet? allowedEntitySet;

    public IdSet? AllowedEntitySet
    {
        get => this.allowedEntitySet;
        init => this.allowedEntitySet = value;
    }

    private bool equipOnInteract;

    public bool EquipOnInteract
    {
        get => this.equipOnInteract;
        init => this.equipOnInteract = value;
    }

    private bool canBeSheared;

    public bool CanBeSheared
    {
        get => this.canBeSheared;
        init => this.canBeSheared = value;
    }

    private SoundEvent shearingSound;

    public SoundEvent ShearingSound
    {
        get => this.shearingSound;
        init => this.shearingSound = value;
    }

    private EquipmentAssets? assetId;

    public EquipmentAssets? AssetId
    {
        get => this.assetId;
        init => this.assetId = value;
    }

    private string? cameraOverlay;

    public string? CameraOverlay
    {
        get => this.cameraOverlay;
        init => this.cameraOverlay = value;
    }

    private ImmutableArray<int>? allowedEntities;

    public ImmutableArray<int>? AllowedEntities
    {
        get => this.allowedEntities;
        init => this.allowedEntities = value;
    }

    private bool dispensable;

    public bool Dispensable
    {
        get => this.dispensable;
        init => this.dispensable = value;
    }

    private bool swappable;

    public bool Swappable
    {
        get => this.swappable;
        init => this.swappable = value;
    }

    private bool damageOnHurt;

    public bool DamageOnHurt
    {
        get => this.damageOnHurt;
        init => this.damageOnHurt = value;
    }

    [SetsRequiredMembers]
    internal EquippableDataComponent() { }

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


public enum EquipmentAssets
{
    Leather,
    Chainmail,
    Iron,
    Gold,
    Diamond,
    TutleScute,
    Netherite,
    ArmadilloScute,
    Elytra,

    WhiteCarpet,
    OrangeCarpet,
    MagentaCarpet,
    LightBlueCarpet,
    YellowCarpet,
    LimeCarpet,
    PinkCarpet,
    GrayCarpet,
    LightGrayCarpet,
    CyanCarpet,
    PurpleCarpet,
    BlueCarpet,
    BrownCarpet,
    GreenCarpet,
    RedCarpet,
    BlackCarpet,

    TraderLlama
}
