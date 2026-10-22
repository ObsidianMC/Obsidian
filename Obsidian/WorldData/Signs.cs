using Obsidian.API.Blocks;
using Obsidian.API.Inventory;
using Obsidian.Entities;
using Obsidian.Nbt;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.WorldData.Features;

namespace Obsidian.WorldData;

/// <summary>
/// Vanilla's signs, of every wood: standing and wall signs, and hanging signs under a block or beside one. It places
/// them the way they face, keeps them on what holds them, and lets players write on both sides, dye the text, make it
/// glow, and wax the sign so it can't be changed.
/// </summary>
/// <remarks>
/// The text is the sign block entity's saved data (see <see cref="DataBlockEntity"/>), as vanilla saves it:
/// <c>front_text</c> and <c>back_text</c>, each with four <c>messages</c>, a <c>color</c> and
/// <c>has_glowing_text</c>, and <c>is_waxed</c>.
/// </remarks>
internal static class Signs
{
    private const string Standing = "StandingSignBlock";
    private const string Wall = "WallSignBlock";
    private const string Ceiling = "CeilingHangingSignBlock";
    private const string WallHanging = "WallHangingSignBlock";

    // How far a player can be from a sign to edit it, vanilla's interaction range check.
    private const double EditRange = 8;

    private static readonly string[] dyes =
    [
        "white", "orange", "magenta", "light_blue", "yellow", "lime", "pink", "gray", "light_gray", "cyan", "purple",
        "blue", "brown", "green", "red", "black"
    ];

    // Who's writing on which sign, as vanilla's is_editing keeps it: only they may change it meanwhile.
    private static readonly ConcurrentDictionary<(ILevel Level, Vector Position), Guid> editors = new();

    /// <summary>Whether a block is a sign of any kind.</summary>
    public static bool IsSign(IBlock block) => block.BlockClass() is Standing or Wall or Ceiling or WallHanging;

    /// <summary>
    /// A standing sign already chosen by <see cref="StandingAndWallItems"/>: turned to the player and kept standing,
    /// without reselecting a wall variant from the clicked face.
    /// </summary>
    public static IBlock? GetStandingState(LevelBlockUpdates level, IBlock block, Vector position, IPlayer player)
    {
        var state = StandingState(level, block, position, player);
        return state is null ? null : Waterlog(level, state, position);
    }

    /// <summary>
    /// The sign a sign item places, like vanilla's <c>StandingAndWallBlockItem</c>: a sign on a block's side hangs on
    /// it as a wall sign and one on top stands; a hanging sign hangs under a block or beside one.
    /// </summary>
    /// <param name="block">The item's own block: a standing sign, or a ceiling hanging sign.</param>
    /// <returns>The state to place, or null when nothing would hold the sign there.</returns>
    public static IBlock? GetState(LevelBlockUpdates level, IBlock block, Vector position, IPlayer player, BlockFace face)
    {
        var hanging = block.BlockClass() == Ceiling;

        IBlock? state = hanging
            ? face == BlockFace.Down
                ? CeilingState(level, block, position, player) ?? WallHangingState(level, block, position, player, face)
                : WallHangingState(level, block, position, player, face) ?? CeilingState(level, block, position, player)
            : face.IsVertical()
                ? face == BlockFace.Up ? StandingState(level, block, position, player) : null
                : WallState(level, block, position, face) ?? StandingState(level, block, position, player);

        return state is null ? null : Waterlog(level, state, position);
    }

    private static IBlock? StandingState(LevelBlockUpdates level, IBlock block, Vector position, IPlayer player)
    {
        if (!level.GetBlock(position + Vector.Down).IsSolid())
            return null;

        return block.WithProperty("rotation", Segment(player.Yaw.Degrees + 180));
    }

    private static IBlock? WallState(LevelBlockUpdates level, IBlock block, Vector position, BlockFace face)
    {
        if (!level.GetBlock(position - face.ToVector()).IsSolid() || WallVariant(block, "_sign", "_wall_sign") is not IBlock wall)
            return null;

        return wall.WithProperty("facing", Name(face));
    }

    // Vanilla CeilingHangingSignBlock: a sign under a full face hangs straight, facing the player; under anything
    // else, or placed sneaking, it hangs from chains and turns as freely as a standing sign.
    private static IBlock? CeilingState(LevelBlockUpdates level, IBlock block, Vector position, IPlayer player)
    {
        var above = position + Vector.Up;
        if (!level.Level.IsFaceSturdy(above, BlockFace.Down, SupportType.Center))
            return null;

        var attached = !level.Level.IsFaceSturdy(above, BlockFace.Down, SupportType.Full) || player.Sneaking;
        var rotation = attached
            ? Segment(player.Yaw.Degrees + 180)
            : HorizontalIndex(PlacementDirections.Horizontal(player.Yaw.Degrees).Opposite()) * 4;

        return block.WithProperty("attached", attached).WithProperty("rotation", rotation);
    }

    // Vanilla WallHangingSignBlock: the sign hangs across the face clicked, from a block on either side of it.
    private static IBlock? WallHangingState(LevelBlockUpdates level, IBlock block, Vector position, IPlayer player, BlockFace face)
    {
        if (WallVariant(block, "_hanging_sign", "_wall_hanging_sign") is not IBlock wall)
            return null;

        var facing = face.IsVertical()
            ? PlacementDirections.Horizontal(player.Yaw.Degrees).Opposite()
            : AcrossLook(player, face);

        var state = wall.WithProperty("facing", Name(facing));
        return WallHangingSupported(level, state, position) ? state : null;
    }

    /// <summary>
    /// Whether what holds a sign is still there, as vanilla's <c>canSurvive</c> checks after a neighbour changed.
    /// </summary>
    public static bool Survives(LevelBlockUpdates level, IBlock state, Vector position) => state.BlockClass() switch
    {
        Standing => level.GetBlock(position + Vector.Down).IsSolid(),
        Wall => Face(state.GetProperty("facing")) is BlockFace facing && level.GetBlock(position - facing.ToVector()).IsSolid(),
        Ceiling => level.Level.IsFaceSturdy(position + Vector.Up, BlockFace.Down, SupportType.Center),
        WallHanging => WallHangingSupported(level, state, position),
        _ => true
    };

    // A wall hanging sign needs a block that holds it, or another wall hanging sign along the same line, on one side.
    private static bool WallHangingSupported(LevelBlockUpdates level, IBlock state, Vector position)
    {
        if (Face(state.GetProperty("facing")) is not BlockFace facing)
            return false;

        foreach (var side in (BlockFace[])[facing.ClockWise(), facing.CounterClockWise()])
        {
            var neighbor = level.GetBlock(position.Offset(side));
            if (level.Level.IsFaceSturdy(position.Offset(side), side.Opposite(), SupportType.Center)
                || neighbor.BlockClass() == WallHanging && Face(neighbor.GetProperty("facing")) is BlockFace other
                    && (other == facing || other == facing.Opposite()))
                return true;
        }

        return false;
    }

    /// <summary>
    /// After a player placed a sign, vanilla opens its front for them to write on.
    /// </summary>
    public static void OnPlacedByPlayer(ILevel level, Vector position, IBlock state, IPlayer player)
    {
        if (IsSign(state))
            OpenEditor(level, position, player, front: true);
    }

    /// <summary>
    /// A player uses a sign, like vanilla's <c>SignBlock.useItemOn</c>: a dye colors the text on the side they face,
    /// a glow ink sac makes it glow and an ink sac stops it glowing, honeycomb waxes the sign, and anything else
    /// opens that side to write on. A waxed sign takes none of it.
    /// </summary>
    /// <returns>Whether the sign used the interaction, and the item to use up, if any.</returns>
    public static (bool Used, bool ConsumeItem) Use(ILevel level, Vector position, IBlock state, IPlayer player, ItemStack? held)
    {
        if (!IsSign(state) || level.GetLoadedBlockEntity(position) is not DataBlockEntity sign)
            return (false, false);

        var data = sign.SnapshotData();
        var front = IsFacingFront(state, position, player);

        if (data.TryGetBool("is_waxed", out var waxed) && waxed)
        {
            PlaySound(level, position, "minecraft:block.sign.waxed_interact_fail");
            return (true, false);
        }

        var side = front ? "front_text" : "back_text";
        var text = Text(data, side);
        var name = held?.Holder.UnlocalizedName;
        var adventure = player.GameMode == GameMode.Adventure;

        if (name is not null && !adventure)
        {
            var dye = name.StartsWith("minecraft:", StringComparison.Ordinal) && name.EndsWith("_dye", StringComparison.Ordinal)
                ? name[10..^4]
                : null;

            if (dye is not null && dyes.Contains(dye) && HasText(text))
            {
                if (text.TryGetTag<NbtTag<string>>("color", out var color) && color.Value == dye)
                    return (false, false);

                text.Set(new NbtTag<string>("color", dye));
                Store(sign, side, text);
                PlaySound(level, position, "minecraft:item.dye.use");
                return (true, true);
            }

            if (name is "minecraft:glow_ink_sac" or "minecraft:ink_sac" && HasText(text))
            {
                var glowing = name == "minecraft:glow_ink_sac";
                if ((text.TryGetBool("has_glowing_text", out var current) && current) == glowing)
                    return (false, false);

                text.Set(new NbtTag<bool>("has_glowing_text", glowing));
                Store(sign, side, text);
                PlaySound(level, position, glowing ? "minecraft:item.glow_ink_sac.use" : "minecraft:item.ink_sac.use");
                return (true, true);
            }

            if (name == "minecraft:honeycomb")
            {
                sign.Set(new NbtTag<bool>("is_waxed", true));
                sign.SetChanged();
                sign.SendUpdate();
                level.SpawnParticles(new WaxParticle(), (VectorD)position + new VectorD(0.5, 0.5, 0.5), 6, new VectorF(0.3f, 0.3f, 0.3f));
                PlaySound(level, position, "minecraft:item.honeycomb.wax_on");
                return (true, true);
            }
        }

        if (adventure)
            return (false, false);

        OpenEditor(level, position, player, front);
        return (true, false);
    }

    /// <summary>
    /// Writes the lines a player sent from the sign editor, like vanilla's <c>SignBlockEntity.updateSignText</c>,
    /// if they're the one editing it and close enough, and the sign isn't waxed.
    /// </summary>
    public static void Edit(ILevel level, Vector position, IPlayer player, bool front, IReadOnlyList<string> lines)
    {
        if (!editors.TryGetValue((level, position), out var editor) || editor != player.Uuid)
            return;

        editors.TryRemove((level, position), out _);

        if ((player.Position - ((VectorD)position + new VectorD(0.5, 0.5, 0.5))).Magnitude > EditRange
            || level.GetLoadedBlock(position) is not IBlock state || !IsSign(state)
            || level.GetLoadedBlockEntity(position) is not DataBlockEntity sign)
            return;

        var data = sign.SnapshotData();
        if (data.TryGetBool("is_waxed", out var waxed) && waxed)
            return;

        var side = front ? "front_text" : "back_text";
        var text = Text(data, side);

        var messages = new NbtList(NbtTagType.String, "messages");
        for (var i = 0; i < 4; i++)
        {
            var line = i < lines.Count ? StripFormatting(lines[i]) : string.Empty;
            messages.Add(new NbtTag<string>(string.Empty, line.Length > 384 ? line[..384] : line));
        }

        text.Set(messages);
        Store(sign, side, text);
    }

    private static void OpenEditor(ILevel level, Vector position, IPlayer player, bool front)
    {
        editors[(level, position)] = player.Uuid;
        player.SendPacket(new OpenSignEditorPacket { Location = new VectorF(position.X, position.Y, position.Z), IsFrontText = front });
    }

    // Vanilla SignBlockEntity.isFacingFrontText: whether the player stands on the front's side of the sign.
    private static bool IsFacingFront(IBlock state, Vector position, IPlayer player)
    {
        var rotation = state.BlockClass() is Wall or WallHanging
            ? Face(state.GetProperty("facing")) switch
            {
                BlockFace.South => 0f,
                BlockFace.West => 90f,
                BlockFace.North => 180f,
                _ => 270f
            }
            : (int.TryParse(state.GetProperty("rotation"), out var segment) ? segment : 0) * 22.5f;

        var dx = player.Position.X - (position.X + 0.5);
        var dz = player.Position.Z - (position.Z + 0.5);
        var towardPlayer = (float)(Math.Atan2(dz, dx) * 180 / Math.PI) - 90;

        var difference = Math.Abs(((towardPlayer - rotation) % 360 + 540) % 360 - 180);
        return difference <= 90;
    }

    // A side's text, with vanilla's defaults for what's missing.
    private static NbtCompound Text(NbtCompound data, string side)
    {
        if (data.TryGetTag<NbtCompound>(side, out var saved))
            return DataBlockEntity.Copy(saved);

        var text = new NbtCompound(side)
        {
            new NbtTag<string>("color", "black"),
            new NbtTag<bool>("has_glowing_text", false)
        };

        var messages = new NbtList(NbtTagType.String, "messages");
        for (var i = 0; i < 4; i++)
            messages.Add(new NbtTag<string>(string.Empty, string.Empty));

        text.Add(messages);
        return text;
    }

    // Vanilla only dyes or lights text that's there.
    private static bool HasText(NbtCompound text) =>
        text.TryGetTag<NbtList>("messages", out var messages)
        && messages.Any(line => line is NbtTag<string> { Value.Length: > 0 } || line is NbtCompound);

    private static void Store(DataBlockEntity sign, string side, NbtCompound text)
    {
        var named = new NbtCompound(side);
        foreach (var (name, tag) in text)
            named.Add(name, tag);

        sign.Set(named);
        sign.SetChanged();
        sign.SendUpdate();
    }

    private static string StripFormatting(string line)
    {
        var builder = new System.Text.StringBuilder(line.Length);
        for (var i = 0; i < line.Length; i++)
        {
            if (line[i] == '§')
                i++;
            else if (!char.IsControl(line[i]))
                builder.Append(line[i]);
        }

        return builder.ToString();
    }

    // The wall form of a standing or ceiling sign, like oak_wall_sign for oak_sign.
    private static IBlock? WallVariant(IBlock block, string suffix, string wallSuffix)
    {
        var name = block.UnlocalizedName;
        if (!name.EndsWith(suffix, StringComparison.Ordinal))
            return null;

        var wall = name[..^suffix.Length] + wallSuffix;
        return BlockStateProperties.TryGetState(wall, null, out var state) ? state : null;
    }

    // Vanilla RotationSegment.convertToSegment: an angle as one of sixteen steps.
    private static int Segment(float degrees) => (int)Math.Floor(degrees * 16 / 360 + 0.5) & 15;

    // Vanilla's 2D direction values: south, west, north, east.
    private static int HorizontalIndex(BlockFace face) => face switch
    {
        BlockFace.South => 0,
        BlockFace.West => 1,
        BlockFace.North => 2,
        _ => 3
    };

    // The horizontal direction the player looks most along, of the two across the clicked face, turned to face them.
    private static BlockFace AcrossLook(IPlayer player, BlockFace face)
    {
        var yaw = player.Yaw.Degrees * Math.PI / 180;
        var lookX = -Math.Sin(yaw);
        var lookZ = Math.Cos(yaw);

        var direction = face is BlockFace.North or BlockFace.South
            ? lookX >= 0 ? BlockFace.East : BlockFace.West
            : lookZ >= 0 ? BlockFace.South : BlockFace.North;

        return direction.Opposite();
    }

    private static IBlock Waterlog(LevelBlockUpdates level, IBlock block, Vector position)
    {
        if (block.GetProperty("waterlogged") is null)
            return block;

        var there = level.GetBlock(position);
        return block.WithProperty("waterlogged", there.Is(BlockIds.Water) && there.GetProperty("level") is null or "0");
    }

    private static void PlaySound(ILevel level, Vector position, string sound) =>
        level.PacketBroadcaster.QueuePacketToLevelInRange(level, (VectorD)position + new VectorD(0.5, 0.5, 0.5), new SoundPacket
        {
            SoundLocation = sound,
            SoundPosition = new SoundPosition(position.X + 0.5, position.Y + 0.5, position.Z + 0.5),
            Category = SoundCategory.Blocks,
            Volume = 1,
            Pitch = 1,
            Seed = Random.Shared.NextInt64()
        });

    private static string Name(BlockFace face) => face.ToString().ToLowerInvariant();

    private static BlockFace? Face(string? name) => name switch
    {
        "north" => BlockFace.North,
        "south" => BlockFace.South,
        "west" => BlockFace.West,
        "east" => BlockFace.East,
        _ => null
    };

    private sealed class WaxParticle : ParticleData
    {
        public override ParticleType ParticleType => ParticleType.WaxOn;
    }
}
