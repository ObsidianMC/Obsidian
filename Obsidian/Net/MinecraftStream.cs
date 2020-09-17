using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Obsidian.Boss;
using Obsidian.Chat;
using Obsidian.Commands;
using Obsidian.Entities;
using Obsidian.Items;
using Obsidian.Nbt;
using Obsidian.Nbt.Tags;
using Obsidian.Net.Packets.Play.Client;
using Obsidian.PlayerData.Info;
using Obsidian.Serializer.Attributes;
using Obsidian.Serializer.Enums;
using Obsidian.Util.DataTypes;
using Obsidian.Util.Extensions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit.Abstractions;

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
                    await this.WriteChatAsync((ChatMessage)value);
                    break;

                case EntityMetadataType.Slot:
                    await this.WriteUnsignedByteAsync((byte)value);
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
                    await this.WritePositionAsync((Position)value);
                    break;

                case EntityMetadataType.Direction:
                    break;

                case EntityMetadataType.OptUuid:
                    await this.WriteBooleanAsync(optional);
                    await this.WriteUuidAsync((Guid)value);
                    break;

                case EntityMetadataType.OptBlockId:
                    await this.WriteVarIntAsync((int)value);
                    break;

                case EntityMetadataType.Nbt:
                    break;

                case EntityMetadataType.Particle:
                    break;

                default:
                    break;
            }
        }

        public async Task WriteUuidAsync(Guid value)
        {
            var arr = value.ToByteArray();

            await this.WriteLongAsync(BitConverter.ToInt64(arr, 0));
            await this.WriteLongAsync(BitConverter.ToInt64(arr, 8));
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
                    await this.WriteSlotAsync((Slot)value);
                    break;
                }
                default:
                    throw new ArgumentOutOfRangeException(nameof(type));
            }
        }

        public async Task WritePositionAsync(Position value, bool pre14 = true)
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

        public async Task WriteSlotAsync(Slot slot)
        {
            await this.WriteBooleanAsync(slot.Present);
            if (slot.Present)
            {
                await this.WriteVarIntAsync(slot.Id);
                await this.WriteByteAsync(slot.Count);

                var writer = new NbtWriter(this, "");

                writer.WriteByte("Slot", slot.ItemNbt.Slot);
                writer.WriteByte("Count", (byte)slot.Count);
                writer.WriteShort("id", (short)slot.Id);
                writer.WriteInt("Damage", slot.ItemNbt.Damage);

                writer.EndCompound();

                writer.Finish();
            }
        }

        public async Task<Slot> ReadSlotAsync()
        {
            var slot = new Slot();

            var present = await this.ReadBooleanAsync();
            slot.Present = present;

            if (present)
            {
                slot.Id = await this.ReadVarIntAsync();
                slot.Count = await this.ReadByteAsync();

                /*await using var stream = new MemoryStream();

                await this.CopyToAsync(stream);
                stream.Position = 0;*/

                var reader = new NbtReader(this);

                while (reader.ReadToFollowing())
                {
                    if (!reader.HasName)
                    {
                        //TODO?????????
                        continue;
                    }

                    slot.ItemNbt = new ItemNbt();

                    if (reader.IsCompound)
                    {
                        var root = (NbtCompound)reader.ReadAsTag();

                        Program.PacketLogger.LogDebug(root.ToString());
                        foreach (var tag in root)
                        {
                            Program.PacketLogger.LogDebug($"Tag name: {tag.Name} | Type: {tag.TagType}");
                            if (tag.TagType == NbtTagType.Compound)
                            {
                                Program.PacketLogger.LogDebug("Other compound");
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
                                            slot.ItemNbt.Enchantments.Add(new Enchantment
                                            {
                                                Id = compound.Get<NbtString>("id").Value,
                                                Level = compound.Get<NbtShort>("lvl").Value
                                            });
                                        }
                                    }

                                    break;
                                }
                                case "storedenchantments":
                                {
                                    var enchantments = (NbtList)tag;

                                    Program.PacketLogger.LogDebug($"List Type: {enchantments.ListType}");

                                    foreach (var enchantment in enchantments)
                                    {
                                        if (enchantment is NbtCompound compound)
                                        {

                                            slot.ItemNbt.StoredEnchantments.Add(new Enchantment
                                            {
                                                Id = compound.Get<NbtString>("id").Value,
                                                Level = compound.Get<NbtShort>("lvl").Value
                                            });
                                        }
                                    }
                                    break;
                                }
                                case "slot":
                                {
                                    slot.ItemNbt.Slot = tag.ByteValue;
                                    break;
                                }
                                case "damage":
                                {
                                    
                                    slot.ItemNbt.Damage = tag.IntValue;
                                    Program.PacketLogger.LogDebug($"Setting damage: {tag.IntValue}");
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
                        Program.PacketLogger.LogDebug($"Other Name: {reader.TagName}");
                    }



                }

            }

            return slot;
        }

        #endregion Writing

        #region Reading

        [ReadMethod(DataType.Slot)]
        public Slot ReadSlot()
        {
            var slot = new Slot();

            var present = this.ReadBoolean();
            slot.Present = present;

            if (present)
            {
                slot.Id = this.ReadVarInt();
                slot.Count = this.ReadSignedByte();

                using var stream = new MemoryStream();

                this.CopyTo(stream);
                stream.Position = 0;

                var reader = new NbtReader(stream);

                while (reader.ReadToFollowing())
                {
                    if (!reader.HasName)
                    {
                        //TODO?????????
                        continue;
                    }

                    slot.ItemNbt = new ItemNbt();

                    if (reader.IsCompound)
                    {
                        var root = (NbtCompound)reader.ReadAsTag();
                        foreach (var tag in root)
                        {
                            Console.WriteLine($"Tag name: {tag.Name} | Type: {tag.TagType}");
                            if (tag.TagType == NbtTagType.Compound)
                            {
                                Console.WriteLine("Other compound");
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
                                            slot.ItemNbt.Enchantments.Add(new Enchantment
                                            {
                                                Id = compound.Get<NbtString>("id").Value,
                                                Level = compound.Get<NbtShort>("lvl").Value
                                            });
                                        }
                                    }

                                    break;
                                }
                                case "storedenchantments":
                                {
                                    var enchantments = (NbtList)tag;

                                    Console.WriteLine($"List Type: {enchantments.ListType}");

                                    foreach (var enchantment in enchantments)
                                    {
                                        if (enchantment is NbtCompound compound)
                                        {

                                            slot.ItemNbt.StoredEnchantments.Add(new Enchantment
                                            {
                                                Id = compound.Get<NbtString>("id").Value,
                                                Level = compound.Get<NbtShort>("lvl").Value
                                            });
                                        }
                                    }
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

    public enum EntityMetadataType : int
    {
        Byte,

        VarInt,

        Float,

        String,

        Chat,

        OptChat,

        Slot,

        Boolean,

        Rotation,

        Position,

        OptPosition,

        Direction,

        OptUuid,

        OptBlockId,

        Nbt,

        Particle
    }
}