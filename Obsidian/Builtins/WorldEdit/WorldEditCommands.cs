using System.Globalization;
using Obsidian.API;
using Obsidian.API.Commands;

namespace Obsidian.Builtins.WorldEdit;

public sealed class WorldEditCommands : CommandModuleBase
{
    private Task Run(string command, string arguments) =>
        ((WorldEditBuiltin)this.Plugin!).ExecuteAsync(this.CommandContext, command, arguments);

    [Command("/wand"), RequirePermission, IssuerScope(CommandIssuers.Client)]
    public Task Wand() => this.Run("wand", "");

    [Command("/pos1"), RequirePermission, IssuerScope(CommandIssuers.Client)]
    public Task Pos1([Remaining] string arguments) => this.Run("pos1", arguments);
    [CommandOverload]
    public Task Pos1() => this.Run("pos1", "");

    [Command("/pos2"), RequirePermission, IssuerScope(CommandIssuers.Client)]
    public Task Pos2([Remaining] string arguments) => this.Run("pos2", arguments);
    [CommandOverload]
    public Task Pos2() => this.Run("pos2", "");

    [Command("/sel", "/deselect"), RequirePermission, IssuerScope(CommandIssuers.Client)]
    public Task Deselect() => this.Run("sel", "");

    [Command("/size"), RequirePermission, IssuerScope(CommandIssuers.Client)]
    public Task Size() => this.Run("size", "");

    [Command("/set"), RequirePermission, IssuerScope(CommandIssuers.Client)]
    public Task Set([Remaining] string arguments) => this.Run("set", arguments);

    [Command("/replace"), RequirePermission, IssuerScope(CommandIssuers.Client)]
    public Task Replace([Remaining] string arguments) => this.Run("replace", arguments);

    [Command("/walls"), RequirePermission, IssuerScope(CommandIssuers.Client)]
    public Task Walls([Remaining] string arguments) => this.Run("walls", arguments);

    [Command("/outline", "/faces"), RequirePermission, IssuerScope(CommandIssuers.Client)]
    public Task Outline([Remaining] string arguments) => this.Run("outline", arguments);

    [Command("/copy"), RequirePermission, IssuerScope(CommandIssuers.Client)]
    public Task Copy() => this.Run("copy", "");

    [Command("/cut"), RequirePermission, IssuerScope(CommandIssuers.Client)]
    public Task Cut() => this.Run("cut", "");

    [Command("/paste"), RequirePermission, IssuerScope(CommandIssuers.Client)]
    public Task Paste([Remaining] string arguments) => this.Run("paste", arguments);
    [CommandOverload]
    public Task Paste() => this.Run("paste", "");

    [Command("/rotate"), RequirePermission, IssuerScope(CommandIssuers.Client)]
    public Task Rotate([Remaining] string arguments) => this.Run("rotate", arguments);

    [Command("/flip"), RequirePermission, IssuerScope(CommandIssuers.Client)]
    public Task Flip([Remaining] string arguments) => this.Run("flip", arguments);
    [CommandOverload]
    public Task Flip() => this.Run("flip", "");

    [Command("/undo"), RequirePermission, IssuerScope(CommandIssuers.Client)]
    public Task Undo() => this.Run("undo", "");

    [Command("/redo"), RequirePermission, IssuerScope(CommandIssuers.Client)]
    public Task Redo() => this.Run("redo", "");

    [Command("/expand"), RequirePermission, IssuerScope(CommandIssuers.Client)]
    public Task Expand([Remaining] string arguments) => this.Run("expand", arguments);

    [Command("/contract"), RequirePermission, IssuerScope(CommandIssuers.Client)]
    public Task Contract([Remaining] string arguments) => this.Run("contract", arguments);

    [Command("/shift"), RequirePermission, IssuerScope(CommandIssuers.Client)]
    public Task Shift([Remaining] string arguments) => this.Run("shift", arguments);

    [Command("/stack"), RequirePermission, IssuerScope(CommandIssuers.Client)]
    public Task Stack([Remaining] string arguments) => this.Run("stack", arguments);
    [CommandOverload]
    public Task Stack() => this.Run("stack", "");

    [Command("/move"), RequirePermission, IssuerScope(CommandIssuers.Client)]
    public Task Move([Remaining] string arguments) => this.Run("move", arguments);
    [CommandOverload]
    public Task Move() => this.Run("move", "");

    [Command("/sphere"), RequirePermission, IssuerScope(CommandIssuers.Client)]
    public Task Sphere([Remaining] string arguments) => this.Run("sphere", arguments);

    [Command("/hsphere"), RequirePermission, IssuerScope(CommandIssuers.Client)]
    public Task HollowSphere([Remaining] string arguments) => this.Run("hsphere", arguments);

    [Command("/cyl"), RequirePermission, IssuerScope(CommandIssuers.Client)]
    public Task Cylinder([Remaining] string arguments) => this.Run("cyl", arguments);

    [Command("/hcyl"), RequirePermission, IssuerScope(CommandIssuers.Client)]
    public Task HollowCylinder([Remaining] string arguments) => this.Run("hcyl", arguments);

    [Command("/help", "worldedit"), RequirePermission, IssuerScope(CommandIssuers.Client)]
    public Task Help() => this.Run("help", "");

    internal static async Task<string> DispatchAsync(EditSession session, IPlayer player, string command, string arguments)
    {
        var args = arguments.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var level = player.Level;
        var origin = Position(player);
        switch (command)
        {
            case "wand": return await WorldEditTools.GiveWandAsync(player);
            case "pos1":
            case "pos2":
                return session.Select(level, ReadPosition(args, origin), command == "pos1");
            case "sel":
                session.ClearSelection();
                return "Selection cleared.";
            case "size":
                var region = session.Selection(level);
                return $"Selection: {region.Size.X} x {region.Size.Y} x {region.Size.Z} ({region.Volume:N0} blocks).";
            case "set":
            case "walls":
            case "outline":
                RequireCount(args, 1, 1, $"//{command} <pattern>");
                return Changed(await session.FillAsync(level, new BlockPattern(args[0]), command));
            case "replace":
                RequireCount(args, 2, 2, "//replace <from-blocks|*> <pattern>");
                return Changed(await session.FillAsync(level, new BlockPattern(args[1]), "replace", new BlockMask(args[0])));
            case "copy":
                return $"Copied {session.Copy(level, origin):N0} blocks relative to your position.";
            case "cut":
                session.Copy(level, origin);
                return Changed(await session.FillAsync(level, new BlockPattern("air"), "set"));
            case "paste":
                if (args.Any(arg => arg is not ("-a" or "-s" or "-as" or "-sa")))
                    throw new ArgumentException("Usage: //paste [-a] [-s]. -a skips air, -s selects the pasted region.");
                return Changed(await session.PasteAsync(level, origin, args.Any(arg => arg.Contains('a')), args.Any(arg => arg.Contains('s'))));
            case "rotate":
                RequireCount(args, 1, 1, "//rotate <degrees> (multiples of 90, around Y)");
                var degrees = Integer(args[0]);
                BlockTransforms.Turns(degrees);
                session.TransformClipboard(position => BlockTransforms.RotatePosition(position, degrees),
                    block => BlockTransforms.RotateBlock(block, degrees));
                return $"Clipboard rotated {degrees} degrees around Y.";
            case "flip":
                RequireCount(args, 0, 1, "//flip [x|y|z|direction]");
                var direction = Direction(args.Length == 0 ? "me" : args[0], player);
                var axis = direction.X != 0 ? "x" : direction.Y != 0 ? "y" : "z";
                session.TransformClipboard(position => BlockTransforms.FlipPosition(position, axis), block => BlockTransforms.FlipBlock(block, axis));
                return $"Clipboard flipped across {axis}.";
            case "undo": return $"Undid {await session.ReplayAsync(redo: false):N0} blocks.";
            case "redo": return $"Redid {await session.ReplayAsync(redo: true):N0} blocks.";
            case "expand":
            case "contract":
            case "shift":
                RequireCount(args, 1, 2, $"//{command} <amount> [direction]");
                var amount = Positive(args[0]);
                var offset = EditRegion.Scale(Direction(args.Length > 1 ? args[1] : "me", player), amount);
                Resize(session, player, offset, command);
                return "Selection updated.";
            case "stack":
            case "move":
                RequireCount(args, 0, 2, $"//{command} [count] [direction]");
                var count = args.Length == 0 ? 1 : Positive(args[0]);
                var along = Direction(args.Length > 1 ? args[1] : "me", player);
                return Changed(command == "stack"
                    ? await session.StackAsync(level, along, count)
                    : await session.MoveAsync(level, EditRegion.Scale(along, count)));
            case "sphere":
            case "hsphere":
            case "cyl":
            case "hcyl":
                var cylinder = command.EndsWith("cyl", StringComparison.Ordinal);
                RequireCount(args, 2, cylinder ? 3 : 2, cylinder ? $"//{command} <pattern> <radius> [height]" : $"//{command} <pattern> <radius>");
                return Changed(await session.ShapeAsync(level, origin, new BlockPattern(args[0]), Positive(args[1]),
                    args.Length == 3 ? Positive(args[2]) : 1, cylinder, command.StartsWith('h')));
            case "help":
                return "WorldEdit: //wand, //pos1 [x y z], //pos2 [x y z], //sel, //size; "
                    + "//set <pattern>, //replace <from> <pattern>, //walls <pattern>, //outline <pattern>; "
                    + "//copy, //cut, //paste [-a] [-s], //rotate <degrees>, //flip [axis]; //undo, //redo; "
                    + "//expand|contract|shift <amount> [direction], //stack|move [count] [direction]; "
                    + "//sphere|hsphere <pattern> <radius>, //cyl|hcyl <pattern> <radius> [height]. "
                    + $"Limit: {EditRegion.MaxBlocks:N0} blocks per operation, {EditSession.HistoryLimit} undo entries. Loaded chunks only.";
            default: throw new ArgumentException("Unknown WorldEdit command.");
        }
    }

    private static string Changed(int count) => $"Changed {count:N0} blocks.";

    internal static Vector Position(IPlayer player) => new(
        checked((int)Math.Floor(player.Position.X)), checked((int)Math.Floor(player.Position.Y)), checked((int)Math.Floor(player.Position.Z)));

    private static Vector ReadPosition(string[] args, Vector origin)
    {
        if (args.Length == 0) return origin;
        if (args.Length == 1) args = args[0].Split(',');
        RequireCount(args, 3, 3, "//pos1|pos2 [x y z] (relative ~ coordinates supported)");
        return new Vector(Coordinate(args[0], origin.X), Coordinate(args[1], origin.Y), Coordinate(args[2], origin.Z));
    }

    private static int Coordinate(string value, int origin) => value.StartsWith('~')
        ? checked(origin + (value.Length == 1 ? 0 : Integer(value[1..]))) : Integer(value);

    private static int Integer(string value) => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)
        ? number : throw new ArgumentException($"'{value}' must be a whole number.");

    private static int Positive(string value)
    {
        var number = Integer(value);
        if (number <= 0 || number > EditRegion.MaxBlocks)
            throw new ArgumentException($"Amount must be between 1 and {EditRegion.MaxBlocks:N0}.");
        return number;
    }

    private static void RequireCount(string[] args, int min, int max, string usage)
    {
        if (args.Length < min || args.Length > max)
            throw new ArgumentException("Usage: " + usage);
    }

    internal static Vector Direction(string value, IPlayer player)
    {
        if (value is "me" or "forward")
        {
            if (player.Pitch.Degrees is > 45 and < 135) return new(0, -1, 0);
            if (player.Pitch.Degrees is > 225 and < 315) return new(0, 1, 0);
            return ((int)Math.Floor(player.Yaw.Degrees / 90 + 0.5) % 4) switch
            {
                0 => new(0, 0, 1), 1 => new(-1, 0, 0), 2 => new(0, 0, -1), _ => new(1, 0, 0)
            };
        }
        return value.ToLowerInvariant() switch
        {
            "n" or "north" => new(0, 0, -1), "s" or "south" => new(0, 0, 1),
            "e" or "east" or "x" => new(1, 0, 0), "w" or "west" => new(-1, 0, 0),
            "u" or "up" or "y" => new(0, 1, 0), "d" or "down" => new(0, -1, 0),
            "z" => new(0, 0, 1),
            _ => throw new ArgumentException("Direction must be north, south, east, west, up, down or me.")
        };
    }

    private static void Resize(EditSession session, IPlayer player, Vector offset, string command)
    {
        var region = session.Selection(player.Level);
        if (command == "shift")
        {
            session.SetSelection(player.Level, region.Shift(offset));
            return;
        }
        var min = region.Min;
        var max = region.Max;
        if (command == "expand")
        {
            if (offset.X < 0 || offset.Y < 0 || offset.Z < 0) min = EditRegion.Add(min, offset);
            else max = EditRegion.Add(max, offset);
        }
        else
        {
            if (offset.X < 0 || offset.Y < 0 || offset.Z < 0) max = EditRegion.Add(max, offset);
            else min = EditRegion.Add(min, offset);
            if (min.X > max.X || min.Y > max.Y || min.Z > max.Z)
                throw new ArgumentException("Contraction would remove the entire selection.");
        }
        session.SetSelection(player.Level, new EditRegion(min, max));
    }
}
