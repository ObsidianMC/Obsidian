using Obsidian.API.Commands;
using Obsidian.WorldData;
using Obsidian.WorldData.Structures;
using System.Globalization;

namespace Obsidian.Commands.Modules;

public sealed partial class PlaceCommandModule
{
    // Shares the generated-template cache with structure blocks. File IO stays on workers; only an already
    // loaded destination is mutated, synchronously under the tick lock.
    private async Task<int> PlaceTemplateAsync(CommandInput input)
    {
        var name = StructureBlocks.NormalizeName(input.Read());
        if (name.Length == 0)
            throw new FormatException("Invalid template name");

        if (!StructureTemplateNames.IsValid(name))
            throw new FormatException($"Invalid template '{name}'");

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
        var strict = !input.End;
        if (strict)
            input.Expect("strict");

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
            IgnoreEntities = false,
            Strict = strict
        });
        var position = (Vector)origin.Floor();
        lock (level.Fluids.TickLock)
        {
            RequireLoadedTemplateArea(level, position, template.Size);
            var view = new StructureBlockLevel(level, strict);
            if (!template.PlaceInWorld(view, position, position, settings, StructureBlocks.Random(seed)))
                throw new FormatException("Template placement failed");
        }

        await this.CommandContext.SuccessAsync($"Loaded template \"{name}\" at {CommandFeedback.Format(position)}");
        return 1;
    }

    private static void RequireLoadedTemplateArea(AbstractLevel level, Vector position, Vector size)
    {
        // 26.3 checks the unrotated position..position+size chunk range, including the far endpoint.
        // Reads here never load a region or enqueue generation, unlike the structure block's LOAD action.
        var end = position + size;
        for (var x = position.X >> 4; x <= end.X >> 4; x++)
        {
            for (var z = position.Z >> 4; z <= end.Z >> 4; z++)
            {
                if (level.GetLoadedChunk(x, z) is not { IsGenerated: true })
                    throw new FormatException("That position is not loaded");
            }
        }
    }
}
