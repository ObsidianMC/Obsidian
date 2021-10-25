using Obsidian.API;
using Obsidian.Nbt;

namespace Obsidian.Utilities
{
    public partial class Extensions
    {
        public static ItemStack ItemFromNbt(this INbtTag tag)
        {
            var item = tag as NbtCompound;

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

                            //Globals.PacketLogger.LogDebug($"List Type: {enchantments.ListType}");

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

                            itemMetaBuilder.WithSlot(byteTag.Value);
                            //Console.WriteLine($"Setting slot: {itemMetaBuilder.Slot}");
                            break;
                        }

                    case "DAMAGE":
                        {
                            var intTag = (NbtTag<int>)child;

                            itemMetaBuilder.WithDurability(intTag.Value);
                            //Globals.PacketLogger.LogDebug($"Setting damage: {tag.IntValue}");
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
}
