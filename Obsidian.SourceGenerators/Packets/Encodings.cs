using System.Text.Json;

namespace Obsidian.SourceGenerators.Packets;

/// <summary>
/// Turns vanilla's encodings (the <c>encoding</c> nodes of <c>packets.json</c> and <c>packet_types.json</c>) into C#
/// types and the statements that write and read them with <c>INetStreamWriter</c>/<c>INetStreamReader</c>.
/// </summary>
/// <remarks>
/// An encoding is a JSON object with a <c>kind</c>. Primitives (<c>var_int</c>, <c>string</c>, <c>uuid</c>,
/// <c>block_pos</c>, <c>component</c>, …) map to Obsidian's reader and writer methods; <c>optional</c>,
/// <c>nullable</c>, <c>list</c> and <c>map</c> wrap other encodings; <c>enum</c> and <c>type</c> refer to the vanilla
/// enums and records in <c>packet_types.json</c>, generated into <see cref="Protocol.TypesNamespace"/>. Anything else
/// (<c>holder</c>, <c>codec</c>, <c>sequence</c>, …) isn't supported, and what uses it isn't generated.
/// </remarks>
internal sealed class Encodings
{
    private const string Types = "global::" + Protocol.TypesNamespace;

    // How many elements a read collection makes room for up front, whatever count the packet claims (vanilla caps it too).
    private const int MaxInitialCapacity = 65536;

    private readonly Protocol protocol;
    private readonly Func<ProtocolType, string?> unavailable;
    private readonly Dictionary<string, string?> typeProblems = [];
    private int temporaries;

    /// <param name="unavailable">
    /// Why a vanilla type that could be generated isn't available under its name (e.g. a hand-written type has it),
    /// or null when it is.
    /// </param>
    public Encodings(Protocol protocol, Func<ProtocolType, string?> unavailable)
    {
        this.protocol = protocol;
        this.unavailable = unavailable;
    }

    /// <summary>Why a field can't be written and read by generated code, or null when it can.</summary>
    public string? Problem(ProtocolField field)
    {
        if (field.Sent != true)
            return null;
        if (field.Encoding is not JsonElement encoding)
            return $"`{field.Name}` has no known encoding";
        if (field.Conditional)
            return $"`{field.Name}` is only written sometimes";
        if (field.Repeated)
            return $"`{field.Name}` is written in a loop";
        if (field.Packed)
            return $"`{field.Name}` is packed into a value with other fields";
        var problem = Problem(encoding);
        return problem is null ? null : $"`{field.Name}` {problem}";
    }

    /// <summary>Why a vanilla type can't be generated, or null when it can.</summary>
    public string? Problem(ProtocolType type)
    {
        if (typeProblems.TryGetValue(type.MojangName, out var known))
            return known;

        typeProblems[type.MojangName] = null; // types that contain themselves are fine
        var problem = type.IsEnum ? null : type.Fields.Select(Problem).FirstOrDefault(fieldProblem => fieldProblem is not null);
        problem ??= unavailable(type);

        typeProblems[type.MojangName] = problem;
        return problem;
    }

    private string? Problem(JsonElement encoding)
    {
        switch (Kind(encoding))
        {
            case "boolean": case "byte": case "short": case "unsigned_short": case "int": case "var_int": case "long":
            case "var_long": case "float": case "double": case "string": case "identifier": case "uuid": case "block_pos":
            case "vec3": case "lp_vec3": case "angle": case "component": case "item_stack": case "nbt": case "optional_nbt":
            case "byte_array": case "long_array": case "var_int_array": case "bit_set": case "remaining_bytes": case "instant":
            case "container_id": case "registry_id":
                return null;
            case "fixed_bit_set":
                return encoding.TryGetProperty("size", out _) ? null : "is a fixed bit set of unknown size";
            case "optional": case "nullable": case "list":
                return Problem(encoding.GetProperty("of"));
            case "map":
                return Problem(encoding.GetProperty("key")) ?? Problem(encoding.GetProperty("value"));
            case "enum":
            case "type":
                var type = ReferencedType(encoding);
                if (type is null)
                    return $"uses {Name(encoding)}, which the asset generator didn't describe";
                var problem = Problem(type);
                if (problem is not null)
                    return $"uses {type.Name}, which isn't generated: {problem}";
                return Kind(encoding) == "enum" ? Problem(encoding.GetProperty("as")) : null;
            case "codec":
                return $"uses vanilla's {encoding.GetProperty("name").GetString()}, which isn't described";
            default:
                return $"is written as `{Kind(encoding)}`, which isn't supported";
        }
    }

    /// <summary>The C# type of a field (its encoding, and its Java type for numbers written narrower or wider).</summary>
    public string CSharpType(ProtocolField field) => CSharpType(field.Encoding!.Value, field.JavaType);

    private string CSharpType(JsonElement encoding, string javaType) => Kind(encoding) switch
    {
        "boolean" => "bool",
        "byte" => javaType == "byte" ? "sbyte" : "int",
        "short" => javaType == "short" ? "short" : "int",
        "unsigned_short" or "int" or "var_int" or "container_id" or "registry_id" => "int",
        "long" or "var_long" => "long",
        "float" => "float",
        "double" => "double",
        "string" or "identifier" => "string",
        "uuid" => "global::System.Guid",
        "block_pos" => "global::Obsidian.API.Vector",
        "vec3" => "global::Obsidian.API.VectorD",
        "lp_vec3" => "global::Obsidian.API.Velocity",
        "angle" => "global::Obsidian.API.Angle",
        "component" => "global::Obsidian.API.ChatMessage",
        "item_stack" => "global::Obsidian.API.Inventory.ItemStack?",
        "nbt" => "global::Obsidian.Nbt.NbtCompound",
        "optional_nbt" => "global::Obsidian.Nbt.NbtCompound?",
        "byte_array" or "remaining_bytes" => "byte[]",
        "long_array" => "long[]",
        "var_int_array" => "int[]",
        "bit_set" or "fixed_bit_set" => "global::Obsidian.API.BitSet",
        "instant" => "global::System.DateTimeOffset",
        "optional" or "nullable" => Nullable(CSharpType(encoding.GetProperty("of"), ElementType(javaType, 0))),
        "list" => $"global::System.Collections.Generic.List<{CSharpType(encoding.GetProperty("of"), ElementType(javaType, 0))}>",
        "map" => $"global::System.Collections.Generic.Dictionary<{CSharpType(encoding.GetProperty("key"), ElementType(javaType, 0))}, " +
            $"{CSharpType(encoding.GetProperty("value"), ElementType(javaType, 1))}>",
        "enum" or "type" => $"{Types}.{ReferencedType(encoding)!.Name}",
        _ => throw new InvalidOperationException($"Unsupported encoding {Kind(encoding)}"),
    };

    /// <summary>Appends the statements that write <paramref name="value"/> of a field with <c>writer</c>.</summary>
    public void Write(CodeBuilder code, ProtocolField field, string value) =>
        Write(code, field.Encoding!.Value, field.JavaType, value);

    private void Write(CodeBuilder code, JsonElement encoding, string javaType, string value)
    {
        switch (Kind(encoding))
        {
            case "boolean": code.Line($"writer.WriteBoolean({value});"); break;
            case "byte": code.Line(javaType == "byte" ? $"writer.WriteByte({value});" : $"writer.WriteByte(unchecked((byte){value}));"); break;
            case "short": code.Line(javaType == "short" ? $"writer.WriteShort({value});" : $"writer.WriteShort(unchecked((short){value}));"); break;
            case "unsigned_short": code.Line($"writer.WriteUnsignedShort(unchecked((ushort){value}));"); break;
            case "int": code.Line($"writer.WriteInt({value});"); break;
            case "var_int": case "container_id": case "registry_id": code.Line($"writer.WriteVarInt({value});"); break;
            case "long": code.Line($"writer.WriteLong({value});"); break;
            case "var_long": code.Line($"writer.WriteVarLong({value});"); break;
            case "float": code.Line($"writer.WriteSingle({value});"); break;
            case "double": code.Line($"writer.WriteDouble({value});"); break;
            case "string": code.Line($"writer.WriteString({value}, {Max(encoding, 32767)});"); break;
            case "identifier": code.Line($"writer.WriteString({value});"); break;
            case "uuid": code.Line($"writer.WriteUuid({value});"); break;
            case "block_pos": code.Line($"writer.WritePosition({value});"); break;
            case "vec3": code.Line($"global::Obsidian.API.VectorD.Write({value}, writer);"); break;
            case "lp_vec3": code.Line($"writer.WriteVelocity({value});"); break;
            case "angle": code.Line($"writer.WriteByte({value}.Value);"); break;
            case "component": code.Line($"writer.WriteChat({value});"); break;
            case "item_stack": code.Line($"writer.WriteItemStack({value});"); break;
            case "nbt": code.Line($"writer.WriteNbtCompound({value});"); break;
            case "optional_nbt": code.Line($"writer.WriteOptionalNbtCompound({value});"); break;
            case "byte_array":
                code.Line($"writer.WriteVarInt({value}.Length);");
                code.Line($"writer.WriteByteArray({value});");
                break;
            case "remaining_bytes": code.Line($"writer.WriteByteArray({value});"); break;
            case "long_array":
                code.Line($"writer.WriteVarInt({value}.Length);");
                code.Line($"writer.WriteLongArray({value});");
                break;
            case "var_int_array":
            {
                var element = Temporary("element");
                code.Line($"writer.WriteVarInt({value}.Length);");
                code.Statement($"foreach (var {element} in {value})");
                code.Line($"writer.WriteVarInt({element});");
                code.EndScope();
                break;
            }
            case "bit_set": code.Line($"writer.WriteBitSet({value});"); break;
            case "fixed_bit_set": code.Line($"writer.WriteFixedBitSet({value}, {encoding.GetProperty("size").GetInt32()});"); break;
            case "instant": code.Line($"writer.WriteDateTimeOffset({value});"); break;
            case "enum": Write(code, encoding.GetProperty("as"), "int", $"(int){value}"); break;
            case "type": code.Line($"{Types}.{ReferencedType(encoding)!.Name}.Write({value}, writer);"); break;
            case "optional":
            case "nullable":
            {
                // Both are a boolean for whether the value is there, then the value.
                var present = Temporary("value");
                code.Statement($"if ({value} is {{ }} {present})");
                code.Line("writer.WriteBoolean(true);");
                Write(code, encoding.GetProperty("of"), ElementType(javaType, 0), present);
                code.EndScope();
                code.Statement("else");
                code.Line("writer.WriteBoolean(false);");
                code.EndScope();
                break;
            }
            case "list":
            {
                var element = Temporary("element");
                CheckCount(code, encoding, $"{value}.Count", reading: false);
                code.Line($"writer.WriteVarInt({value}.Count);");
                code.Statement($"foreach (var {element} in {value})");
                Write(code, encoding.GetProperty("of"), ElementType(javaType, 0), element);
                code.EndScope();
                break;
            }
            case "map":
            {
                var entry = Temporary("entry");
                CheckCount(code, encoding, $"{value}.Count", reading: false);
                code.Line($"writer.WriteVarInt({value}.Count);");
                code.Statement($"foreach (var {entry} in {value})");
                Write(code, encoding.GetProperty("key"), ElementType(javaType, 0), $"{entry}.Key");
                Write(code, encoding.GetProperty("value"), ElementType(javaType, 1), $"{entry}.Value");
                code.EndScope();
                break;
            }
            default:
                throw new InvalidOperationException($"Unsupported encoding {Kind(encoding)}");
        }
    }

    /// <summary>
    /// Appends the statements that read a field with <c>reader</c>, and returns the expression of its value.
    /// </summary>
    public string Read(CodeBuilder code, ProtocolField field) =>
        Read(code, field.Encoding!.Value, field.JavaType);

    private string Read(CodeBuilder code, JsonElement encoding, string javaType)
    {
        switch (Kind(encoding))
        {
            case "boolean": return "reader.ReadBoolean()";
            case "byte" when IsUnsigned(encoding): return javaType == "byte" ? "unchecked((sbyte)reader.ReadByte())" : "(int)reader.ReadByte()";
            case "byte": return javaType == "byte" ? "reader.ReadSignedByte()" : "(int)reader.ReadSignedByte()";
            case "short" when IsUnsigned(encoding): return javaType == "short" ? "unchecked((short)reader.ReadUnsignedShort())" : "(int)reader.ReadUnsignedShort()";
            case "short": return javaType == "short" ? "reader.ReadShort()" : "(int)reader.ReadShort()";
            case "unsigned_short": return "(int)reader.ReadUnsignedShort()";
            case "int": return "reader.ReadInt()";
            case "var_int": case "container_id": case "registry_id": return "reader.ReadVarInt()";
            case "long": return "reader.ReadLong()";
            case "var_long": return "reader.ReadVarLong()";
            case "float": return "reader.ReadSingle()";
            case "double": return "reader.ReadDouble()";
            case "string": return $"reader.ReadString({Max(encoding, 32767)})";
            case "identifier": return "reader.ReadString()";
            case "uuid": return "reader.ReadGuid()";
            case "block_pos": return "reader.ReadPosition()";
            case "vec3": return "global::Obsidian.API.VectorD.Read(reader)";
            case "lp_vec3": return "reader.ReadVelocity()";
            case "angle": return "reader.ReadAngle()";
            case "component": return "reader.ReadChat()";
            case "item_stack": return "reader.ReadItemStack()";
            case "nbt": return "reader.ReadNbtCompound()";
            case "optional_nbt": return "reader.ReadOptionalNbtCompound()";
            case "byte_array": return $"reader.ReadByteArray({Max(encoding, int.MaxValue)})";
            case "remaining_bytes": return $"reader.ReadRemainingBytes({Max(encoding, int.MaxValue)})";
            case "long_array": return "reader.ReadLongArray()";
            case "var_int_array": return "reader.ReadVarIntArray()";
            case "bit_set": return "reader.ReadBitSet()";
            case "fixed_bit_set": return $"reader.ReadFixedBitSet({encoding.GetProperty("size").GetInt32()})";
            case "instant": return "reader.ReadDateTimeOffset()";
            case "enum": return $"({Types}.{ReferencedType(encoding)!.Name}){Read(code, encoding.GetProperty("as"), "int")}";
            case "type": return $"{Types}.{ReferencedType(encoding)!.Name}.Read(reader)";
            case "optional":
            case "nullable":
            {
                var result = Temporary("value");
                code.Line($"{CSharpType(encoding, javaType)} {result} = null;");
                code.Statement("if (reader.ReadBoolean())");
                var inner = Read(code, encoding.GetProperty("of"), ElementType(javaType, 0));
                code.Line($"{result} = {inner};");
                code.EndScope();
                return result;
            }
            case "list":
            {
                var result = Temporary("list");
                var count = Temporary("count");
                var index = Temporary("index");
                code.Line($"var {count} = reader.ReadVarInt();");
                CheckCount(code, encoding, count, reading: true);
                code.Line($"var {result} = new {CSharpType(encoding, javaType)}(global::System.Math.Min({count}, {MaxInitialCapacity}));");
                code.Statement($"for (var {index} = 0; {index} < {count}; {index}++)");
                var element = Read(code, encoding.GetProperty("of"), ElementType(javaType, 0));
                code.Line($"{result}.Add({element});");
                code.EndScope();
                return result;
            }
            case "map":
            {
                var result = Temporary("map");
                var count = Temporary("count");
                var index = Temporary("index");
                var key = Temporary("key");
                code.Line($"var {count} = reader.ReadVarInt();");
                CheckCount(code, encoding, count, reading: true);
                code.Line($"var {result} = new {CSharpType(encoding, javaType)}(global::System.Math.Min({count}, {MaxInitialCapacity}));");
                code.Statement($"for (var {index} = 0; {index} < {count}; {index}++)");
                code.Line($"var {key} = {Read(code, encoding.GetProperty("key"), ElementType(javaType, 0))};");
                var item = Read(code, encoding.GetProperty("value"), ElementType(javaType, 1));
                code.Line($"{result}[{key}] = {item};");
                code.EndScope();
                return result;
            }
            default:
                throw new InvalidOperationException($"Unsupported encoding {Kind(encoding)}");
        }
    }

    private ProtocolType? ReferencedType(JsonElement encoding)
    {
        var name = Name(encoding);
        return name is not null && protocol.Types.TryGetValue(name, out var type) ? type : null;
    }

    private static string? Name(JsonElement encoding) =>
        encoding.TryGetProperty("type", out var type) ? type.GetString()
        : encoding.TryGetProperty("name", out var name) ? name.GetString()
        : null;

    private static string Kind(JsonElement encoding) => encoding.GetProperty("kind").GetString()!;

    /// <summary>
    /// Throws when a collection's count is negative or over the encoding's maximum (vanilla rejects both when reading,
    /// and won't write more than the maximum).
    /// </summary>
    private static void CheckCount(CodeBuilder code, JsonElement encoding, string count, bool reading)
    {
        var max = Max(encoding, int.MaxValue);
        if (!reading && max == int.MaxValue)
            return;

        var exception = reading ? "global::System.IO.InvalidDataException" : "global::System.InvalidOperationException";
        code.Statement(max == int.MaxValue ? $"if ({count} < 0)" : $"if ({count} < 0 || {count} > {max})");
        code.Line($"throw new {exception}($\"Collection count {{{count}}} is outside 0 to {max}.\");");
        code.EndScope();
    }

    private static bool IsUnsigned(JsonElement encoding) =>
        encoding.TryGetProperty("unsigned", out var unsigned) && unsigned.GetBoolean();

    private static int Max(JsonElement encoding, int fallback) =>
        encoding.TryGetProperty("max", out var max) ? max.GetInt32() : fallback;

    private string Temporary(string name) => $"{name}{temporaries++}";

    private static string Nullable(string type) => type.EndsWith("?") ? type : type + "?";

    /// <summary>The <paramref name="index"/>th type argument of a Java type (<c>List&lt;Integer&gt;</c> → <c>Integer</c>).</summary>
    private static string ElementType(string javaType, int index)
    {
        var start = javaType.IndexOf('<');
        if (start < 0 || !javaType.EndsWith(">"))
            return "?";

        var arguments = new List<string>();
        var depth = 0;
        var from = start + 1;
        for (var i = from; i < javaType.Length - 1; i++)
        {
            switch (javaType[i])
            {
                case '<': depth++; break;
                case '>': depth--; break;
                case ',' when depth == 0:
                    arguments.Add(javaType.Substring(from, i - from).Trim());
                    from = i + 1;
                    break;
            }
        }
        arguments.Add(javaType.Substring(from, javaType.Length - 1 - from).Trim());
        return index < arguments.Count ? arguments[index] : "?";
    }
}
