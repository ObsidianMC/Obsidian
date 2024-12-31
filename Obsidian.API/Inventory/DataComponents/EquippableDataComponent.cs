using Obsidian.API.Utilities;
using System.Diagnostics.CodeAnalysis;

namespace Obsidian.API.Inventory.DataComponents;
public sealed record class EquippableDataComponent : IDataComponent
{
    public DataComponentType Type => DataComponentType.Equippable;

    public string Identifier => "minecraft:equippable";

    public required EquipmentSlot Slot { get; set; }

    public required SoundEvent EquipSound { get; set; }

    public EquipmentAssets? AssetId { get; set; }

    public string? CameraOverlay { get; set; }

    public List<int>? AllowedEntities { get; set; }

    public bool Dispensable { get; set; }
    public bool Swappable { get; set; }
    public bool DamageOnHurt { get; set; }

    [SetsRequiredMembers]
    internal EquippableDataComponent() { }

    public void Read(INetStreamReader reader)
    {
        this.Slot = reader.ReadVarInt<EquipmentSlot>();
        this.EquipSound = reader.ReadSoundEvent();
        this.AssetId = reader.ReadOptionalString() is string value ? Enum.Parse<EquipmentAssets>(value.TrimResourceTag(), true) : null;
        this.CameraOverlay = reader.ReadOptionalString();

        var hasEntities = reader.ReadBoolean();
        if (hasEntities)
            this.AllowedEntities = reader.ReadLengthPrefixedArray(reader.ReadVarInt);

        this.Dispensable = reader.ReadBoolean();
        this.Swappable = reader.ReadBoolean();
        this.DamageOnHurt = reader.ReadBoolean();
    }

    public void Write(INetStreamWriter writer)
    {
        writer.WriteVarInt(this.Slot);
        writer.WriteSoundEvent(this.EquipSound);
        writer.WriteOptional(this.AssetId.HasValue ? this.AssetId.Value.ToString().ToSnakeCase() : null);
        writer.WriteOptional(this.CameraOverlay);

        var hasEntities = this.AllowedEntities != null;
        writer.WriteBoolean(hasEntities);

        if (hasEntities)
            writer.WriteLengthPrefixedArray(writer.WriteVarInt, this.AllowedEntities);

        writer.WriteBoolean(this.Dispensable);
        writer.WriteBoolean(this.Swappable);
        writer.WriteBoolean(this.DamageOnHurt);
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
