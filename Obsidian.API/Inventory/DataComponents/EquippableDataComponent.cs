using Obsidian.API.Utilities;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace Obsidian.API.Inventory.DataComponents;
public sealed record class EquippableDataComponent : DataComponent
{
    public override DataComponentType Type => DataComponentType.Equippable;

    public override string Identifier => "minecraft:equippable";

    public required EquipmentSlot Slot { get; set; }

    public required SoundEvent EquipSound { get; set; }

    public string? AssetIdentifier { get; set; }
    public IdSet? AllowedEntitySet { get; set; }
    public bool EquipOnInteract { get; set; }
    public bool CanBeSheared { get; set; }
    public SoundEvent ShearingSound { get; set; }

    public EquipmentAssets? AssetId { get; set; }

    public string? CameraOverlay { get; set; }

    public ImmutableArray<int>? AllowedEntities { get; set; }

    public bool Dispensable { get; set; }
    public bool Swappable { get; set; }
    public bool DamageOnHurt { get; set; }

    [SetsRequiredMembers]
    internal EquippableDataComponent() { }

    public override void Read(INetStreamReader reader)
    {
        this.Slot = reader.ReadVarInt<EquipmentSlot>();
        this.EquipSound = ComponentValueCodecs.ReadSoundHolder(reader);
        this.AssetIdentifier = reader.ReadOptionalString();
        this.AssetId = this.AssetIdentifier is string value
            && Enum.TryParse<EquipmentAssets>(value.TrimResourceTag().ToPascalCase(), true, out var asset)
                ? asset
                : null;
        this.CameraOverlay = reader.ReadOptionalString();

        var hasEntities = reader.ReadBoolean();
        if (hasEntities)
        {
            this.AllowedEntitySet = reader.ReadIdSet();
            this.AllowedEntities = this.AllowedEntitySet.Value.Ids;
        }

        this.Dispensable = reader.ReadBoolean();
        this.Swappable = reader.ReadBoolean();
        this.DamageOnHurt = reader.ReadBoolean();
        this.EquipOnInteract = reader.ReadBoolean();
        this.CanBeSheared = reader.ReadBoolean();
        this.ShearingSound = ComponentValueCodecs.ReadSoundHolder(reader);
    }

    public override void Write(INetStreamWriter writer)
    {
        writer.WriteVarInt(this.Slot);
        ComponentValueCodecs.WriteSoundHolder(this.EquipSound, writer);
        writer.WriteOptional(this.AssetIdentifier ?? (this.AssetId.HasValue ? "minecraft:" + this.AssetId.Value.ToString().ToSnakeCase() : null));
        writer.WriteOptional(this.CameraOverlay);

        var hasEntities = this.AllowedEntitySet.HasValue || this.AllowedEntities.HasValue;
        writer.WriteBoolean(hasEntities);

        if (hasEntities)
            IdSet.Write(this.AllowedEntitySet ?? new IdSet { Type = 1, Ids = this.AllowedEntities }, writer);

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
