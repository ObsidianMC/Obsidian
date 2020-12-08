using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Obsidian.API;
using Obsidian.Boss;
using Obsidian.Chat;
using Obsidian.Commands;
using Obsidian.Crafting;
using Obsidian.Entities;
using Obsidian.Items;
using Obsidian.Nbt;
using Obsidian.Nbt.Tags;
using Obsidian.Net.Packets.Play.Client;
using Obsidian.PlayerData.Info;
using Obsidian.Serializer.Attributes;
using Obsidian.Serializer.Enums;
using Obsidian.Util.Extensions;
using Obsidian.Util.Registry.Codecs.Dimensions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Obsidian.Net
{
    public partial class MinecraftStream
    {
        public static Encoding StringEncoding { get; } = Encoding.UTF8;

        public readonly SemaphoreSlim Lock = new SemaphoreSlim(1, 1);

        #region Writing

        public async Task WriteEntityMetdata(byte index, EntityMetadataType type, object value, bool optional = false)
        {
            await this.WriteUnsignedByteAsync(index);
            await this.WriteVarIntAsync((int)type);
            switch (type)
            {
                case EntityMetadataType.Byte:
                    await this.WriteUnsignedByteAsync((byte)value);
                    break;

                case EntityMetadataType.VarInt:
                    await this.WriteVarIntAsync((int)value);
                    break;

                case EntityMetadataType.Float:
                    await this.WriteFloatAsync((float)value);
                    break;

                case EntityMetadataType.String:
                    await this.WriteStringAsync((string)value);
                    break;

                case EntityMetadataType.Chat:
                    await this.WriteChatAsync((ChatMessage)value);
                    break;

                case EntityMetadataType.OptChat:
                    await this.WriteBooleanAsync(optional);

                    if (optional)
                        await this.WriteChatAsync((ChatMessage)value);
                    break;

                case EntityMetadataType.Slot:
                    await this.WriteSlotAsync((ItemStack)value);
                    break;

                case EntityMetadataType.Boolean:
                    await this.WriteBooleanAsync((bool)value);
                    break;

                case EntityMetadataType.Rotation:
                    break;

                case EntityMetadataType.Position:
                    await this.WritePositionAsync((Position)value);
                    break;

                case EntityMetadataType.OptPosition:
                    await this.WriteBooleanAsync(optional);

                    if (optional)
                        await this.WritePositionAsync((Position)value);

                    break;

                case EntityMetadataType.Direction:
                    break;

                case EntityMetadataType.OptUuid:
                    await this.WriteBooleanAsync(optional);

                    if (optional)
                        await this.WriteUuidAsync((Guid)value);
                    break;

                case EntityMetadataType.OptBlockId:
                    await this.WriteVarIntAsync((int)value);
                    break;

                case EntityMetadataType.Nbt:
                case EntityMetadataType.Particle:
                case EntityMetadataType.VillagerData:
                case EntityMetadataType.OptVarInt:
                    if (optional)
                    {
                        await this.WriteVarIntAsync(0);
                        break;
                    }
                    await this.WriteVarIntAsync(1 + (int)value);
                    break;
                case EntityMetadataType.Pose:
                    await this.WriteVarIntAsync((Pose)value);
                    break;
                default:
                    break;
            }
        }

        public async Task WriteUuidAsync(Guid value)
        {
            //var arr = value.ToByteArray();
            BigInteger uuid = BigInteger.Parse(value.ToString().Replace("-", ""), System.Globalization.NumberStyles.HexNumber);
            await this.WriteAsync(uuid.ToByteArray(false, true));
        }

        public async Task WriteChatAsync(ChatMessage value) => await this.WriteStringAsync(value.ToString());


        [Obsolete("Shouldn't be used anymore")]
        public async Task WriteAutoAsync(object value, bool countLength = false)
        {
            switch (value)
            {
                case Enum enumValue:
                    if (enumValue is PositionFlags flags)
                    {
                        await this.WriteByteAsync((sbyte)flags);
                        break;
                    }

                    await this.WriteVarIntAsync(enumValue);
                    break;

                case Velocity velocity:
                    await this.WriteShortAsync(velocity.X);
                    await this.WriteShortAsync(velocity.Y);
                    await this.WriteShortAsync(velocity.Z);
                    break;

                case SoundPosition soundPos:
                    await this.WriteIntAsync(soundPos.X);
                    await this.WriteIntAsync(soundPos.Y);
                    await this.WriteIntAsync(soundPos.Z);
                    break;

                case BossBarAction actionValue:
                    await this.WriteVarIntAsync(actionValue.Action);
                    await this.WriteAsync(await actionValue.ToArrayAsync());
                    break;

                case List<CommandNode> nodes:
                    await this.WriteVarIntAsync(nodes.Count);
                    foreach (var node in nodes)
                        await node.CopyToAsync(this);

                    await this.WriteVarIntAsync(0);
                    break;

                case List<PlayerInfoAction> actions:
                    await this.WriteVarIntAsync(actions.Count);

                    foreach (var action in actions)
                        await action.WriteAsync(this);

                    break;

                case byte[] byteArray:
                    if (countLength)
                    {
                        await this.WriteVarIntAsync(byteArray.Length);
                        await this.WriteAsync(byteArray);
                    }
                    else
                    {
                        await this.WriteAsync(byteArray);
                    }
                    break;

                default:
                    throw new InvalidOperationException($"Can't handle {value} ({value.GetType()})");
            }
        }

        public async Task WriteAsync(DataType type, FieldAttribute attribute, object value, int length = 32767)
        {
            switch (type)
            {
                case DataType.Auto:
                    {
                        if (value is Player player)
                        {
                            await this.WriteUnsignedByteAsync(0xff);
                        }
                        else
                        {
                            await this.WriteAutoAsync(value);
                        }

                        break;
                    }
                case DataType.Angle:
                    {
                        await this.WriteAngleAsync((Angle)value);
                        break;
                    }
                case DataType.Boolean:
                    {
                        await this.WriteBooleanAsync((bool)value);
                        break;
                    }
                case DataType.Byte:
                    {
                        await this.WriteByteAsync((sbyte)value);
                        break;
                    }
                case DataType.UnsignedByte:
                    {
                        await this.WriteUnsignedByteAsync((byte)value);
                        break;
                    }
                case DataType.Short:
                    {
                        await this.WriteShortAsync((short)value);
                        break;
                    }
                case DataType.UnsignedShort:
                    {
                        await this.WriteUnsignedShortAsync((ushort)value);
                        break;
                    }
                case DataType.Int:
                    {
                        await this.WriteIntAsync((int)value);
                        break;
                    }
                case DataType.Long:
                    {
                        await this.WriteLongAsync((long)value);
                        break;
                    }
                case DataType.Float:
                    {
                        if (value is Enum _enum)
                        {
                            value = Convert.ChangeType(value, _enum.GetTypeCode());
                            await this.WriteFloatAsync(Convert.ToSingle(value));
                        }
                        else
                            await this.WriteFloatAsync((float)value);
                        break;
                    }
                case DataType.Double:
                    {
                        await this.WriteDoubleAsync((double)value);
                        break;
                    }

                case DataType.String:
                    {
                        // TODO: add casing options on Field attribute and support custom naming enums.
                        var val = value.GetType().IsEnum ? value.ToString().ToCamelCase() : value.ToString();
                        await this.WriteStringAsync(val, length);
                        break;
                    }
                case DataType.Chat:
                    {
                        await this.WriteChatAsync((ChatMessage)value);
                        break;
                    }
                case DataType.VarInt:
                    {
                        await this.WriteVarIntAsync((int)value);
                        break;
                    }
                case DataType.VarLong:
                    {
                        await this.WriteVarLongAsync((long)value);
                        break;
                    }
                case DataType.Position:
                    {
                        if (value is Position position)
                        {
                            if (attribute.Absolute)
                            {
                                await this.WriteDoubleAsync(position.X);
                                await this.WriteDoubleAsync(position.Y);
                                await this.WriteDoubleAsync(position.Z);
                                break;
                            }

                            await this.WritePositionAsync(position);
                        }
                        else if (value is SoundPosition soundPosition)
                        {
                            await this.WriteIntAsync(soundPosition.X);
                            await this.WriteIntAsync(soundPosition.Y);
                            await this.WriteIntAsync(soundPosition.Z);
                        }

                        break;
                    }
                case DataType.Velocity:
                    {
                        var velocity = (Velocity)value;

                        await this.WriteShortAsync(velocity.X);
                        await this.WriteShortAsync(velocity.Y);
                        await this.WriteShortAsync(velocity.Z);
                        break;
                    }
                case DataType.UUID:
                    {
                        await this.WriteUuidAsync((Guid)value);
                        break;
                    }
                case DataType.Array:
                    {
                        if (value is List<CommandNode> nodes)
                        {
                            foreach (var node in nodes)
                                await node.CopyToAsync(this);
                        }
                        else if (value is List<PlayerInfoAction> actions)
                        {
                            await this.WriteVarIntAsync(actions.Count);

                            foreach (var action in actions)
                                await action.WriteAsync(this);
                        }
                        else if (value is List<int> ids)
                        {
                            foreach (var id in ids)
                                await this.WriteVarIntAsync(id);
                        }
                        else if (value is List<string> values)
                        {
                            foreach (var vals in values)
                                await this.WriteStringAsync(vals);
                        }
                        else if (value is List<long> vals)
                        {
                            foreach (var val in vals)
                                await this.WriteLongAsync(val);
                        }
                        else if (value is List<Tag> tags)
                        {
                            await this.WriteVarIntAsync(tags.Count);

                            foreach (var tag in tags)
                            {
                                await this.WriteStringAsync(tag.Name);
                                await this.WriteVarIntAsync(tag.Count);

                                foreach (var entry in tag.Entries)
                                    await this.WriteVarIntAsync(entry);
                            }
                        }
                        else if (value is List<ItemStack> items)
                        {
                            foreach (var item in items)
                                await this.WriteSlotAsync(item);
                        }
                        break;
                    }
                case DataType.ByteArray:
                    {
                        var array = (byte[])value;
                        if (attribute.CountLength)
                        {
                            await this.WriteVarIntAsync(array.Length);
                            await this.WriteAsync(array);
                        }
                        else
                            await this.WriteAsync(array);
                        break;
                    }
                case DataType.Slot:
                    {
                        await this.WriteSlotAsync((ItemStack)value);
                        break;
                    }
                case DataType.EntityMetadata:
                    {
                        var ent = (Entity)value;
                        await ent.WriteAsync(this);

                        await this.WriteUnsignedByteAsync(0xff);
                        break;
                    }
                case DataType.NbtTag:
                    {
                        if (value is MixedCodec codecs)
                        {
                            var dimensions = new NbtCompound(codecs.Dimensions.Name)
                            {
                                 new NbtString("type", codecs.Dimensions.Name)
                            };

                            var list = new NbtList("value", NbtTagType.Compound);

                            foreach (var (_, codec) in codecs.Dimensions)
                            {
                                codec.Write(list);
                            }

                            dimensions.Add(list);

                            #region biomes
                            var biomeCompound = new NbtCompound(codecs.Biomes.Name)
                            {
                                new NbtString("type", codecs.Biomes.Name)
                            };

                            var biomes = new NbtList("value", NbtTagType.Compound);

                            foreach (var (_, biome) in codecs.Biomes)
                            {
                                biome.Write(biomes);
                            }

                            biomeCompound.Add(biomes);
                            #endregion
                            var compound = new NbtCompound("")
                            {
                                dimensions,
                                biomeCompound
                            };
                            var nbt = new NbtFile(compound);

                            nbt.SaveToStream(this, NbtCompression.None);
                        }
                        else if (value is DimensionCodec codec)
                        {
                            var nbt = new NbtFile(codec.ToNbt());

                            nbt.SaveToStream(this, NbtCompression.None);
                        }
                        break;
                    }
                default:
                    throw new ArgumentOutOfRangeException(nameof(type));
            }
        }

        internal async Task WriteRecipeAsync(string name, object obj)
        {
            if (obj is IRecipe defaultRecipe)
                await this.WriteStringAsync(defaultRecipe.Type);
            else
                return;

            await this.WriteStringAsync(name);

            if (obj is ShapedRecipe shapedRecipe)
            {
                var patterns = shapedRecipe.Pattern;

                int width = patterns[0].Length, height = patterns.Count;

                await this.WriteVarIntAsync(width);
                await this.WriteVarIntAsync(height);

                await this.WriteStringAsync(shapedRecipe.Group ?? "");

                var ingredients = new List<ItemStack>[width * height];

                var y = 0;
                foreach (var pattern in patterns)
                {
                    var x = 0;
                    foreach (var c in pattern)
                    {
                        if (char.IsWhiteSpace(c))
                            continue;

                        var index = x + (y * width);

                        var key = shapedRecipe.Key[c];

                        foreach (var item in key)
                        {
                            if (ingredients[index] is null)
                                ingredients[index] = new List<ItemStack> { item };
                            else
                                ingredients[index].Add(item);
                        }

                        x++;
                    }
                    y++;
                }

                foreach (var items in ingredients)
                {
                    if (items == null)
                    {
                        await this.WriteVarIntAsync(1);
                        await this.WriteSlotAsync(ItemStack.Air);
                        continue;
                    }

                    await this.WriteVarIntAsync(items.Count);

                    foreach (var itemStack in items)
                        await this.WriteSlotAsync(itemStack);
                }

                await this.WriteSlotAsync(shapedRecipe.Result.First());
            }
            else if (obj is ShapelessRecipe shapelessRecipe)
            {
                var ingredients = shapelessRecipe.Ingredients;

                await this.WriteStringAsync(shapelessRecipe.Group ?? "");

                await this.WriteVarIntAsync(ingredients.Count);
                foreach (var ingredient in ingredients)
                {
                    await this.WriteVarIntAsync(ingredient.Count);
                    foreach (var item in ingredient)
                        await this.WriteSlotAsync(item);
                }

                var result = shapelessRecipe.Result.First();

                await this.WriteSlotAsync(result);
            }
            else if (obj is SmeltingRecipe smeltingRecipe)
            {
                await this.WriteStringAsync(smeltingRecipe.Group ?? "");


                await this.WriteVarIntAsync(smeltingRecipe.Ingredient.Count);
                foreach (var i in smeltingRecipe.Ingredient)
                    await this.WriteSlotAsync(i);

                await this.WriteSlotAsync(smeltingRecipe.Result.First());

                await this.WriteFloatAsync(smeltingRecipe.Experience);
                await this.WriteVarIntAsync(smeltingRecipe.Cookingtime);
            }
            else if (obj is CuttingRecipe cuttingRecipe)
            {
                await this.WriteStringAsync(cuttingRecipe.Group ?? "");

                await this.WriteVarIntAsync(cuttingRecipe.Ingredient.Count);
                foreach (var item in cuttingRecipe.Ingredient)
                    await this.WriteSlotAsync(item);


                var result = cuttingRecipe.Result.First();

                result.Count = (short)cuttingRecipe.Count;

                await this.WriteSlotAsync(result);
            }
            else if (obj is SmithingRecipe smithingRecipe)
            {
                await this.WriteVarIntAsync(smithingRecipe.Base.Count);
                foreach (var item in smithingRecipe.Base)
                    await this.WriteSlotAsync(item);

                await this.WriteVarIntAsync(smithingRecipe.Addition.Count);
                foreach (var item in smithingRecipe.Addition)
                    await this.WriteSlotAsync(item);

                await this.WriteSlotAsync(smithingRecipe.Result.First());
            }
        }

        public async Task WritePositionAsync(Position value)
        {
            var val = (long)((int)value.X & 0x3FFFFFF) << 38;
            val |= (long)((int)value.Z & 0x3FFFFFF) << 12;
            val |= (long)((int)value.Y & 0xFFF);

            await this.WriteLongAsync(val);
        }

        public Task WriteNbtAsync(NbtTag tag)
        {
            var writer = new NbtWriter(new MemoryStream(), "Item");

            writer.WriteTag(tag);

            writer.EndCompound();
            writer.Finish();

            return Task.CompletedTask;
        }

        public async Task WriteSlotAsync(ItemStack slot)
        {
            if (slot is null)
                slot = new ItemStack(0, 0)
                {
                    Present = true
                };

            await this.WriteBooleanAsync(slot.Present);
            if (slot.Present)
            {
                await this.WriteVarIntAsync(slot.Id);
                await this.WriteByteAsync((sbyte)slot.Count);

                var writer = new NbtWriter(this, "");

                var itemMeta = slot.ItemMeta;

                //TODO write enchants
                if (itemMeta.HasTags())
                {
                    writer.WriteByte("Unbreakable", itemMeta.Unbreakable ? 1 : 0);

                    if (itemMeta.Durability > 0)
                        writer.WriteInt("Damage", itemMeta.Durability);

                    if (itemMeta.CustomModelData > 0)
                        writer.WriteInt("CustomModelData", itemMeta.CustomModelData);

                    if (itemMeta.CanDestroy != null)
                    {
                        writer.BeginList("CanDestroy", NbtTagType.String, itemMeta.CanDestroy.Count);

                        foreach (var block in itemMeta.CanDestroy)
                            writer.WriteString(block);

                        writer.EndList();
                    }

                    if (itemMeta.Name != null)
                    {
                        writer.BeginCompound("display");

                        Console.WriteLine($"Writing Name: {JsonConvert.SerializeObject(new List<ChatMessage> { itemMeta.Name }, Formatting.Indented)}");

                        writer.WriteString("Name", JsonConvert.SerializeObject(new List<ChatMessage> { itemMeta.Name }));

                        if (itemMeta.Lore != null)
                        {
                            writer.BeginList("Lore", NbtTagType.String, itemMeta.Lore.Count);

                            foreach (var lore in itemMeta.Lore)
                                writer.WriteString(JsonConvert.SerializeObject(new List<ChatMessage> { lore }));

                            writer.EndList();
                        }

                        writer.EndCompound();
                    }
                    else if (itemMeta.Lore != null)
                    {
                        writer.BeginCompound("display");

                        writer.BeginList("Lore", NbtTagType.String, itemMeta.Lore.Count);

                        foreach (var lore in itemMeta.Lore)
                            writer.WriteString(JsonConvert.SerializeObject(new List<ChatMessage> { lore }));

                        writer.EndList();

                        writer.EndCompound();
                    }
                }

                writer.WriteString("id", slot.UnlocalizedName);
                writer.WriteByte("Count", (byte)slot.Count);

                writer.EndCompound();
                writer.Finish();
            }
        }

        public async Task<ItemStack> ReadSlotAsync()
        {
            var present = await this.ReadBooleanAsync();

            if (present)
            {
                var slot = new ItemStack((short)await this.ReadVarIntAsync(), await this.ReadByteAsync())
                {
                    Present = present
                };

                var reader = new NbtReader(this);

                while (reader.ReadToFollowing())
                {
                    var itemMetaBuilder = new ItemMetaBuilder();

                    if (reader.IsCompound)
                    {
                        var root = (NbtCompound)reader.ReadAsTag();

                        //Globals.PacketLogger.LogDebug(root.ToString());
                        foreach (var tag in root)
                        {
                            //Globals.PacketLogger.LogDebug($"Tag name: {tag.Name} | Type: {tag.TagType}");
                            if (tag.TagType == NbtTagType.Compound)
                            {
                                Globals.PacketLogger.LogDebug("Other compound");
                            }

                            switch (tag.Name.ToLower())
                            {
                                case "enchantments":
                                    {
                                        var enchantments = (NbtList)tag;

                                        foreach (var enchant in enchantments)
                                        {
                                            if (enchant is NbtCompound compound)
                                            {
                                                var id = compound.Get<NbtString>("id").Value;

                                                itemMetaBuilder.AddEnchantment(id.ToEnchantType(), compound.Get<NbtShort>("lvl").Value);
                                            }
                                        }

                                        break;
                                    }
                                case "storedenchantments":
                                    {
                                        var enchantments = (NbtList)tag;

                                        //Globals.PacketLogger.LogDebug($"List Type: {enchantments.ListType}");

                                        foreach (var enchantment in enchantments)
                                        {
                                            if (enchantment is NbtCompound compound)
                                            {

                                                var id = compound.Get<NbtString>("id").Value;

                                                itemMetaBuilder.AddStoredEnchantment(id.ToEnchantType(), compound.Get<NbtShort>("lvl").Value);
                                            }
                                        }
                                        break;
                                    }
                                case "slot":
                                    {
                                        itemMetaBuilder.WithSlot(tag.ByteValue);
                                        //Console.WriteLine($"Setting slot: {itemMetaBuilder.Slot}");
                                        break;
                                    }
                                case "damage":
                                    {

                                        itemMetaBuilder.WithDurability(tag.IntValue);
                                        //Globals.PacketLogger.LogDebug($"Setting damage: {tag.IntValue}");
                                        break;
                                    }
                                default:
                                    break;
                            }
                        }
                        //slot.ItemNbt.Slot = compound.Get<NbtByte>("Slot").Value;
                        //slot.ItemNbt.Count = compound.Get<NbtByte>("Count").Value;
                        //slot.ItemNbt.Id = compound.Get<NbtShort>("id").Value;
                        //slot.ItemNbt.Damage = compound.Get<NbtShort>("Damage").Value;
                        //slot.ItemNbt.RepairCost = compound.Get<NbtInt>("RepairCost").Value;
                    }
                    else
                    {
                        Globals.PacketLogger.LogDebug($"Other Name: {reader.TagName}");
                    }

                    slot.ItemMeta = itemMetaBuilder.Build();
                }

                return slot;
            }

            return null;
        }

        #endregion Writing

        #region Reading

        [ReadMethod(DataType.Slot)]
        public ItemStack ReadSlot()
        {
            var slot = new ItemStack();

            var present = this.ReadBoolean();
            slot.Present = present;

            if (present)
            {
                slot.Id = (short)this.ReadVarInt();
                slot.Count = this.ReadSignedByte();

                var reader = new NbtReader(this);

                while (reader.ReadToFollowing())
                {
                    var itemMetaBuilder = new ItemMetaBuilder();

                    if (reader.IsCompound)
                    {
                        var root = (NbtCompound)reader.ReadAsTag();
                        foreach (var tag in root)
                        {
                            Globals.PacketLogger.LogDebug($"Tag name: {tag.Name} | Type: {tag.TagType}");
                            switch (tag.Name.ToLower())
                            {
                                case "enchantments":
                                    {
                                        var enchantments = (NbtList)tag;

                                        foreach (var enchant in enchantments)
                                        {
                                            if (enchant is NbtCompound compound)
                                            {
                                                var id = compound.Get<NbtString>("id").Value;

                                                itemMetaBuilder.AddEnchantment(id.ToEnchantType(), compound.Get<NbtShort>("lvl").Value);
                                            }
                                        }

                                        break;
                                    }
                                case "storedenchantments":
                                    {
                                        var enchantments = (NbtList)tag;

                                        Globals.PacketLogger.LogDebug($"List Type: {enchantments.ListType}");

                                        foreach (var enchantment in enchantments)
                                        {
                                            if (enchantment is NbtCompound compound)
                                            {

                                                var id = compound.Get<NbtString>("id").Value;

                                                itemMetaBuilder.AddStoredEnchantment(id.ToEnchantType(), compound.Get<NbtShort>("lvl").Value);
                                            }
                                        }
                                        break;
                                    }
                                case "slot":
                                    {
                                        itemMetaBuilder.WithSlot(tag.ByteValue);
                                        Console.WriteLine($"Setting slot: {itemMetaBuilder.Slot}");
                                        break;
                                    }
                                case "damage":
                                    {

                                        itemMetaBuilder.WithDurability(tag.IntValue);
                                        Globals.PacketLogger.LogDebug($"Setting damage: {tag.IntValue}");
                                        break;
                                    }
                                default:
                                    break;
                            }
                        }

                        slot.ItemMeta = itemMetaBuilder.Build();
                        //slot.ItemNbt.Slot = compound.Get<NbtByte>("Slot").Value;
                        //slot.ItemNbt.Count = compound.Get<NbtByte>("Count").Value;
                        //slot.ItemNbt.Id = compound.Get<NbtShort>("id").Value;
                        //slot.ItemNbt.Damage = compound.Get<NbtShort>("Damage").Value;
                        //slot.ItemNbt.RepairCost = compound.Get<NbtInt>("RepairCost").Value;
                    }
                    else
                    {
                        Console.WriteLine($"Other Name: {reader.TagName}");
                    }
                }

            }

            return slot;
        }

        public async Task<object> ReadAsync(Type type, DataType dataType, FieldAttribute attr, int? readLen = null)
        {
            switch (dataType)
            {
                case DataType.Auto:
                    return await this.ReadAutoAsync(type, attr.Absolute, attr.CountLength);
                case DataType.Boolean:
                    return await this.ReadBooleanAsync();
                case DataType.Byte:
                    return await this.ReadByteAsync();
                case DataType.UnsignedByte:
                    return await this.ReadUnsignedByteAsync();
                case DataType.Short:
                    return await this.ReadShortAsync();
                case DataType.UnsignedShort:
                    return await this.ReadUnsignedShortAsync();
                case DataType.Int:
                    return await this.ReadIntAsync();
                case DataType.Long:
                    return await this.ReadLongAsync();
                case DataType.Float:
                    return await this.ReadFloatAsync();
                case DataType.Double:
                    return await this.ReadDoubleAsync();
                case DataType.String:
                    return await this.ReadStringAsync(attr.MaxLength);
                case DataType.Chat:
                    return await this.ReadChatAsync();
                case DataType.Identifier:
                    return await this.ReadStringAsync();
                case DataType.VarInt:
                    return await this.ReadVarIntAsync();
                case DataType.VarLong:
                    return await this.ReadVarLongAsync();
                case DataType.Position:
                    {
                        if (type == typeof(Position))
                        {
                            if (attr.Absolute)
                                return new Position(await this.ReadDoubleAsync(), await this.ReadDoubleAsync(), await this.ReadDoubleAsync());

                            return await this.ReadPositionAsync();
                        }
                        else if (type == typeof(SoundPosition))
                            return new SoundPosition(await this.ReadIntAsync(), await this.ReadIntAsync(), await this.ReadIntAsync());

                        return null;
                    }
                case DataType.Angle:
                    return this.ReadFloatAsync();
                case DataType.UUID:
                    return Guid.Parse(await this.ReadStringAsync());
                case DataType.Velocity:
                    return new Velocity(await this.ReadShortAsync(), await this.ReadShortAsync(), await this.ReadShortAsync());
                case DataType.EntityMetadata:
                case DataType.Slot:
                    {
                        return await this.ReadSlotAsync();
                    }
                case DataType.ByteArray:
                    {
                        int len = readLen.Value;
                        var arr = await this.ReadUInt8ArrayAsync(len);
                        return arr;
                    }
                case DataType.NbtTag:
                case DataType.Array:
                default:
                    throw new NotImplementedException(nameof(type));
            }
        }

        [Obsolete("Shouldn't be used anymore")]
        public async Task<object> ReadAutoAsync(Type type, bool absolute = false, bool countLength = false)
        {
            if (type == typeof(int))
            {
                if (absolute)
                    return await this.ReadIntAsync();

                return await this.ReadVarIntAsync();
            }
            else if (type == typeof(string))
            {
                return await this.ReadStringAsync(32767) ?? string.Empty;
            }
            else if (type == typeof(float))
            {
                return await this.ReadFloatAsync();
            }
            else if (type == typeof(double))
            {
                return await this.ReadDoubleAsync();
            }
            else if (type == typeof(short))
            {
                return await this.ReadShortAsync();
            }
            else if (type == typeof(ushort))
            {
                return await this.ReadUnsignedShortAsync();
            }
            else if (type == typeof(long))
            {
                return await this.ReadLongAsync();
            }
            else if (type == typeof(bool))
            {
                return await this.ReadBooleanAsync();
            }
            else if (type == typeof(byte))
            {
                return await this.ReadUnsignedByteAsync();
            }
            else if (type == typeof(sbyte))
            {
                return await this.ReadByteAsync();
            }
            else if (type == typeof(byte[]) && countLength)
            {
                var length = await this.ReadVarIntAsync();
                return await this.ReadUInt8ArrayAsync(length);
            }
            else if (type == typeof(ChatMessage))
            {
                return JsonConvert.DeserializeObject<ChatMessage>(await this.ReadStringAsync());
            }
            else if (type == typeof(Position))
            {
                if (absolute)
                {
                    return new Position(await this.ReadDoubleAsync(), await this.ReadDoubleAsync(), await this.ReadDoubleAsync());
                }

                return await this.ReadPositionAsync();
            }
            else if (type.BaseType != null && type.BaseType == typeof(Enum))
            {
                return await this.ReadVarIntAsync();
            }
            else
            {
                throw new InvalidOperationException($"Tried to read un-supported type {type}");
            }
        }

        public async Task<ChatMessage> ReadChatAsync()
        {
            var chat = await this.ReadStringAsync();

            if (chat.Length > 32767)
                throw new ArgumentException("string provided by stream exceeded maximum length", nameof(BaseStream));

            return JsonConvert.DeserializeObject<ChatMessage>(chat);
        }

        public async Task<Position> ReadPositionAsync()
        {
            ulong value = await this.ReadUnsignedLongAsync();

            long x = (long)(value >> 38);
            long y = (long)(value & 0xFFF);
            long z = (long)(value << 26 >> 38);

            if (x >= Math.Pow(2, 25))
                x -= (long)Math.Pow(2, 26);

            if (y >= Math.Pow(2, 11))
                y -= (long)Math.Pow(2, 12);

            if (z >= Math.Pow(2, 25))
                z -= (long)Math.Pow(2, 26);

            return new Position
            {
                X = x,

                Y = y,

                Z = z,
            };
        }

        #endregion Reading
    }


}