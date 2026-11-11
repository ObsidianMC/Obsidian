using Obsidian.API.Commands;
using Obsidian.WorldData;
using Obsidian.WorldData.Structures;
using System.Globalization;

namespace Obsidian.Commands.Modules;

public sealed partial class PlaceCommandModule
{
    // Shares the world's generated-template cache with structure blocks. Reads are worker work; placement is a
    // short, synchronous mutation under the tick lock, after the affected chunks have loaded.
    private async Task<int> PlaceTemplateAsync(CommandInput input)
    {
        var name = StructureBlocks.NormalizeName(input.Read());
        if (name.Length == 0)
            throw new FormatException("Invalid template name");

        if (CommandFeedback.LevelOf(this.CommandContext) is not AbstractLevel level)
            throw new FormatException("This level cannot place templates");

        var origin = CommandFeedback.OriginOf(this.CommandContext);
        if (!input.End && !CommandCoordinates.TryParse(input.Position(), this.CommandContext, false, true, out origin))
            throw new FormatException("Invalid template position");

        var rotation = input.End
            ? 0 : Array.IndexOf(StructureBlockEntity.RotationNames, input.Read().ToUpperInvariant());
        var mirror = input.End ? 0 : Array.IndexOf(StructureBlockEntity.MirrorNames, input.Read().ToUpperInvariant());
        var integrity = input.End ? 1 : CommandInput.Float(input.Read());
        var seed = input.End ? 0 : int.Parse(input.Read(), CultureInfo.InvariantCulture);
        input.ExpectEnd();
        if (rotation < 0 || mirror < 0 || !float.IsFinite(integrity) || integrity is < 0 or > 1)
            throw new FormatException("Invalid template placement settings");

        var template = await StructureBlockTemplates.For(level).ReadAsync(name)
            ?? throw new FormatException($"Invalid template '{name}'");
        var settings = StructureBlocks.Placement(new StructureBlockSettings
        {
            Rotation = (StructureRotation)rotation,
            Mirror = (StructureMirror)mirror,
            Integrity = integrity,
            Seed = seed,
            IgnoreEntities = false
        });
        var position = (Vector)origin.Floor();
        var box = template.GetBoundingBox(settings, position);
        using var area = await StructureBlocks.LoadAreaAsync(level, new Vector(box.MinX, box.MinY, box.MinZ),
            new Vector(box.MaxX - box.MinX + 1, box.MaxY - box.MinY + 1, box.MaxZ - box.MinZ + 1));
        lock (level.Fluids.TickLock)
        {
            var view = new StructureBlockLevel(level, strict: false);
            if (!template.PlaceInWorld(view, position, position, settings, StructureBlocks.Random(seed)))
                throw new FormatException("Template placement failed");
        }

        await this.CommandContext.SuccessAsync($"Placed template {name} at {CommandFeedback.Format(position)}");
        return 1;
    }
}
