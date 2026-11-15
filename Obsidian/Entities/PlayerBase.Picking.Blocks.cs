using Obsidian.API.BlockEntities;
using Obsidian.API.Inventory;
using Obsidian.API.Inventory.DataComponents;
using Obsidian.Nbt;
using Obsidian.Plugins;
using Obsidian.WorldData;

namespace Obsidian.Entities;

public partial class PlayerBase
{
    private async ValueTask<ItemStack?> ClonePickedBlockAsync(IBlock block, Vector position, bool includeData)
    {
        var name = block.UnlocalizedName;
        var itemName = name switch
        {
            "minecraft:wheat" => "minecraft:wheat_seeds",
            "minecraft:carrots" => "minecraft:carrot",
            "minecraft:potatoes" => "minecraft:potato",
            "minecraft:beetroots" => "minecraft:beetroot_seeds",
            "minecraft:cocoa" => "minecraft:cocoa_beans",
            "minecraft:torchflower_crop" => "minecraft:torchflower_seeds",
            "minecraft:pitcher_crop" => "minecraft:pitcher_pod",
            "minecraft:melon_stem" or "minecraft:attached_melon_stem" => "minecraft:melon_seeds",
            "minecraft:pumpkin_stem" or "minecraft:attached_pumpkin_stem" => "minecraft:pumpkin_seeds",
            "minecraft:bamboo_sapling" => "minecraft:bamboo",
            "minecraft:big_dripleaf_stem" => "minecraft:big_dripleaf",
            "minecraft:cave_vines" or "minecraft:cave_vines_plant" => "minecraft:glow_berries",
            "minecraft:sweet_berry_bush" => "minecraft:sweet_berries",
            "minecraft:kelp_plant" => "minecraft:kelp",
            "minecraft:twisting_vines_plant" => "minecraft:twisting_vines",
            "minecraft:weeping_vines_plant" => "minecraft:weeping_vines",
            "minecraft:tall_seagrass" => "minecraft:seagrass",
            "minecraft:redstone_wire" => "minecraft:redstone",
            "minecraft:tripwire" => "minecraft:string",
            "minecraft:piston_head" => block.GetProperty("type") == "sticky"
                ? "minecraft:sticky_piston" : "minecraft:piston",
            _ => name
        };
        if (name.EndsWith("_candle_cake", StringComparison.Ordinal) || name == "minecraft:candle_cake")
            itemName = "minecraft:cake";
        if (name.StartsWith("minecraft:potted_", StringComparison.Ordinal))
        {
            itemName = "minecraft:" + name[17..];
            if (itemName.EndsWith("azalea_bush", StringComparison.Ordinal))
                itemName = itemName[..^5];
        }

        itemName = itemName.Replace("_wall_hanging_sign", "_hanging_sign")
            .Replace("_wall_sign", "_sign").Replace("_wall_banner", "_banner")
            .Replace("_wall_head", "_head").Replace("_wall_skull", "_skull")
            .Replace("wall_torch", "torch");
        if (!ItemsRegistry.TryGet(itemName, out var holder)
            && !BlocksRegistry.TryGetItem(block, out holder))
        {
            return null;
        }

        if (name is "minecraft:nether_portal" or "minecraft:end_portal" or "minecraft:end_gateway"
            or "minecraft:frosted_ice" or "minecraft:moving_piston")
        {
            return null;
        }

        var item = new ItemStack(holder);
        var components = new NbtCompound("components");
        if (name == "minecraft:light")
            PickState(components, "level", block.GetProperty("level")!);
        if (name == "minecraft:test_block")
            PickState(components, "mode", block.GetProperty("mode")!);
        if (includeData && name is "minecraft:bee_nest" or "minecraft:beehive")
            PickState(components, "honey_level", block.GetProperty("honey_level")!);

        if (name.EndsWith("copper_golem_statue", StringComparison.Ordinal))
            PickState(components, "copper_golem_pose", block.GetProperty("copper_golem_pose")!);

        var statue = name.EndsWith("copper_golem_statue", StringComparison.Ordinal);
        var banner = name.EndsWith("_banner", StringComparison.Ordinal);
        var pot = name == "minecraft:decorated_pot";
        if (includeData || banner || pot || statue)
        {
            if (await this.Level.GetBlockEntityAsync(position) is BlockEntity entity)
            {
                var data = BlockEntityNbt.Save(entity);
                if (data is not null)
                {
                    foreach (var field in new[] { "id", "x", "y", "z", "keepPacked", "obsidian:container" })
                        data.Remove(field);
                    if ((includeData || banner || statue)
                        && data.TryGetTag<NbtCompound>("components", out var stored))
                    {
                        foreach (var (key, value) in stored)
                            SetPickedTag(components, key, value);
                    }
                    data.Remove("components");

                    CollectPickedComponents(entity, data, components, includeData, banner, pot);
                    if (includeData && data.Count > 0)
                    {
                        data.Add(new NbtTag<string>("id", entity.Id));
                        SetPickedTag(components, "minecraft:block_entity_data", data);
                    }
                }

                if (includeData && entity.Owner is PluginContainer plugin && this.Level is AbstractLevel level)
                {
                    foreach (var component in await level.CollectPluginComponentsAsync(entity, plugin, holder))
                        item[component.Type] = component;
                }
            }
        }

        // Reuse the saved-component codecs for modeled and retained vanilla block-entity data.
        var saved = item.ToNbt();
        if (saved.TryGetTag<NbtCompound>("components", out var patch))
        {
            foreach (var (key, value) in components)
                SetPickedTag(patch, key, value);
        }
        else
        {
            saved.Add(components);
        }

        return saved.ItemFromNbt();
    }

    private static void SetPickedTag(NbtCompound target, string name, INbtTag value)
    {
        var copy = DataBlockEntity.Copy(value);
        copy.Name = name;
        target.Remove(name);
        target.Add(copy);
    }

    private static void PickState(NbtCompound components, string name, string value) =>
        SetPickedTag(components, "minecraft:block_state", new NbtCompound
        {
            new NbtTag<string>(name, value)
        });

    private static void CollectPickedComponents(
        BlockEntity entity,
        NbtCompound data,
        NbtCompound components,
        bool includeData,
        bool banner,
        bool pot)
    {
        if (banner)
        {
            Move("patterns", "minecraft:banner_patterns");
            Move("CustomName", "minecraft:custom_name");
        }
        if (pot)
            Move("sherds", "minecraft:pot_decorations");
        if (!includeData)
            return;

        if (entity is ContainerBlockEntity || entity.Id is "minecraft:beacon" or "minecraft:enchanting_table"
            or "minecraft:command_block")
        {
            Move("CustomName", "minecraft:custom_name");
            Move("lock", "minecraft:lock");
        }
        if (entity is ContainerBlockEntity || entity.Id is "minecraft:campfire" or "minecraft:chiseled_bookshelf"
            or "minecraft:shelf")
        {
            var contents = new NbtList(NbtTagType.Compound, "minecraft:container");
            if (data.TryGetTag<NbtList>("Items", out var items))
            {
                foreach (var stack in items.OfType<NbtCompound>())
                {
                    var copy = DataBlockEntity.Copy(stack);
                    var slot = copy.TryGetNumber<int>("Slot", out var index) ? index : 0;
                    copy.Remove("Slot");
                    contents.Add(new NbtCompound
                    {
                        new NbtTag<int>("slot", slot),
                        new NbtCompound("item", copy.Select(pair => pair.Value).ToList())
                    });
                }
            }
            data.Remove("Items");
            SetPickedTag(components, "minecraft:container", contents);
        }
        if (data.TryGetTagValue<string>("LootTable", out var table))
        {
            var loot = new NbtCompound
            {
                new NbtTag<string>("loot_table", table)
            };
            if (data.TryGetNumber<long>("LootTableSeed", out var seed) && seed != 0)
                loot.Add(new NbtTag<long>("seed", seed));
            SetPickedTag(components, "minecraft:container_loot", loot);
            data.Remove("LootTable");
            data.Remove("LootTableSeed");
        }
        if (pot)
        {
            var contents = new NbtList(NbtTagType.Compound, "minecraft:container");
            if (data.TryGetTag<NbtCompound>("item", out var stack))
            {
                contents.Add(new NbtCompound
                {
                    new NbtTag<int>("slot", 0),
                    new NbtCompound("item", stack.Select(pair => pair.Value).ToList())
                });
            }
            data.Remove("item");
            SetPickedTag(components, "minecraft:container", contents);
        }
        if (entity.Id == "minecraft:beehive")
            Move("bees", "minecraft:bees");
        if (entity.Id is "minecraft:sign" or "minecraft:hanging_sign")
        {
            Move("front_text", "minecraft:sign_text_front");
            Move("back_text", "minecraft:sign_text_back");
            if (data.TryGetBool("is_waxed", out var waxed) && waxed)
                SetPickedTag(components, "minecraft:waxed", new NbtCompound());
            data.Remove("is_waxed");
        }
        if (entity.Id == "minecraft:skull")
        {
            Move("profile", "minecraft:profile");
            Move("note_block_sound", "minecraft:note_block_sound");
            Move("custom_name", "minecraft:custom_name");
        }
        if (entity.Id == "minecraft:command_block")
        {
            data.Remove("conditionMet");
            data.Remove("powered");
        }

        void Move(string field, string component)
        {
            if (data.TryGetTag(field, out var value))
            {
                SetPickedTag(components, component, value);
                data.Remove(field);
            }
        }
    }
}
