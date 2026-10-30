using Obsidian.Nbt;
using Obsidian.Net.Packets.Play.Clientbound;

namespace Obsidian.Entities;

/// <summary>
/// A text display, like vanilla's <c>Display.TextDisplay</c>: floating text with no hitbox. Its display fields are sent
/// as vanilla's synched data (indexes 8 to 27) and saved under vanilla's keys. Each change is sent to the players
/// tracking it.
/// </summary>
[MinecraftEntity("minecraft:text_display")]
public sealed partial class TextDisplay : Entity, ITextDisplay
{
    // Vanilla's defaults: an opaque grey background, 200-pixel lines, and no transformation.
    private const int DefaultBackgroundColor = 0x40000000;
    private const int DefaultLineWidth = 200;
    private const int NoBrightnessOverride = -1;

    // Bits of the style flags byte (index 27).
    private const byte ShadowFlag = 1;
    private const byte SeeThroughFlag = 2;
    private const byte DefaultBackgroundFlag = 4;
    private const byte LeftAlignFlag = 8;
    private const byte RightAlignFlag = 16;

    private ChatMessage text = ChatMessage.Simple(string.Empty);
    private TextDisplayBillboard billboard;
    private int backgroundColor = DefaultBackgroundColor;
    private int lineWidth = DefaultLineWidth;
    private byte textOpacity = 0xFF;
    private bool shadow;
    private bool seeThrough;
    private bool defaultBackground;
    private TextDisplayAlignment alignment;
    private float viewRange = 1f;
    private float shadowRadius;
    private float shadowStrength = 1f;
    private float width;
    private float height;
    private int glowColorOverride = -1;
    private int brightnessOverride = NoBrightnessOverride;
    private VectorF translation;
    private VectorF scale = new(1f);
    private Rotation leftRotation = Rotation.Identity;
    private Rotation rightRotation = Rotation.Identity;
    private int interpolationDuration;
    private int positionInterpolationDuration;

    public TextDisplay() => this.Type = EntityType.TextDisplay;

    /// <summary>The text shown. It can't be <c>null</c>, since the client can't read an empty text component.</summary>
    public ChatMessage Text
    {
        get => this.text;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            this.Update(ref this.text, value);
        }
    }

    public TextDisplayBillboard Billboard
    {
        get => this.billboard;
        set => this.Update(ref this.billboard, value);
    }

    public int BackgroundColor
    {
        get => this.backgroundColor;
        set => this.Update(ref this.backgroundColor, value);
    }

    public int LineWidth
    {
        get => this.lineWidth;
        set => this.Update(ref this.lineWidth, value);
    }

    public byte TextOpacity
    {
        get => this.textOpacity;
        set => this.Update(ref this.textOpacity, value);
    }

    public bool Shadow
    {
        get => this.shadow;
        set => this.Update(ref this.shadow, value);
    }

    public bool SeeThrough
    {
        get => this.seeThrough;
        set => this.Update(ref this.seeThrough, value);
    }

    /// <summary>Whether the text uses the default background, like vanilla's <c>default_background</c> flag.</summary>
    public bool DefaultBackground
    {
        get => this.defaultBackground;
        set => this.Update(ref this.defaultBackground, value);
    }

    public TextDisplayAlignment Alignment
    {
        get => this.alignment;
        set => this.Update(ref this.alignment, value);
    }

    public float ViewRange
    {
        get => this.viewRange;
        set => this.Update(ref this.viewRange, value);
    }

    public float ShadowRadius
    {
        get => this.shadowRadius;
        set => this.Update(ref this.shadowRadius, value);
    }

    public float ShadowStrength
    {
        get => this.shadowStrength;
        set => this.Update(ref this.shadowStrength, value);
    }

    public float Width
    {
        get => this.width;
        set => this.Update(ref this.width, value);
    }

    public float Height
    {
        get => this.height;
        set => this.Update(ref this.height, value);
    }

    /// <summary>The glow colour as ARGB, or <c>-1</c> to use the glow colour of the entity's team.</summary>
    public int GlowColorOverride
    {
        get => this.glowColorOverride;
        set => this.Update(ref this.glowColorOverride, value);
    }

    /// <summary>The packed block and sky light the display is drawn at, or <c>-1</c> to use the world's light.</summary>
    public int BrightnessOverride
    {
        get => this.brightnessOverride;
        set => this.Update(ref this.brightnessOverride, value);
    }

    public VectorF Translation
    {
        get => this.translation;
        set => this.Update(ref this.translation, value);
    }

    public VectorF Scale
    {
        get => this.scale;
        set => this.Update(ref this.scale, value);
    }

    /// <summary>The rotation applied before scaling, as a quaternion (x, y, z, w).</summary>
    public Rotation LeftRotation
    {
        get => this.leftRotation;
        set => this.Update(ref this.leftRotation, value);
    }

    /// <summary>The rotation applied after scaling, as a quaternion (x, y, z, w).</summary>
    public Rotation RightRotation
    {
        get => this.rightRotation;
        set => this.Update(ref this.rightRotation, value);
    }

    /// <summary>Ticks over which a transformation change is smoothed on the client.</summary>
    public int TransformationInterpolationDuration
    {
        get => this.interpolationDuration;
        set => this.Update(ref this.interpolationDuration, value);
    }

    public int PositionInterpolationDuration
    {
        get => this.positionInterpolationDuration;
        set => this.Update(ref this.positionInterpolationDuration, value);
    }

    /// <inheritdoc/>
    public bool Transient { get; set; }

    public override void Write(INetStreamWriter writer)
    {
        base.Write(writer);

        writer.WriteEntityMetadataType(8, EntityMetadataType.VarInt);
        writer.WriteVarInt(0);

        writer.WriteEntityMetadataType(9, EntityMetadataType.VarInt);
        writer.WriteVarInt(this.interpolationDuration);

        writer.WriteEntityMetadataType(10, EntityMetadataType.VarInt);
        writer.WriteVarInt(this.positionInterpolationDuration);

        writer.WriteEntityMetadataType(11, EntityMetadataType.Vector3);
        WriteVector3(writer, this.translation);

        writer.WriteEntityMetadataType(12, EntityMetadataType.Vector3);
        WriteVector3(writer, this.scale);

        writer.WriteEntityMetadataType(13, EntityMetadataType.Quaternion);
        WriteQuaternion(writer, this.leftRotation);

        writer.WriteEntityMetadataType(14, EntityMetadataType.Quaternion);
        WriteQuaternion(writer, this.rightRotation);

        writer.WriteEntityMetadataType(15, EntityMetadataType.Byte);
        writer.WriteByte((byte)this.billboard);

        writer.WriteEntityMetadataType(16, EntityMetadataType.VarInt);
        writer.WriteVarInt(this.brightnessOverride);

        writer.WriteEntityMetadataType(17, EntityMetadataType.Float);
        writer.WriteSingle(this.viewRange);

        writer.WriteEntityMetadataType(18, EntityMetadataType.Float);
        writer.WriteSingle(this.shadowRadius);

        writer.WriteEntityMetadataType(19, EntityMetadataType.Float);
        writer.WriteSingle(this.shadowStrength);

        writer.WriteEntityMetadataType(20, EntityMetadataType.Float);
        writer.WriteSingle(this.width);

        writer.WriteEntityMetadataType(21, EntityMetadataType.Float);
        writer.WriteSingle(this.height);

        writer.WriteEntityMetadataType(22, EntityMetadataType.VarInt);
        writer.WriteVarInt(this.glowColorOverride);

        writer.WriteEntityMetadataType(23, EntityMetadataType.TextComponent);
        writer.WriteChat(this.text);

        writer.WriteEntityMetadataType(24, EntityMetadataType.VarInt);
        writer.WriteVarInt(this.lineWidth);

        writer.WriteEntityMetadataType(25, EntityMetadataType.VarInt);
        writer.WriteVarInt(this.backgroundColor);

        writer.WriteEntityMetadataType(26, EntityMetadataType.Byte);
        writer.WriteByte(this.textOpacity);

        writer.WriteEntityMetadataType(27, EntityMetadataType.Byte);
        writer.WriteByte(this.StyleFlags());

        this.MetadataIndex = 28;
    }

    internal override void WriteNbt(NbtCompound tag)
    {
        base.WriteNbt(tag);

        tag.Set(this.text.ToNbt("text"));
        tag.Set(new NbtTag<int>("line_width", this.lineWidth));
        tag.Set(new NbtTag<int>("background", this.backgroundColor));
        tag.Set(new NbtTag<byte>("text_opacity", this.textOpacity));
        tag.SetFlag("shadow", this.shadow);
        tag.SetFlag("see_through", this.seeThrough);
        tag.SetFlag("default_background", this.defaultBackground);
        tag.Set(new NbtTag<string>("alignment", this.alignment switch
        {
            TextDisplayAlignment.Left => "left",
            TextDisplayAlignment.Right => "right",
            _ => "center"
        }));

        tag.Set(new NbtTag<string>("billboard", this.billboard switch
        {
            TextDisplayBillboard.Vertical => "vertical",
            TextDisplayBillboard.Horizontal => "horizontal",
            TextDisplayBillboard.Center => "center",
            _ => "fixed"
        }));

        tag.Set(new NbtCompound("transformation")
        {
            FloatList("translation", this.translation.X, this.translation.Y, this.translation.Z),
            FloatList("left_rotation", this.leftRotation.X, this.leftRotation.Y, this.leftRotation.Z, this.leftRotation.W),
            FloatList("scale", this.scale.X, this.scale.Y, this.scale.Z),
            FloatList("right_rotation", this.rightRotation.X, this.rightRotation.Y, this.rightRotation.Z, this.rightRotation.W)
        });

        tag.Set(new NbtTag<int>("interpolation_duration", this.interpolationDuration));
        tag.Set(new NbtTag<int>("teleport_duration", this.positionInterpolationDuration));
        tag.Set(new NbtTag<float>("view_range", this.viewRange));
        tag.Set(new NbtTag<float>("shadow_radius", this.shadowRadius));
        tag.Set(new NbtTag<float>("shadow_strength", this.shadowStrength));
        tag.Set(new NbtTag<float>("width", this.width));
        tag.Set(new NbtTag<float>("height", this.height));
        tag.Set(new NbtTag<int>("glow_color_override", this.glowColorOverride));

        // Vanilla packs brightness as block light in bits 4 to 7 and sky light in bits 20 to 23.
        tag.SetOrRemove("brightness", this.brightnessOverride == NoBrightnessOverride ? null : new NbtCompound("brightness")
        {
            new NbtTag<int>("block", (this.brightnessOverride >> 4) & 0xF),
            new NbtTag<int>("sky", (this.brightnessOverride >> 20) & 0xF)
        });
    }

    internal override void ReadNbt(NbtCompound tag)
    {
        base.ReadNbt(tag);

        this.text = tag.TryGetTag("text", out var text) && text.TextFromNbt() is { } message ? message : ChatMessage.Simple(string.Empty);
        this.lineWidth = tag.TryGetNumber<int>("line_width", out var lineWidthTag) ? lineWidthTag : DefaultLineWidth;
        this.backgroundColor = tag.TryGetNumber<int>("background", out var background) ? background : DefaultBackgroundColor;
        this.textOpacity = tag.TryGetNumber<byte>("text_opacity", out var opacity) ? opacity : (byte)0xFF;
        this.shadow = tag.TryGetBool("shadow", out var shadowFlag) && shadowFlag;
        this.seeThrough = tag.TryGetBool("see_through", out var seeThroughFlag) && seeThroughFlag;
        this.defaultBackground = tag.TryGetBool("default_background", out var defaultFlag) && defaultFlag;
        this.alignment = tag.TryGetTag<NbtTag<string>>("alignment", out var align) ? align.Value switch
        {
            "left" => TextDisplayAlignment.Left,
            "right" => TextDisplayAlignment.Right,
            _ => TextDisplayAlignment.Center
        } : TextDisplayAlignment.Center;

        this.billboard = tag.TryGetTag<NbtTag<string>>("billboard", out var billboardTag) ? billboardTag.Value switch
        {
            "vertical" => TextDisplayBillboard.Vertical,
            "horizontal" => TextDisplayBillboard.Horizontal,
            "center" => TextDisplayBillboard.Center,
            _ => TextDisplayBillboard.Fixed
        } : TextDisplayBillboard.Fixed;

        if (tag.TryGetTag<NbtCompound>("transformation", out var transformation))
        {
            if (ReadFloats(transformation, "translation", 3) is { } translation)
                this.translation = new VectorF { X = translation[0], Y = translation[1], Z = translation[2] };

            if (ReadFloats(transformation, "scale", 3) is { } scale)
                this.scale = new VectorF { X = scale[0], Y = scale[1], Z = scale[2] };

            if (ReadFloats(transformation, "left_rotation", 4) is { } left)
                this.leftRotation = new Rotation(left[0], left[1], left[2], left[3]);

            if (ReadFloats(transformation, "right_rotation", 4) is { } right)
                this.rightRotation = new Rotation(right[0], right[1], right[2], right[3]);
        }

        this.interpolationDuration = tag.TryGetNumber<int>("interpolation_duration", out var interpolation) ? interpolation : 0;
        this.positionInterpolationDuration = tag.TryGetNumber<int>("teleport_duration", out var teleport) ? teleport : 0;
        this.viewRange = ReadFloat(tag, "view_range", 1f);
        this.shadowRadius = ReadFloat(tag, "shadow_radius", 0f);
        this.shadowStrength = ReadFloat(tag, "shadow_strength", 1f);
        this.width = ReadFloat(tag, "width", 0f);
        this.height = ReadFloat(tag, "height", 0f);
        this.glowColorOverride = tag.TryGetNumber<int>("glow_color_override", out var glow) ? glow : -1;

        this.brightnessOverride = tag.TryGetTag<NbtCompound>("brightness", out var brightness)
            && brightness.TryGetNumber<int>("block", out var block) && brightness.TryGetNumber<int>("sky", out var sky)
            ? (Math.Clamp(block, 0, 15) << 4) | (Math.Clamp(sky, 0, 15) << 20)
            : NoBrightnessOverride;
    }

    // Stores a changed field and sends the new state to the players tracking this display. Unchanged values send nothing.
    private void Update<T>(ref T field, T value)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return;

        field = value;
        this.SynchronizeMetadata();
    }

    // Positions are compared exactly: VectorF.Equals treats differences under 0.01 as equal, which would drop a real move.
    private void Update(ref VectorF field, VectorF value)
    {
        if (field.X == value.X && field.Y == value.Y && field.Z == value.Z)
            return;

        field = value;
        this.SynchronizeMetadata();
    }

    /// <summary>The synched data packet that carries this display's current state to tracking players.</summary>
    internal SetEntityDataPacket CreateMetadataPacket() => new() { EntityId = this.EntityId, Entity = this };

    // Only a display in a level is tracked, so a field set before it's spawned is sent when it's first tracked.
    private void SynchronizeMetadata()
    {
        if (this.Level is not null)
            this.PacketBroadcaster.QueuePacketToLevelInRange(this.Level, this.Position, this.CreateMetadataPacket());
    }

    private byte StyleFlags()
    {
        byte flags = 0;
        if (this.shadow)
            flags |= ShadowFlag;
        if (this.seeThrough)
            flags |= SeeThroughFlag;
        if (this.defaultBackground)
            flags |= DefaultBackgroundFlag;

        flags |= this.alignment switch
        {
            TextDisplayAlignment.Left => LeftAlignFlag,
            TextDisplayAlignment.Right => RightAlignFlag,
            _ => 0
        };

        return flags;
    }

    private static void WriteVector3(INetStreamWriter writer, VectorF value)
    {
        writer.WriteSingle(value.X);
        writer.WriteSingle(value.Y);
        writer.WriteSingle(value.Z);
    }

    private static void WriteQuaternion(INetStreamWriter writer, Rotation value)
    {
        writer.WriteSingle(value.X);
        writer.WriteSingle(value.Y);
        writer.WriteSingle(value.Z);
        writer.WriteSingle(value.W);
    }

    private static NbtList FloatList(string name, params float[] values)
    {
        var list = new NbtList(NbtTagType.Float, name);
        foreach (var value in values)
            list.Add(new NbtTag<float>(string.Empty, value));

        return list;
    }

    private static float[]? ReadFloats(NbtCompound parent, string name, int count)
    {
        if (!parent.TryGetTag<NbtList>(name, out var list) || list.Count != count)
            return null;

        var values = new float[count];
        for (var i = 0; i < count; i++)
        {
            if (list[i] is not NbtTag<float> value)
                return null;

            values[i] = value.Value;
        }

        return values;
    }

    private static float ReadFloat(NbtCompound tag, string name, float fallback) =>
        tag.TryGetNumber<float>(name, out var value) ? value : fallback;

    /// <summary>A quaternion (x, y, z, w) as vanilla's transformations hold it.</summary>
    public readonly record struct Rotation(float X, float Y, float Z, float W)
    {
        public static Rotation Identity => new(0f, 0f, 0f, 1f);
    }
}
