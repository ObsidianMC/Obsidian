using Obsidian.API.BlockEntities;
using Obsidian.API.Commands;
using Obsidian.API.Data;
using Obsidian.Entities;
using Obsidian.Nbt;
using Obsidian.WorldData;
using System.Globalization;

namespace Obsidian.Commands.Modules;

public sealed partial class DataCommandModule
{
    [Command("modify")]
    public async Task<int> ModifyAsync([Remaining] string arguments)
    {
        try
        {
            var input = CommandInput.Arguments(this.CommandContext);
            input.Expect("modify");
            var (tag, commit) = await this.TargetAsync(input);
            var path = input.Read();
            var operation = input.Read();
            var index = operation == "insert" ? int.Parse(input.Read(), CultureInfo.InvariantCulture) : 0;
            input.Expect("compute");
            var numbers = await ComputeCommandModule.CreateContextAsync(this.CommandContext, input);
            var kind = input.Read();
            var provider = ComputeCommandModule.Provider(input.Read());
            input.ExpectEnd();
            INbtTag value = kind switch
            {
                "integer" => new NbtTag<int>("", ContextNumberProviders.ReadInt(provider).GetIntOrThrow(numbers)),
                "float" => new NbtTag<float>("", ContextNumberProviders.ReadFloat(provider).GetFloatOrThrow(numbers)),
                _ => throw new FormatException("Expected integer or float")
            };

            var changed = ModifyNumber(tag, path, operation, index, value);
            if (!changed)
                throw new FormatException("The data did not change");

            commit(tag);
            await this.CommandContext.SuccessAsync("Modified data");
            return 1;
        }
        catch (Exception exception) when (ComputeCommandModule.IsInputError(exception))
        {
            await this.CommandContext.FailAsync(exception.Message);
            return 0;
        }
    }

    private async Task<(NbtCompound Tag, Action<NbtCompound> Commit)> TargetAsync(CommandInput input)
    {
        switch (input.Read())
        {
            case "storage":
                var identifier = CommandInput.Qualify(input.Read());
                return (CommandStorage.Read(this.Server, identifier),
                    tag => CommandStorage.Write(this.Server, identifier, tag));

            case "entity":
                if (!EntitySelector.TryParse(input.Read(), out var selector) || !selector.Single)
                    throw new FormatException("Expected one entity");

                var target = selector.Select(this.CommandContext).SingleOrDefault();
                if (target is IPlayer)
                    throw new FormatException("Player data cannot be modified");

                if (target is not Entity entity)
                    throw new FormatException("No entity was found");

                var data = new NbtCompound();
                entity.WriteNbt(data);
                return (data, entity.ReadNbt);

            case "block":
                if (!CommandCoordinates.TryParse(input.Position(), this.CommandContext, false, true, out var position))
                    throw new FormatException("Invalid block position");

                var blockEntity = await CommandFeedback.LevelOf(this.CommandContext).GetBlockEntityAsync((Vector)position);
                if (blockEntity is not BlockEntity concrete || BlockEntityNbt.Save(concrete) is not NbtCompound saved)
                    throw new FormatException("The target block is not a block entity");

                return (saved, tag => BlockEntityNbt.LoadInto(concrete, tag));

            default:
                throw new FormatException("Expected storage, entity or block");
        }
    }

    internal static bool ModifyNumber(NbtCompound tag, string path, string operation, int index, INbtTag value)
    {
        if (operation == "merge")
            throw new FormatException("A computed number is not a compound");

        if (operation is "append" or "prepend" or "insert")
        {
            var found = Find(tag, path);
            if (found is null)
            {
                var created = new NbtList(value.Type);
                SetPath(tag, path, created);
                found = created;
            }

            if (found is not NbtList list)
                throw new FormatException("Expected a list");

            if (list.Count > 0 && list.ListType != value.Type)
                return false;

            var at = operation switch
            {
                "append" => list.Count,
                "prepend" => 0,
                _ => index < 0 ? list.Count + index + 1 : index
            };
            if (at < 0 || at > list.Count)
                throw new FormatException("Invalid list index");

            list.Insert(at, value);
            return true;
        }

        if (operation != "set")
            throw new FormatException("Unknown data operation");

        var previous = Find(tag, path);
        if (previous is not null && Snbt.Write(previous) == Snbt.Write(value))
            return false;

        SetPath(tag, path, value);
        return true;
    }

    private static void SetPath(NbtCompound tag, string path, INbtTag value)
    {
        // Match the existing /data get path subset; reject wildcard and predicate paths before changing data.
        var parts = path.Split('.');
        var parent = tag;
        foreach (var part in parts[..^1])
        {
            if (part.Length == 0 || part.IndexOfAny(['[', ']', '{', '}']) >= 0)
                throw new FormatException("Unsupported NBT path");

            if (!parent.TryGetTag<NbtCompound>(part, out var child))
            {
                if (parent.HasTag(part))
                    throw new FormatException("Expected a compound in path");

                child = new NbtCompound(part);
                parent.Add(child);
            }

            parent = child;
        }

        var name = parts[^1];
        var bracket = name.IndexOf('[');
        if (bracket >= 0)
        {
            var owner = Find(parent, name[..bracket]) as NbtList;
            if (owner is null || !name.EndsWith(']')
                || !int.TryParse(name[(bracket + 1)..^1], out var slot))
            {
                throw new FormatException("Unsupported NBT path");
            }

            if (slot < 0)
                slot += owner.Count;

            if (slot < 0 || slot >= owner.Count || owner.ListType != value.Type)
                throw new FormatException("Invalid list element");

            owner[slot] = value;
            return;
        }

        if (name.Length == 0 || name.IndexOfAny([']', '{', '}']) >= 0)
            throw new FormatException("Unsupported NBT path");

        name = name.Trim('"');
        value.Name = name;
        parent.Remove(name);
        parent.Add(value);
    }
}
