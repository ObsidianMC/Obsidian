using Obsidian.API.Inventory;
using Obsidian.API.Inventory.DataComponents;
using Obsidian.Nbt;
using System.IO;

namespace Obsidian.API.Utilities;

public partial class Extensions
{
    // AdventureModePredicate is one BlockPredicate or a list; component matchers are named maps on each predicate.
    private static NbtList PredicatesToNbt(string name, BlockPredicatesDataComponent component) =>
        SavedList(name, component.Predicates.Select(PredicateToNbt));

    private static NbtCompound PredicateToNbt(BlockPredicate predicate)
    {
        var tag = new NbtCompound();
        if (predicate.Blocks is IdSet blocks)
            tag.Add(SetToNbt("blocks", blocks, ComponentValueCodecs.Blocks));
        else if (predicate.BlockIds.Length > 0)
            tag.Add(SavedStrings("blocks", predicate.BlockIds));

        if (predicate.Properties is ImmutableArray<BlockProperty> properties)
        {
            var state = new NbtCompound("state");
            foreach (var property in properties)
            {
                if (property.IsExactMatch)
                {
                    state.Add(new NbtTag<string>(property.Name, property.ExactValue!));
                    continue;
                }

                var range = new NbtCompound(property.Name);
                if (property.MinValue is not null)
                    range.Add(new NbtTag<string>("min", property.MinValue));

                if (property.MaxValue is not null)
                    range.Add(new NbtTag<string>("max", property.MaxValue));

                state.Add(range);
            }

            tag.Add(state);
        }

        if (predicate.Nbt is not null)
            tag.Add(CopyCompound("nbt", predicate.Nbt));

        if (predicate.ExactComponents.Length > 0)
        {
            var components = new NbtCompound("components");
            foreach (var component in predicate.ExactComponents)
            {
                components.Add(ComponentToNbt(component)
                    ?? throw new NotSupportedException($"No saved codec for {component.Identifier}."));
            }

            tag.Add(components);
        }

        if (predicate.PartialComponents.Length > 0)
        {
            var components = new NbtCompound("predicates");
            foreach (var component in predicate.PartialComponents)
            {
                var registry = component.IsPredicateType
                    ? "minecraft:data_component_predicate_type" : ComponentValueCodecs.DataComponentTypes;
                components.Add(CopyCompound(ComponentRegistryNames.Name(registry, component.TypeId), component.Value));
            }

            tag.Add(components);
        }

        return tag;
    }

    private static BlockPredicate PredicateFromNbt(NbtCompound tag)
    {
        var exact = ImmutableArray.CreateBuilder<DataComponent>();
        if (tag.TryGetTag<NbtCompound>("components", out var components))
        {
            foreach (var (name, value) in components)
            {
                exact.Add(ComponentFromNbt(name, value)
                    ?? throw new NotSupportedException($"Cannot model predicate component {name}."));
            }
        }

        var partial = ImmutableArray.CreateBuilder<ComponentPredicate>();
        if (tag.TryGetTag<NbtCompound>("predicates", out var predicates))
        {
            foreach (var (name, value) in predicates)
            {
                var isPredicate = true;
                int id;
                try
                {
                    id = ComponentRegistryNames.Id("minecraft:data_component_predicate_type", name);
                }
                catch (InvalidDataException)
                {
                    isPredicate = false;
                    id = ComponentRegistryNames.Id(ComponentValueCodecs.DataComponentTypes, name);
                }

                partial.Add(new ComponentPredicate
                {
                    IsPredicateType = isPredicate,
                    TypeId = id,
                    Value = (NbtCompound)value
                });
            }
        }

        return new BlockPredicate
        {
            Blocks = tag.TryGetTag("blocks", out var blocks) ? SetFromNbt(blocks, ComponentValueCodecs.Blocks) : null,
            Properties = tag.TryGetTag<NbtCompound>("state", out var state)
                ? [.. state.Select(pair => pair.Value is NbtTag<string> value
                    ? new BlockProperty
                    {
                        Name = pair.Key,
                        IsExactMatch = true,
                        ExactValue = value.Value
                    }
                    : new BlockProperty
                    {
                        Name = pair.Key,
                        IsExactMatch = false,
                        MinValue = ((NbtCompound)pair.Value).SavedString("min"),
                        MaxValue = ((NbtCompound)pair.Value).SavedString("max")
                    })]
                : null,
            Nbt = tag.TryGetTag<NbtCompound>("nbt", out var nbt) ? nbt : null,
            ExactComponents = exact.ToImmutable(),
            PartialComponents = partial.ToImmutable()
        };
    }

    private static DataComponent? ListComponentFromNbt(DataComponentType type, INbtTag tag)
    {
        switch (type)
        {
            case DataComponentType.AttributeModifiers when tag is NbtList list:
                return AttributesFromNbt(list);
            case DataComponentType.BannerPatterns when tag is NbtList list:
                return PatternsFromNbt(list);
            case DataComponentType.Bees when tag is NbtList list:
                return new BeesDataComponent
                {
                    Occupants = [.. list.OfType<NbtCompound>().Select(bee =>
                        (EntityDataFromNbt(DataComponentType.EntityData, (NbtCompound)bee["entity_data"]),
                            SavedInt(bee, "ticks_in_hive"), SavedInt(bee, "min_ticks_in_hive"))) ]
                };
            case DataComponentType.CanBreak:
            case DataComponentType.CanPlaceOn:
                var predicates = tag is NbtList entries ? entries.OfType<NbtCompound>() : [(NbtCompound)tag];
                var prototype = (BlockPredicatesDataComponent)ComponentBuilder.Create(type)!;
                return prototype with { Predicates = [.. predicates.Select(PredicateFromNbt)] };
            default:
                return null;
        }
    }
}
