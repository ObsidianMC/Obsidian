using Obsidian.Nbt;

namespace Obsidian.Utilities;

public partial class Extensions
{
    public static NbtCompound ToNbt(this ItemStack value)
    {
        value ??= new ItemStack(0, 0) { Present = true };

        var item = value.AsItem();

        var compound = new NbtCompound
            {
                new NbtTag<string>("id", item.UnlocalizedName),
                new NbtTag<byte>("Count", (byte)value.Count),
                new NbtTag<byte>("Slot", (byte)value.Slot)
            };

        ItemMeta meta = value.ItemMeta;

        if (meta.HasTags())
        {
            compound.Add(new NbtTag<bool>("Unbreakable", meta.Unbreakable));

            if (meta.Durability > 0)
                compound.Add(new NbtTag<int>("Damage", meta.Durability));

            if (meta.CustomModelData > 0)
                compound.Add(new NbtTag<int>("CustomModelData", meta.CustomModelData));

            if (meta.CanDestroy is not null)
            {
                var list = new NbtList(NbtTagType.String, "CanDestroy");

                foreach (var block in meta.CanDestroy)
                    list.Add(new NbtTag<string>(string.Empty, block));

                compound.Add(list);
            }

            if (meta.Name is not null)
            {
                var displayCompound = new NbtCompound("display")
                        {
                            new NbtTag<string>("Name", new List<ChatMessage> { meta.Name }.ToJson())
                        };

                if (meta.Lore is not null)
                {
                    var list = new NbtList(NbtTagType.String, "Lore");

                    foreach (var lore in meta.Lore)
                        list.Add(new NbtTag<string>(string.Empty, new List<ChatMessage> { lore }.ToJson()));

                    displayCompound.Add(list);
                }

                compound.Add(displayCompound);
            }
            else if (meta.Lore is not null)
            {
                var displayCompound = new NbtCompound("display")
                        {
                            new NbtTag<string>("Name", new List<ChatMessage> { meta.Name }.ToJson())
                        };

                var list = new NbtList(NbtTagType.String, "Lore");

                foreach (var lore in meta.Lore)
                    list.Add(new NbtTag<string>(string.Empty, new List<ChatMessage> { lore }.ToJson()));

                displayCompound.Add(list);

                compound.Add(displayCompound);
            }
        }

        return compound;
    }

    public static ItemStack ItemFromNbt(this NbtCompound item)
    {
        if (item is null)
            return null;

        var itemStack = Registry.Registry.GetSingleItem(item.GetString("id"));

        var itemMetaBuilder = new ItemMetaBuilder();

        foreach (var (name, child) in item)
        {
            switch (name.ToUpperInvariant())
            {
                case "ENCHANTMENTS":
                    {
                        var enchantments = (NbtList)child;

                        foreach (var enchant in enchantments)
                        {
                            if (enchant is NbtCompound compound)
                            {
                                itemMetaBuilder.AddEnchantment(compound.GetString("id").ToEnchantType(), compound.GetShort("lvl"));
                            }
                        }

                        break;
                    }

                case "STOREDENCHANTMENTS":
                    {
                        var enchantments = (NbtList)child;

                        foreach (var enchantment in enchantments)
                        {
                            if (enchantment is NbtCompound compound)
                            {
                                compound.TryGetTag("id", out var id);
                                compound.TryGetTag("lvl", out var lvl);

                                itemMetaBuilder.AddStoredEnchantment(compound.GetString("id").ToEnchantType(), compound.GetShort("lvl"));
                            }
                        }
                        break;
                    }

                case "SLOT":
                    {
                        var byteTag = (NbtTag<byte>)child;

                        itemStack.Slot = byteTag.Value;
                        break;
                    }

                case "DAMAGE":
                    {
                        var intTag = (NbtTag<int>)child;

                        itemMetaBuilder.WithDurability(intTag.Value);
                        break;
                    }

                case "DISPLAY":
                    {
                        var display = (NbtCompound)child;

                        foreach (var (displayTagName, displayTag) in display)
                        {
                            if (displayTagName.EqualsIgnoreCase("name") && displayTag is NbtTag<string> stringTag)
                            {
                                itemMetaBuilder.WithName(stringTag.Value);
                            }
                            else if (displayTag.Name.EqualsIgnoreCase("lore"))
                            {
                                var loreTag = (NbtList)displayTag;

                                foreach (NbtTag<string> lore in loreTag)
                                    itemMetaBuilder.AddLore(lore.Value.FromJson<ChatMessage>());
                            }
                        }
                        break;
                    }
            }
        }

        itemStack.ItemMeta = itemMetaBuilder.Build();

        return itemStack;
    }
}
