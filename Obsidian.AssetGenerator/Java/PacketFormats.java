import java.io.IOException;
import java.io.UncheckedIOException;
import java.lang.classfile.ClassFile;
import java.lang.classfile.ClassModel;
import java.lang.classfile.CodeElement;
import java.lang.classfile.Instruction;
import java.lang.classfile.Label;
import java.lang.classfile.MethodModel;
import java.lang.classfile.Opcode;
import java.lang.classfile.TypeKind;
import java.lang.classfile.instruction.ArrayLoadInstruction;
import java.lang.classfile.instruction.ArrayStoreInstruction;
import java.lang.classfile.instruction.BranchInstruction;
import java.lang.classfile.instruction.ConstantInstruction;
import java.lang.classfile.instruction.ConvertInstruction;
import java.lang.classfile.instruction.FieldInstruction;
import java.lang.classfile.instruction.InvokeDynamicInstruction;
import java.lang.classfile.instruction.InvokeInstruction;
import java.lang.classfile.instruction.LabelTarget;
import java.lang.classfile.instruction.LoadInstruction;
import java.lang.classfile.instruction.LookupSwitchInstruction;
import java.lang.classfile.instruction.MonitorInstruction;
import java.lang.classfile.instruction.NewMultiArrayInstruction;
import java.lang.classfile.instruction.NewObjectInstruction;
import java.lang.classfile.instruction.NewPrimitiveArrayInstruction;
import java.lang.classfile.instruction.NewReferenceArrayInstruction;
import java.lang.classfile.instruction.OperatorInstruction;
import java.lang.classfile.instruction.ReturnInstruction;
import java.lang.classfile.instruction.StackInstruction;
import java.lang.classfile.instruction.StoreInstruction;
import java.lang.classfile.instruction.SwitchCase;
import java.lang.classfile.instruction.TableSwitchInstruction;
import java.lang.classfile.instruction.ThrowInstruction;
import java.lang.classfile.instruction.TypeCheckInstruction;
import java.lang.constant.ClassDesc;
import java.lang.constant.ConstantDesc;
import java.lang.constant.DirectMethodHandleDesc;
import java.lang.constant.MethodTypeDesc;
import java.util.ArrayDeque;
import java.util.ArrayList;
import java.util.HashMap;
import java.util.HashSet;
import java.util.LinkedHashMap;
import java.util.LinkedHashSet;
import java.util.List;
import java.util.Map;
import java.util.Set;
import java.util.TreeMap;
import java.util.stream.Collectors;

/**
 * Works out how vanilla writes a packet: which of its class's fields it sends, in which order and how each is encoded.
 *
 * <p>It reads the bytecode of the packet class's static initializer to find the expression that builds its
 * {@code STREAM_CODEC}, then reads what that expression writes: for {@code StreamCodec.composite} the codec paired with
 * each getter, for {@code Packet.codec} and the like the packet's own {@code write} method. Methods are interpreted
 * symbolically: values on the operand stack remember which of the packet's fields they come from, and every call that
 * gets the buffer records the fields it was given and how they're encoded (the buffer method, such as
 * {@code writeVarInt}, or the codec, such as {@code ByteBufCodecs.VAR_INT}). Calls into the packet's own classes (a
 * {@code super.write} or a helper) are followed; other classes' methods are named instead.
 *
 * <p>Branches are not followed separately: the method is walked once, top to bottom. A write inside an {@code if} or
 * after an early return is marked conditional, one inside a loop repeated. A value chosen by a condition on a field
 * without calling anything ({@code flag ? 1 : 0}, {@code if (flag) bits |= 2}) counts as sending that field, so flag
 * bytes report the fields packed into them. Values that don't come from a field (constants, counts of something else)
 * aren't reported.
 */
final class PacketFormats {
    /**
     * How a field is sent: its position among its class's writes, its encoding (a JSON node with a {@code kind}, see
     * {@link #describe}), and whether it's only sometimes written, written in a loop, or written together with other
     * fields in one value (a flag byte).
     */
    record Write(int order, Object encoding, boolean conditional, boolean repeated, boolean packed) {}

    /**
     * A vanilla type that packets write: a record's fields by Mojang name (as {@link #writes} gives a packet's), or an
     * enum's wire value per constant.
     */
    record TypeDefinition(Class<?> type, Map<String, Write> fields, Map<String, Integer> values) {}

    // Buffer write methods (by name) and codecs (by Owner.FIELD) that write the same thing, by encoding kind.
    private static final Map<String, String> KINDS = Map.ofEntries(
        Map.entry("ByteBufCodecs.BOOL", "boolean"), Map.entry("writeBoolean", "boolean"),
        Map.entry("ByteBufCodecs.BYTE", "byte"), Map.entry("writeByte", "byte"),
        Map.entry("ByteBufCodecs.SHORT", "short"), Map.entry("writeShort", "short"),
        Map.entry("ByteBufCodecs.UNSIGNED_SHORT", "unsigned_short"),
        Map.entry("ByteBufCodecs.INT", "int"), Map.entry("writeInt", "int"),
        Map.entry("ByteBufCodecs.VAR_INT", "var_int"), Map.entry("writeVarInt", "var_int"), Map.entry("VarInt.write", "var_int"),
        Map.entry("ByteBufCodecs.LONG", "long"), Map.entry("writeLong", "long"),
        Map.entry("ByteBufCodecs.VAR_LONG", "var_long"), Map.entry("writeVarLong", "var_long"), Map.entry("VarLong.write", "var_long"),
        Map.entry("ByteBufCodecs.FLOAT", "float"), Map.entry("writeFloat", "float"),
        Map.entry("ByteBufCodecs.DOUBLE", "double"), Map.entry("writeDouble", "double"),
        Map.entry("ByteBufCodecs.BYTE_ARRAY", "byte_array"), Map.entry("writeByteArray", "byte_array"),
        Map.entry("ByteBufCodecs.LONG_ARRAY", "long_array"), Map.entry("writeLongArray", "long_array"),
        Map.entry("writeVarIntArray", "var_int_array"),
        Map.entry("UUIDUtil.STREAM_CODEC", "uuid"), Map.entry("writeUUID", "uuid"),
        Map.entry("Identifier.STREAM_CODEC", "identifier"), Map.entry("writeIdentifier", "identifier"),
        Map.entry("writeResourceKey", "identifier"),
        Map.entry("BlockPos.STREAM_CODEC", "block_pos"), Map.entry("writeBlockPos", "block_pos"),
        Map.entry("Vec3.STREAM_CODEC", "vec3"), Map.entry("writeVec3", "vec3"),
        Map.entry("Vec3.LP_STREAM_CODEC", "lp_vec3"), Map.entry("LpVec3.write", "lp_vec3"), Map.entry("writeLpVec3", "lp_vec3"),
        Map.entry("ByteBufCodecs.CONTAINER_ID", "container_id"), Map.entry("writeContainerId", "container_id"),
        Map.entry("ByteBufCodecs.ROTATION_BYTE", "angle"),
        Map.entry("ComponentSerialization.TRUSTED_STREAM_CODEC", "component"),
        Map.entry("ComponentSerialization.TRUSTED_CONTEXT_FREE_STREAM_CODEC", "component"),
        Map.entry("ComponentSerialization.STREAM_CODEC", "component"),
        Map.entry("ItemStack.OPTIONAL_STREAM_CODEC", "item_stack"), Map.entry("ItemStack.STREAM_CODEC", "required_item_stack"),
        Map.entry("ItemStack.OPTIONAL_UNTRUSTED_STREAM_CODEC", "untrusted_item_stack"),
        Map.entry("ByteBufCodecs.COMPOUND_TAG", "nbt"), Map.entry("ByteBufCodecs.TRUSTED_COMPOUND_TAG", "nbt"),
        Map.entry("ByteBufCodecs.OPTIONAL_COMPOUND_TAG", "optional_nbt"), Map.entry("writeNbt", "optional_nbt"),
        Map.entry("ByteBufCodecs.TAG", "tag"), Map.entry("writeBitSet", "bit_set"),
        Map.entry("ChunkPos.STREAM_CODEC", "chunk_pos"), Map.entry("writeChunkPos", "chunk_pos"));

    // FriendlyByteBuf.writeUtf's and ByteBufCodecs.STRING_UTF8's maximum length.
    private static final int MAX_STRING = 32767;

    // The marker field of a collection's element, for describing what a writer lambda writes per element.
    private static final String ELEMENT = "\0element";
    private static final int MAX_DEPTH = 8;

    private final VanillaDumper.Mojang mojang;
    private final Class<?> byteBuf;
    private final Class<?> streamEncoder;
    private final Class<?> codec;
    private final Class<?> codecOperation;
    private final Map<String, ClassModel> models = new HashMap<>();
    private final Map<Class<?>, Map<String, Node>> statics = new HashMap<>();
    private final Map<String, TypeDefinition> types = new TreeMap<>();

    PacketFormats(VanillaDumper.Mojang mojang) {
        this.mojang = mojang;
        byteBuf = mojang.type("io.netty.buffer.ByteBuf");
        streamEncoder = mojang.type("net.minecraft.network.codec.StreamEncoder");
        codec = mojang.type("com.mojang.serialization.Codec");
        codecOperation = mojang.type("net.minecraft.network.codec.StreamCodec$CodecOperation");
    }

    /**
     * How a packet class's fields are written, by Mojang field name; fields it doesn't send are missing. Null when the
     * packet's codec isn't one this understands.
     */
    Map<String, Write> writes(Class<?> packet) {
        var own = staticsOf(packet);
        var codecs = own.keySet().stream().filter(name -> name.endsWith("STREAM_CODEC")).sorted().toList();
        if (codecs.isEmpty())
            return null;
        return recordWrites(packet, own.get(codecs.contains("STREAM_CODEC") ? "STREAM_CODEC" : codecs.getFirst()));
    }

    /** The vanilla records and enums the packets read so far write, by Mojang name ({@code Outer.Inner}). */
    Map<String, TypeDefinition> types() {
        return types;
    }

    /** How a codec writes the fields of the class it encodes, or null when it isn't one this understands. */
    private Map<String, Write> recordWrites(Class<?> type, Node codec) {
        var analysis = new Analysis(type);
        analysis.statics.putAll(staticsOf(type));
        var events = new ArrayList<Event>();
        if (!readCodec(analysis, codec, events))
            return null;

        // Codecs with a hand-written decoder (Packet.codec(X::write, X::new)) can read differently from how they write.
        var writes = aggregate(events);
        if (codec.kind() == Kind.CALL && codec.arguments().size() > 1 && codec.arguments().get(1).kind() == Kind.LAMBDA
            && Set.of("Packet.codec", "StreamCodec.ofMember", "StreamCodec.of").contains(codec.name()))
            applyReadHints(writes, decoderHints(type, codec.arguments().get(1)));
        return writes;
    }

    /**
     * How a decoder reads a class's fields where that differs from how they're written: a string's maximum length
     * ({@code readUtf(16)}) and numbers read unsigned ({@code readUnsignedByte}), by field.
     */
    private Map<String, Map<String, Object>> decoderHints(Class<?> type, Node decoder) {
        var analysis = new Analysis(type);
        analysis.statics.putAll(staticsOf(type));
        var handle = decoder.handle();
        if (handle.kind() == DirectMethodHandleDesc.Kind.CONSTRUCTOR) {
            var owner = classOf(handle.owner());
            var constructor = owner == null ? null : method(owner, "<init>", handle.lookupDescriptor());
            if (constructor != null)
                readConstructor(analysis, owner, constructor);
        } else {
            runLambda(analysis, decoder, new ArrayList<>());
        }
        return analysis.readHints;
    }

    /** The read hints of a class's constructor that reads it from a buffer ({@code X(FriendlyByteBuf)}), if it has one. */
    private Map<String, Map<String, Object>> constructorHints(Class<?> type) {
        var analysis = new Analysis(type);
        analysis.statics.putAll(staticsOf(type));
        var model = model(type);
        for (var method : model == null ? List.<MethodModel>of() : model.methods()) {
            var parameters = MethodTypeDesc.ofDescriptor(method.methodType().stringValue()).parameterList();
            var parameter = parameters.size() == 1 ? classOf(parameters.getFirst()) : null;
            if (method.methodName().equalsString("<init>") && parameter != null && byteBuf.isAssignableFrom(parameter))
                readConstructor(analysis, type, method);
        }
        return analysis.readHints;
    }

    private void readConstructor(Analysis analysis, Class<?> owner, MethodModel constructor) {
        var arguments = new ArrayList<Node>(List.of(Node.of(Kind.PACKET, owner)));
        for (var parameter : MethodTypeDesc.ofDescriptor(constructor.methodType().stringValue()).parameterList()) {
            var parameterType = classOf(parameter);
            arguments.add(parameterType != null && byteBuf.isAssignableFrom(parameterType) ? Node.of(Kind.BUFFER, parameterType) : Node.value(Set.of(), parameterType));
        }
        interpret(analysis, owner, constructor, arguments, Flow.ALWAYS, new ArrayList<>());
    }

    /** Narrows written encodings by how they're read: a string's lower maximum, a number read unsigned. */
    @SuppressWarnings("unchecked")
    private static void applyReadHints(Map<String, Write> writes, Map<String, Map<String, Object>> hints) {
        for (var hint : hints.entrySet()) {
            var write = writes.get(hint.getKey());
            if (write == null || !(write.encoding() instanceof Map<?, ?> encoding))
                continue;

            var narrowed = new LinkedHashMap<>((Map<String, Object>) encoding);
            var kind = narrowed.get("kind");
            if ("string".equals(kind) && hint.getValue().get("max") instanceof Integer max
                && (!(narrowed.get("max") instanceof Integer written) || max < written))
                narrowed.put("max", max);
            if (("byte".equals(kind) || "short".equals(kind)) && Boolean.TRUE.equals(hint.getValue().get("unsigned")))
                narrowed.put("unsigned", true);
            writes.put(hint.getKey(), new Write(write.order(), narrowed, write.conditional(), write.repeated(), write.packed()));
        }
    }

    /**
     * How a class's {@code write} method writes its fields: an instance {@code write(buf)}, or a static
     * {@code write(buf, value)} given a value of the class.
     */
    private Map<String, Write> methodWrites(Class<?> type, Target write) {
        var analysis = new Analysis(type);
        analysis.statics.putAll(staticsOf(type));
        var arguments = new ArrayList<Node>();
        if (!write.method().flags().has(java.lang.reflect.AccessFlag.STATIC))
            arguments.add(Node.of(Kind.PACKET, type));
        for (var parameter : MethodTypeDesc.ofDescriptor(write.method().methodType().stringValue()).parameterList()) {
            var parameterType = classOf(parameter);
            if (parameterType != null && byteBuf.isAssignableFrom(parameterType))
                arguments.add(Node.of(Kind.BUFFER, parameterType));
            else if (parameterType != null && parameterType != Object.class && parameterType.isAssignableFrom(type))
                arguments.add(Node.of(Kind.PACKET, parameterType));
            else
                arguments.add(Node.value(Set.of(), parameterType));
        }
        var events = new ArrayList<Event>();
        interpret(analysis, write.owner(), write.method(), arguments, Flow.ALWAYS, events);
        var writes = aggregate(events);
        applyReadHints(writes, constructorHints(type));
        return writes;
    }

    /** Groups the writes by field: each field's first position, its encodings in order, and when it's written. */
    private static Map<String, Write> aggregate(List<Event> events) {
        var writes = new LinkedHashMap<String, List<Event>>();
        for (var event : events) {
            for (var field : event.fields())
                writes.computeIfAbsent(field, key -> new ArrayList<>()).add(event);
        }

        var result = new HashMap<String, Write>();
        for (var entry : writes.entrySet()) {
            var encodings = new ArrayList<Object>();
            for (var event : entry.getValue())
                encodings.add(event.encoding());
            var fieldEvents = entry.getValue();
            result.put(entry.getKey(), new Write(events.indexOf(fieldEvents.getFirst()),
                encodings.size() == 1 ? encodings.getFirst() : node("sequence", "of", encodings),
                fieldEvents.stream().allMatch(event -> event.flow().conditional()),
                fieldEvents.stream().allMatch(event -> event.flow().repeated()),
                fieldEvents.stream().anyMatch(event -> event.fields().size() > 1)));
        }

        return result;
    }

    /** The static fields a class's initializer sets, as the expressions that build them, by Mojang name. */
    private Map<String, Node> staticsOf(Class<?> type) {
        var known = statics.get(type);
        if (known != null)
            return known;

        var analysis = new Analysis(type);
        statics.put(type, analysis.statics); // before interpreting, for initializers that read their own codecs
        var initializer = method(type, "<clinit>", "()V");
        if (initializer != null)
            interpret(analysis, type, initializer, List.of(), Flow.ALWAYS, new ArrayList<>());
        return analysis.statics;
    }

    /** Records what a codec expression writes of the class it encodes, or returns false for codecs this doesn't understand. */
    private boolean readCodec(Analysis analysis, Node codec, List<Event> events) {
        // A codec that's another static codec (STREAM_CODEC = DETAILS_STREAM_CODEC) is that codec.
        while (codec != null && codec.kind() == Kind.STATIC && codec.owner() != null)
            codec = staticsOf(codec.owner()).get(codec.name().substring(codec.name().lastIndexOf('.') + 1));
        if (codec == null || codec.kind() != Kind.CALL)
            return false;

        var arguments = codec.arguments();
        switch (codec.name()) {
            case "StreamCodec.composite" -> {
                // composite(codec1, getter1, ..., codecN, getterN, constructor)
                for (var i = 0; i + 1 < arguments.size(); i += 2)
                    events.add(new Event(getterFields(analysis, arguments.get(i + 1)), describe(arguments.get(i)), Flow.ALWAYS));
                return true;
            }
            case "StreamCodec.unit" -> {
                return true;
            }
            case "Packet.codec", "StreamCodec.ofMember", "StreamCodec.of" -> {
                return arguments.getFirst().kind() == Kind.LAMBDA && runLambda(analysis, arguments.getFirst(), events) != null;
            }
            case "StreamCodec.map" -> {
                // codec.map(constructor, getter)
                events.add(new Event(getterFields(analysis, arguments.get(1)), describe(codec.receiver()), Flow.ALWAYS));
                return true;
            }
            case "StreamCodec.cast" -> {
                return readCodec(analysis, codec.receiver(), events);
            }
            default -> {
                return false;
            }
        }
    }

    /** The packet fields a getter lambda (Packet::field, p -> p.field) reads. */
    private Set<String> getterFields(Analysis analysis, Node getter) {
        if (getter.kind() != Kind.LAMBDA)
            return Set.of();
        var returned = runLambda(analysis, getter, new ArrayList<>());
        return returned == null ? Set.of() : returned.fields();
    }

    /**
     * Interprets a lambda's implementation, with its captured values followed by the buffer, the packet or an element
     * for its parameters (by type). Returns what it returns, or null when its code can't be read.
     */
    private Node runLambda(Analysis analysis, Node lambda, List<Event> events) {
        var handle = lambda.handle();
        var owner = classOf(handle.owner());
        if (owner == null)
            return null;
        var type = handle.invocationType();
        var method = method(owner, handle.methodName(), handle.lookupDescriptor());
        if (method == null || method.code().isEmpty())
            return null;

        var arguments = new ArrayList<Node>(lambda.arguments());
        for (var i = arguments.size(); i < type.parameterCount(); i++) {
            var parameter = classOf(type.parameterType(i));
            if (parameter != null && byteBuf.isAssignableFrom(parameter))
                arguments.add(Node.of(Kind.BUFFER, parameter));
            else if (parameter != null && parameter != Object.class && parameter.isAssignableFrom(analysis.packet))
                arguments.add(Node.of(Kind.PACKET, parameter));
            else
                arguments.add(new Node(Kind.VALUE, "element", null, List.of(), Set.of(ELEMENT), parameter, null, null));
        }

        return interpret(analysis, owner, method, arguments, Flow.ALWAYS, events);
    }

    // The interpreter.

    /** One packet's analysis: its class and the static fields its initializer sets. */
    private static final class Analysis {
        final Class<?> packet;
        final Map<String, Node> statics = new HashMap<>();
        final Set<String> running = new HashSet<>();
        // How decoders read the class's fields, by field (see decoderHints).
        final Map<String, Map<String, Object>> readHints = new HashMap<>();

        Analysis(Class<?> packet) {
            this.packet = packet;
        }
    }

    private enum Kind { PACKET, BUFFER, STATIC, CALL, LAMBDA, CONSTANT, VALUE }

    /**
     * A symbolic value: the packet, the buffer, a static field, a call building a codec, a lambda, a constant or any
     * other value; each with the packet fields it's computed from.
     */
    private record Node(Kind kind, String name, Node receiver, List<Node> arguments, Set<String> fields, Class<?> type,
                        DirectMethodHandleDesc handle, Class<?> owner) {
        static Node of(Kind kind, Class<?> type) {
            return new Node(kind, null, null, List.of(), Set.of(), type, null, null);
        }

        static Node value(Set<String> fields, Class<?> type) {
            return new Node(Kind.VALUE, null, null, List.of(), fields, type, null, null);
        }

        boolean wide() {
            return type == long.class || type == double.class;
        }
    }

    /** A call that got the buffer: the packet fields it was given, how it encodes them, and when it runs. */
    private record Event(Set<String> fields, Object encoding, Flow flow) {}

    /** Whether code only runs sometimes (in an if, after an early return) and whether it runs in a loop. */
    private record Flow(boolean conditional, boolean repeated) {
        static final Flow ALWAYS = new Flow(false, false);

        Flow and(boolean conditional, boolean repeated) {
            return new Flow(this.conditional || conditional, this.repeated || repeated);
        }
    }

    /**
     * Interprets a method from top to bottom with the given arguments (the receiver first), adding the writes it makes
     * to {@code events}, and returns what it returns.
     */
    private Node interpret(Analysis analysis, Class<?> owner, MethodModel method, List<Node> arguments, Flow flow, List<Event> events) {
        var code = method.code().orElse(null);
        if (code == null)
            return Node.value(Set.of(), null);

        var elements = code.elementList();
        var labels = new HashMap<Label, Integer>();
        for (var i = 0; i < elements.size(); i++) {
            if (elements.get(i) instanceof LabelTarget target)
                labels.put(target.label(), i);
        }
        var branches = new Branches(elements, labels);

        var locals = new HashMap<Integer, Node>();
        var slot = 0;
        for (var argument : arguments) {
            locals.put(slot, argument);
            slot += argument.wide() ? 2 : 1;
        }

        var stack = new ArrayList<Node>();
        var saved = new HashMap<Label, List<Node>>();
        var returned = new ArrayList<Node>();
        var reachable = true;
        for (var i = 0; i < elements.size(); i++) {
            var element = elements.get(i);
            if (element instanceof LabelTarget target) {
                // Code after a jump continues with the stack of the branch that jumps here.
                if (!reachable) {
                    var at = saved.get(target.label());
                    stack = at == null ? new ArrayList<>() : new ArrayList<>(at);
                    reachable = true;
                }
                continue;
            }
            if (!reachable || !(element instanceof Instruction instruction))
                continue;

            var at = flow.and(branches.conditional[i], branches.repeated[i]);
            switch (instruction) {
                case LoadInstruction load -> stack.add(locals.getOrDefault(load.slot(), Node.value(Set.of(), kindClass(load.typeKind()))));
                case StoreInstruction store -> locals.put(store.slot(), pop(stack));
                case ConstantInstruction constant -> stack.add(new Node(Kind.CONSTANT, String.valueOf(constant.constantValue()), null,
                    List.of(), branches.conditionFields(i), kindClass(constant.typeKind()), null, null));
                case FieldInstruction field -> field(analysis, field, stack);
                case InvokeInstruction invoke -> invoke(analysis, invoke, stack, at, events);
                case InvokeDynamicInstruction dynamic -> stack.add(dynamic(dynamic, popArguments(stack, dynamic.typeSymbol().parameterCount())));
                case StackInstruction shuffle -> shuffle(shuffle.opcode(), stack);
                case OperatorInstruction operator -> {
                    var unary = switch (operator.opcode()) {
                        case INEG, LNEG, FNEG, DNEG, ARRAYLENGTH -> true;
                        default -> false;
                    };
                    var operands = unary ? List.of(pop(stack)) : popArguments(stack, 2);
                    var result = switch (operator.opcode()) {
                        case LCMP, FCMPL, FCMPG, DCMPL, DCMPG, ARRAYLENGTH -> int.class;
                        default -> kindClass(operator.typeKind());
                    };
                    stack.add(derive(operands, result));
                }
                case ConvertInstruction convert -> stack.add(derive(List.of(pop(stack)), kindClass(convert.toType())));
                case TypeCheckInstruction check -> {
                    if (check.opcode() == Opcode.INSTANCEOF)
                        stack.add(derive(List.of(pop(stack)), int.class));
                }
                case NewObjectInstruction created -> stack.add(Node.value(Set.of(), classOf(created.className().asSymbol())));
                case NewPrimitiveArrayInstruction ignored -> {
                    pop(stack);
                    stack.add(Node.value(Set.of(), null));
                }
                case NewReferenceArrayInstruction ignored -> {
                    pop(stack);
                    stack.add(Node.value(Set.of(), null));
                }
                case NewMultiArrayInstruction array -> {
                    popArguments(stack, array.dimensions());
                    stack.add(Node.value(Set.of(), null));
                }
                case ArrayLoadInstruction load -> stack.add(derive(popArguments(stack, 2), kindClass(load.typeKind())));
                case ArrayStoreInstruction ignored -> popArguments(stack, 3);
                case BranchInstruction branch -> {
                    var name = branch.opcode().name();
                    var operands = name.startsWith("IF_") ? popArguments(stack, 2) : name.startsWith("IF") ? List.of(pop(stack)) : List.<Node>of();
                    branches.reached(i, operands);
                    saved.putIfAbsent(branch.target(), new ArrayList<>(stack));
                    if (branch.opcode() == Opcode.GOTO || branch.opcode() == Opcode.GOTO_W)
                        reachable = false;
                }
                case TableSwitchInstruction table -> {
                    branches.reached(i, List.of(pop(stack)));
                    saved.putIfAbsent(table.defaultTarget(), new ArrayList<>(stack));
                    for (var target : table.cases())
                        saved.putIfAbsent(target.target(), new ArrayList<>(stack));
                    reachable = false;
                }
                case LookupSwitchInstruction lookup -> {
                    branches.reached(i, List.of(pop(stack)));
                    saved.putIfAbsent(lookup.defaultTarget(), new ArrayList<>(stack));
                    for (var target : lookup.cases())
                        saved.putIfAbsent(target.target(), new ArrayList<>(stack));
                    reachable = false;
                }
                case ReturnInstruction result -> {
                    if (result.typeKind() != TypeKind.VOID)
                        returned.add(pop(stack));
                    reachable = false;
                }
                case ThrowInstruction ignored -> {
                    pop(stack);
                    reachable = false;
                }
                case MonitorInstruction ignored -> pop(stack);
                default -> { } // nop, iinc
            }
        }

        if (returned.size() == 1)
            return returned.getFirst();
        return derive(returned, returned.isEmpty() ? null : returned.getFirst().type());
    }

    private void field(Analysis analysis, FieldInstruction field, List<Node> stack) {
        var owner = classOf(field.owner().asSymbol());
        var type = classOf(field.typeSymbol());
        var name = owner == null ? field.name().stringValue() : fieldName(owner, field.name().stringValue());
        switch (field.opcode()) {
            case GETSTATIC -> {
                var ownerName = owner == null ? field.owner().asInternalName() : mojang.nestedName(owner);
                stack.add(new Node(Kind.STATIC, ownerName + "." + name, null, List.of(), Set.of(), type, null, owner));
            }
            case PUTSTATIC -> {
                var value = pop(stack);
                if (owner == analysis.packet)
                    analysis.statics.put(name, value);
            }
            case GETFIELD -> {
                var receiver = pop(stack);
                stack.add(receiver.kind() == Kind.PACKET ? Node.value(Set.of(name), type) : derive(List.of(receiver), type));
            }
            case PUTFIELD -> {
                var value = pop(stack);
                var receiver = pop(stack);
                if (receiver.kind() == Kind.PACKET && value.name() != null && value.name().startsWith(READ_HINT))
                    analysis.readHints.computeIfAbsent(name, key -> new HashMap<>()).putAll(readHint(value.name()));
            }
            default -> { }
        }
    }

    private void invoke(Analysis analysis, InvokeInstruction invoke, List<Node> stack, Flow flow, List<Event> events) {
        var type = invoke.typeSymbol();
        var arguments = popArguments(stack, type.parameterCount());
        var isStatic = invoke.opcode() == Opcode.INVOKESTATIC;
        var receiver = isStatic ? null : pop(stack);
        var owner = classOf(invoke.owner().asSymbol());
        var returnType = classOf(type.returnType());
        var obfuscated = invoke.name().stringValue();

        var all = new ArrayList<Node>();
        if (receiver != null)
            all.add(receiver);
        all.addAll(arguments);

        // A constructed object stands for the values it was constructed from. Constructing the analyzed class (or
        // this(...)/super(...) in its constructor) is followed, for the fields it reads from the buffer.
        if (obfuscated.equals("<init>")) {
            var key = owner == null ? null : owner.getName() + ".<init>" + type.descriptorString();
            var constructor = key == null ? null : method(owner, "<init>", type.descriptorString());
            if (constructor != null && constructor.code().isPresent() && (receiver.kind() == Kind.PACKET || receiver.type() == analysis.packet)
                && analysis.running.size() < MAX_DEPTH && analysis.running.add(key)) {
                var constructorArguments = new ArrayList<Node>(List.of(Node.of(Kind.PACKET, owner)));
                constructorArguments.addAll(arguments);
                interpret(analysis, owner, constructor, constructorArguments, flow, new ArrayList<>());
                analysis.running.remove(key);
            }
            var constructed = derive(arguments, receiver.type());
            for (var i = 0; i < stack.size(); i++) {
                if (stack.get(i) == receiver)
                    stack.set(i, constructed);
            }
            return;
        }

        Node result;
        var target = owner == null ? null : target(analysis, owner, obfuscated, type.descriptorString(), isStatic, receiver, invoke.opcode());
        var key = target == null ? null : target.owner().getName() + "." + obfuscated + type.descriptorString();
        if (target != null && analysis.running.size() < MAX_DEPTH && analysis.running.add(key)) {
            // The packet's own code (super.write(buf), a helper): follow it with the values it's given.
            result = interpret(analysis, target.owner(), target.method(), all, flow, events);
            analysis.running.remove(key);
        } else if (owner != null && all.stream().anyMatch(node -> node.kind() == Kind.BUFFER)) {
            var fields = new LinkedHashSet<String>();
            for (var node : all) {
                if (node.kind() != Kind.BUFFER)
                    fields.addAll(node.fields());
            }
            var method = methodName(owner, obfuscated, type);
            if (!fields.isEmpty())
                events.add(new Event(fields, encoding(analysis, owner, method, obfuscated, type.descriptorString(), receiver, arguments), flow));
            // Buffer writes return the buffer, for chaining; some reads say how the value is read.
            var hint = receiver != null && receiver.kind() == Kind.BUFFER ? readHint(method.substring(method.lastIndexOf('.') + 1), arguments) : null;
            result = receiver != null && receiver.kind() == Kind.BUFFER && returnType != null && byteBuf.isAssignableFrom(returnType)
                ? receiver
                : hint != null ? new Node(Kind.VALUE, hint, null, List.of(), Set.of(), returnType, null, null) : Node.value(Set.of(), returnType);
        } else if (owner != null && (isCodec(returnType) || returnType == codecOperation)) {
            result = new Node(Kind.CALL, methodName(owner, obfuscated, type), receiver, arguments, Set.of(), returnType, null, null);
        } else {
            result = derive(all, returnType);
        }

        if (returnType != void.class)
            stack.add(result);
    }

    private record Target(Class<?> owner, MethodModel method) {}

    /** The code a call runs when it's the packet's own (in its class or superclasses), else null. */
    private Target target(Analysis analysis, Class<?> owner, String name, String descriptor, boolean isStatic, Node receiver, Opcode opcode) {
        // Virtual calls on the packet run its most derived override.
        var start = !isStatic && opcode != Opcode.INVOKESPECIAL && receiver.kind() == Kind.PACKET ? analysis.packet : owner;
        for (var type = start; type != null && type != Object.class && type != Record.class; type = type.getSuperclass()) {
            if (!type.isAssignableFrom(analysis.packet))
                return null;
            var method = method(type, name, descriptor);
            if (method != null)
                return method.code().isPresent() ? new Target(type, method) : null;
        }

        return null;
    }

    private Node dynamic(InvokeDynamicInstruction dynamic, List<Node> captured) {
        var arguments = dynamic.bootstrapArgs();
        if (dynamic.bootstrapMethod().owner().descriptorString().equals("Ljava/lang/invoke/LambdaMetafactory;")
            && arguments.size() > 1 && arguments.get(1) instanceof DirectMethodHandleDesc handle) {
            var owner = classOf(handle.owner());
            var name = owner == null ? handle.methodName() : methodName(owner, handle.methodName(), MethodTypeDesc.ofDescriptor(handle.lookupDescriptor()));
            var separator = name.lastIndexOf('.');
            var method = name.substring(separator + 1).startsWith("lambda$") ? "lambda" : name.substring(separator + 1);
            var reference = (separator < 0 ? "" : name.substring(0, separator)) + "::" + method;
            return new Node(Kind.LAMBDA, reference, null, captured, Set.of(), classOf(dynamic.typeSymbol().returnType()), handle, null);
        }

        // String concatenation and other dynamic values.
        return derive(captured, classOf(dynamic.typeSymbol().returnType()));
    }

    // Describing encodings.

    /**
     * How a call that got the buffer encodes the values it was given: a buffer method, a codec's {@code encode}, or a
     * value's own {@code write} method.
     */
    private Object encoding(Analysis analysis, Class<?> owner, String method, String obfuscated, String descriptor,
                            Node receiver, List<Node> arguments) {
        var simpleName = method.substring(method.lastIndexOf('.') + 1);
        var isBufferMethod = receiver != null ? receiver.kind() == Kind.BUFFER : byteBuf.isAssignableFrom(owner);
        if (isBufferMethod)
            return bufferWrite(analysis, simpleName, arguments.stream().filter(node -> node.kind() != Kind.BUFFER).toList());
        if (receiver != null && isCodec(receiver.type()) && simpleName.equals("encode"))
            return describe(receiver);
        if (KINDS.containsKey(method))
            return node(KINDS.get(method));

        // value.write(buf) or Value.write(buf, value): the value's class describes itself.
        var writesValue = receiver != null || arguments.stream().anyMatch(argument -> argument.kind() != Kind.BUFFER && argument.type() == owner);
        if (writesValue && simpleName.equals("write")) {
            var reference = writeReference(owner, obfuscated, descriptor);
            if (reference != null)
                return reference;
        }
        return opaque(method);
    }

    /** What a buffer method ({@code FriendlyByteBuf.writeX}) writes, given its arguments other than the buffer. */
    private Object bufferWrite(Analysis analysis, String name, List<Node> values) {
        return switch (name) {
            case "writeUtf" -> node("string", "max", values.size() > 1 ? constant(values, 1) : Integer.valueOf(MAX_STRING));
            case "writeEnum" -> enumNode(values.isEmpty() ? null : values.getFirst().type(), null, node("var_int"));
            case "writeCollection" -> values.size() > 1 ? node("list", "of", describeWriter(analysis, values.get(1))) : opaque(name);
            case "writeOptional" -> values.size() > 1 ? node("optional", "of", describeWriter(analysis, values.get(1))) : opaque(name);
            case "writeNullable" -> values.size() > 1 ? node("nullable", "of", describeWriter(analysis, values.get(1))) : opaque(name);
            case "writeMap" -> values.size() > 2
                ? node("map", "key", describeWriter(analysis, values.get(1)), "value", describeWriter(analysis, values.get(2)))
                : opaque(name);
            case "writeFixedBitSet" -> node("fixed_bit_set", "size", constant(values, 1));
            default -> KINDS.containsKey(name) ? node(KINDS.get(name)) : opaque("FriendlyByteBuf." + name);
        };
    }

    /** What a writer passed to a buffer method writes: a method reference, a codec or a lambda writing each element. */
    private Object describeWriter(Analysis analysis, Node writer) {
        if (writer.kind() != Kind.LAMBDA)
            return describe(writer);

        var handle = writer.handle();
        var owner = classOf(handle.owner());
        var method = writer.name().substring(writer.name().lastIndexOf(':') + 1);
        if (owner != null && byteBuf.isAssignableFrom(owner))
            return bufferWrite(analysis, method, List.of());
        // A bound codec::encode
        if (method.equals("encode") && !writer.arguments().isEmpty() && isCodec(writer.arguments().getFirst().type()))
            return describe(writer.arguments().getFirst());
        // Element::write
        if (method.equals("write") && owner != null) {
            var reference = writeReference(owner, handle.methodName(), handle.lookupDescriptor());
            if (reference != null)
                return reference;
        }

        var events = new ArrayList<Event>();
        if (owner != null && owner.isAssignableFrom(analysis.packet) && runLambda(analysis, writer, events) != null) {
            var encodings = events.stream().filter(event -> event.fields().contains(ELEMENT)).map(Event::encoding).toList();
            if (encodings.size() == 1)
                return encodings.getFirst();
            if (!encodings.isEmpty())
                return node("sequence", "of", encodings);
        }

        return opaque(writer.name());
    }

    /**
     * A codec expression as an encoding node: {@code {"kind": "var_int"}}, {@code {"kind": "optional", "of": ...}},
     * {@code {"kind": "type", "name": "PositionMoveRotation"}} for a vanilla record (see {@link #types}), or
     * {@code {"kind": "codec", "name": ...}} for codecs this can't describe.
     */
    private Object describe(Node node) {
        return switch (node.kind()) {
            case STATIC -> describeStatic(node);
            case CALL -> describeCall(node);
            default -> opaque(text(node));
        };
    }

    private Object describeStatic(Node node) {
        if (KINDS.containsKey(node.name()))
            return node(KINDS.get(node.name()));
        switch (node.name()) {
            case "ByteBufCodecs.STRING_UTF8" -> {
                return node("string", "max", MAX_STRING);
            }
            case "ItemStack.OPTIONAL_LIST_STREAM_CODEC" -> {
                return node("list", "of", node("item_stack"));
            }
            default -> { }
        }

        // Another class's codec: describe the expression its initializer builds it with.
        var tree = node.owner() == null ? null : staticsOf(node.owner()).get(node.name().substring(node.name().lastIndexOf('.') + 1));
        var described = tree == null ? null : describe(tree);
        return described == null || isOpaque(described) ? opaque(node.name()) : described;
    }

    private Object describeCall(Node call) {
        var arguments = call.arguments();
        var receiver = call.receiver();
        if (receiver != null && isCodec(receiver.type())) {
            switch (call.name().substring(call.name().lastIndexOf('.') + 1)) {
                // codec.map(constructor, getter): a record of one field, an enum written as its id, or the same values.
                case "map" -> {
                    var record = recordReference(call, arguments.getFirst());
                    if (record != null)
                        return record;
                    return enumNode(enumType(arguments.getFirst(), arguments.get(1)), idMethod(arguments.get(1)), describe(receiver));
                }
                case "cast", "mapStream" -> {
                    return describe(receiver);
                }
                case "apply" -> {
                    return applied(receiver, arguments.getFirst());
                }
                default -> { }
            }
        }

        return switch (call.name()) {
            case "StreamCodec.composite" -> orOpaque(recordReference(call, arguments.getLast()), call);
            case "Packet.codec", "StreamCodec.ofMember", "StreamCodec.of" -> orOpaque(recordReference(call, arguments.get(1)), call);
            case "StreamCodec.unit" -> node("unit");
            case "ByteBufCodecs.optional" -> node("optional", "of", describe(arguments.getFirst()));
            case "ByteBufCodecs.collection" -> node("list", "of", describe(arguments.get(1)), "max", constant(arguments, 2));
            case "ByteBufCodecs.map" -> node("map", "key", describe(arguments.get(1)), "value", describe(arguments.get(2)), "max", constant(arguments, 3));
            case "ByteBufCodecs.stringUtf8" -> node("string", "max", constant(arguments, 0));
            case "ByteBufCodecs.byteArray" -> node("byte_array", "max", constant(arguments, 0));
            case "ByteBufCodecs.registry" -> node("registry_id", "registry", registryId(arguments.getFirst()));
            case "ByteBufCodecs.holderRegistry" -> node("holder", "registry", registryId(arguments.getFirst()));
            // idMapper(byId, getId) writes an id as a VarInt; with an IdMap it's the map's id.
            case "ByteBufCodecs.idMapper" -> arguments.size() == 2
                ? enumNode(enumType(arguments.getFirst(), arguments.get(1)), idMethod(arguments.get(1)), node("var_int"))
                : node("var_int");
            // A nameless NBT tag, or an end tag for none (the lambda only sets the NBT size budget).
            case "ByteBufCodecs.optionalTagCodec" -> node("optional_nbt");
            // A resource key is written as its identifier; its registry is implied.
            case "ResourceKey.streamCodec" -> node("identifier");
            // The channel id, then the payload to the end of the packet: each channel's codec writes it, and unknown
            // channels fall back to DiscardedPayload.codec(id, limit), which copies the bytes up to the packet's limit.
            case "CustomPacketPayload.codec" -> node("sequence", "of",
                List.of(node("identifier"), node("remaining_bytes", "max", payloadLimit(arguments.getFirst()))));
            default -> opaque(text(call));
        };
    }

    /** {@code codec.apply(operation)}: a list, an optional, a length prefix or JSON of the codec. */
    private Object applied(Node codec, Node operation) {
        var name = operation.kind() == Kind.LAMBDA ? operation.name().replace("::", ".") : operation.name();
        var parameters = operation.kind() == Kind.CALL ? operation.arguments() : List.<Node>of();
        return switch (name == null ? "" : name) {
            case "ByteBufCodecs.list" -> node("list", "of", describe(codec), "max", constant(parameters, 0));
            case "ByteBufCodecs.collection" -> node("list", "of", describe(codec), "max", constant(parameters, 1));
            case "ByteBufCodecs.optional" -> node("optional", "of", describe(codec));
            // The codec's bytes after their VarInt length.
            case "ByteBufCodecs.lengthPrefixed" -> node("length_prefixed", "of", describe(codec), "max", constant(parameters, 0));
            // A serialization codec's value as a JSON string of at most the limit's length.
            case "ByteBufCodecs.fromCodec" -> codec.kind() == Kind.CALL && codec.name().equals("ByteBufCodecs.lenientJson")
                ? node("json", "codec", parameters.stream().filter(node -> isCodec(node.type())).findFirst().map(this::text).orElse(null),
                    "max", constant(codec.arguments(), 0))
                : opaque(text(codec) + ".apply(" + text(operation) + ")");
            default -> opaque(text(codec) + ".apply(" + text(operation) + ")");
        };
    }

    // Vanilla types.

    /**
     * A record type for a codec whose constructor (a lambda returning the class) builds a vanilla class from the
     * fields it writes; null when the class isn't one to describe or the codec doesn't map to its fields.
     */
    private Object recordReference(Node codec, Node constructor) {
        var type = returnType(constructor);
        if (!isDescribable(type))
            return null;
        return typeReference(type, () -> recordWrites(type, codec));
    }

    /** A record type for a class written by its own {@code write} method, or null. */
    private Object writeReference(Class<?> type, String obfuscated, String descriptor) {
        if (!isDescribable(type))
            return null;
        for (var owner = type; owner != null && owner != Object.class; owner = owner.getSuperclass()) {
            var method = method(owner, obfuscated, descriptor);
            if (method != null) {
                var write = new Target(owner, method);
                return method.code().isPresent() ? typeReference(type, () -> methodWrites(type, write)) : null;
            }
        }

        return null;
    }

    /** Registers a record type (once, by its Mojang name) and returns the encoding that refers to it. */
    private Object typeReference(Class<?> type, java.util.function.Supplier<Map<String, Write>> fields) {
        var name = mojang.nestedName(type);
        if (!types.containsKey(name)) {
            types.put(name, new TypeDefinition(type, Map.of(), null)); // against types that contain themselves
            var written = fields.get();
            // A codec that writes none of a class's fields doesn't describe it.
            if (written == null || written.isEmpty() && !hasNoInstanceFields(type)) {
                types.remove(name);
                return null;
            }
            types.put(name, new TypeDefinition(type, written, null));
        }
        return node("type", "name", name);
    }

    /**
     * An enum written as a number ({@code wire}), registering its values: each constant's ordinal, or what
     * {@code id} returns for it. For other types it's just the number.
     */
    private Object enumNode(Class<?> type, java.lang.reflect.Method id, Object wire) {
        if (type == null || !type.isEnum())
            return wire;

        var values = new LinkedHashMap<String, Integer>();
        for (var field : type.getDeclaredFields()) {
            if (!field.isEnumConstant())
                continue;
            field.setAccessible(true);
            var constant = (Enum<?>) VanillaDumper.Mojang.getStatic(field);
            values.put(mojang.fieldName(type, field), id == null ? constant.ordinal() : ((Number) VanillaDumper.Mojang.call(id, constant)).intValue());
        }

        var name = mojang.nestedName(type);
        var known = types.get(name);
        if (known == null)
            types.put(name, new TypeDefinition(type, null, values));
        else if (!values.equals(known.values()))
            System.out.println("Vanilla writes " + name + " with different values in different places; using the first");
        return node("enum", "type", name, "as", wire);
    }

    /** The no-argument method a lambda refers to on its own class (Enum::getId), for an enum's ids; else null. */
    private java.lang.reflect.Method idMethod(Node lambda) {
        if (lambda.kind() != Kind.LAMBDA || !lambda.handle().lookupDescriptor().startsWith("()"))
            return null;
        var owner = classOf(lambda.handle().owner());
        try {
            var method = owner == null ? null : owner.getDeclaredMethod(lambda.handle().methodName());
            if (method != null)
                method.setAccessible(true);
            return method;
        } catch (NoSuchMethodException e) {
            return null;
        }
    }

    /** The enum a (byId, getId) pair converts: what byId returns, or the class getId is declared on. */
    private Class<?> enumType(Node byId, Node getId) {
        var type = returnType(byId);
        if (type != null && type.isEnum())
            return type;
        var owner = getId.kind() == Kind.LAMBDA ? classOf(getId.handle().owner()) : null;
        return owner != null && owner.isEnum() ? owner : type;
    }

    /** The class a lambda returns (a constructor reference's class), or null. */
    private Class<?> returnType(Node lambda) {
        return lambda.kind() == Kind.LAMBDA ? classOf(lambda.handle().invocationType().returnType()) : null;
    }

    /** Whether a class is a vanilla class this describes as a record: concrete, and not an enum. */
    private boolean isDescribable(Class<?> type) {
        return type != null && !type.isPrimitive() && !type.isArray() && !type.isInterface() && !type.isEnum()
            && !java.lang.reflect.Modifier.isAbstract(type.getModifiers()) && mojang.className(type.getName()).startsWith("net.minecraft.");
    }

    private static boolean hasNoInstanceFields(Class<?> type) {
        return java.util.Arrays.stream(type.getDeclaredFields()).allMatch(field -> java.lang.reflect.Modifier.isStatic(field.getModifiers()));
    }

    /** A registry's id ({@code minecraft:entity_type}) from a {@code Registries.X} constant. */
    private String registryId(Node key) {
        if (key.kind() != Kind.STATIC || key.owner() == null)
            return null;
        var field = mojang.field(mojang.className(key.owner().getName()), key.name().substring(key.name().lastIndexOf('.') + 1));
        var value = VanillaDumper.Mojang.getStatic(field);
        return VanillaDumper.Mojang.call(mojang.method("net.minecraft.resources.ResourceKey", "identifier"), value).toString();
    }

    /** The limit a custom payload packet's fallback ({@code id -> DiscardedPayload.codec(id, limit)}) passes on. */
    private Integer payloadLimit(Node fallback) {
        if (fallback.kind() != Kind.LAMBDA)
            return null;
        var owner = classOf(fallback.handle().owner());
        var codec = owner == null ? null : runLambda(new Analysis(owner), fallback, new ArrayList<>());
        return codec != null && codec.kind() == Kind.CALL && codec.name().equals("DiscardedPayload.codec") ? constant(codec.arguments(), 1) : null;
    }

    // Encoding nodes.

    /** An encoding node: its kind and parameters (name, value pairs; null values are left out). */
    private static Map<String, Object> node(String kind, Object... parameters) {
        var node = new LinkedHashMap<String, Object>();
        node.put("kind", kind);
        for (var i = 0; i + 1 < parameters.length; i += 2) {
            if (parameters[i + 1] != null)
                node.put((String) parameters[i], parameters[i + 1]);
        }
        return node;
    }

    /** A codec this can't describe, by its expression. */
    private static Map<String, Object> opaque(String name) {
        return node("codec", "name", name);
    }

    private static boolean isOpaque(Object encoding) {
        return encoding instanceof Map<?, ?> map && "codec".equals(map.get("kind"));
    }

    private Object orOpaque(Object encoding, Node call) {
        return encoding != null ? encoding : opaque(text(call));
    }

    /** An integer constant argument, or null when the argument isn't a constant. */
    private static Integer constant(List<Node> arguments, int index) {
        if (index >= arguments.size() || arguments.get(index).kind() != Kind.CONSTANT)
            return null;
        try {
            return Integer.valueOf(arguments.get(index).name());
        } catch (NumberFormatException e) {
            return null;
        }
    }

    /** An expression as text, like {@code ByteBufCodecs.registry(Registries.BLOCK)}, for naming what isn't described. */
    private String text(Node node) {
        return switch (node.kind()) {
            case STATIC, LAMBDA, CONSTANT -> node.name();
            case CALL -> {
                var parts = new ArrayList<String>();
                if (node.receiver() != null)
                    parts.add(text(node.receiver()));
                for (var argument : node.arguments()) {
                    if (argument.kind() != Kind.VALUE)
                        parts.add(text(argument));
                }
                yield node.name() + "(" + String.join(", ", parts) + ")";
            }
            default -> "?";
        };
    }

    // Read hints: what a buffer read says about the value it returns, carried as the value's name until it's stored.

    private static final String READ_HINT = "read:";

    private static String readHint(String method, List<Node> arguments) {
        return switch (method) {
            case "readUtf" -> constant(arguments, 0) instanceof Integer max ? READ_HINT + "max=" + max : null;
            case "readUnsignedByte", "readUnsignedShort" -> READ_HINT + "unsigned";
            default -> null;
        };
    }

    private static Map<String, Object> readHint(String hint) {
        var value = hint.substring(READ_HINT.length());
        return value.startsWith("max=") ? Map.of("max", Integer.valueOf(value.substring(4))) : Map.of("unsigned", true);
    }

    // Helpers.

    private boolean isCodec(Class<?> type) {
        return type != null && (streamEncoder.isAssignableFrom(type) || codec.isAssignableFrom(type));
    }

    /** A value computed from others, from the fields they're computed from. */
    private static Node derive(List<Node> nodes, Class<?> type) {
        var fields = new LinkedHashSet<String>();
        for (var node : nodes)
            fields.addAll(node.fields());
        return Node.value(fields, type);
    }

    private static Node pop(List<Node> stack) {
        return stack.isEmpty() ? Node.value(Set.of(), null) : stack.removeLast();
    }

    private static List<Node> popArguments(List<Node> stack, int count) {
        var arguments = new Node[count];
        for (var i = count - 1; i >= 0; i--)
            arguments[i] = pop(stack);
        return List.of(arguments);
    }

    /** The stack instructions, where long and double values take two slots. */
    private static void shuffle(Opcode opcode, List<Node> stack) {
        switch (opcode) {
            case POP -> pop(stack);
            case POP2 -> {
                if (!pop(stack).wide())
                    pop(stack);
            }
            case DUP -> stack.add(stack.isEmpty() ? Node.value(Set.of(), null) : stack.getLast());
            case DUP_X1 -> {
                var a = pop(stack);
                var b = pop(stack);
                stack.addAll(List.of(a, b, a));
            }
            case DUP_X2 -> {
                var a = pop(stack);
                var b = pop(stack);
                if (b.wide()) {
                    stack.addAll(List.of(a, b, a));
                } else {
                    var c = pop(stack);
                    stack.addAll(List.of(a, c, b, a));
                }
            }
            case DUP2 -> {
                var a = pop(stack);
                if (a.wide()) {
                    stack.addAll(List.of(a, a));
                } else {
                    var b = pop(stack);
                    stack.addAll(List.of(b, a, b, a));
                }
            }
            case DUP2_X1 -> {
                var a = pop(stack);
                var b = pop(stack);
                if (a.wide()) {
                    stack.addAll(List.of(a, b, a));
                } else {
                    var c = pop(stack);
                    stack.addAll(List.of(b, a, c, b, a));
                }
            }
            case DUP2_X2 -> {
                var a = pop(stack);
                var b = pop(stack);
                if (a.wide() && b.wide()) {
                    stack.addAll(List.of(a, b, a));
                } else if (a.wide()) {
                    var c = pop(stack);
                    stack.addAll(List.of(a, c, b, a));
                } else {
                    var c = pop(stack);
                    if (c.wide()) {
                        stack.addAll(List.of(b, a, c, b, a));
                    } else {
                        var d = pop(stack);
                        stack.addAll(List.of(b, a, d, c, b, a));
                    }
                }
            }
            case SWAP -> {
                var a = pop(stack);
                var b = pop(stack);
                stack.addAll(List.of(a, b));
            }
            default -> { }
        }
    }

    /**
     * Which instructions run only sometimes (those a branch jumps over, and those after a return or throw that only
     * sometimes runs) and which run in a loop (javac's loops test at the top and jump back from the bottom). Also
     * tracks the packet fields each branch's condition reads, so a constant chosen by a condition without calls in
     * between ({@code this.flag ? 1 : 0}) counts as coming from those fields.
     */
    private static final class Branches {
        final boolean[] conditional;
        final boolean[] repeated;
        private final List<List<Integer>> containing = new ArrayList<>();
        private final Map<Integer, Set<String>> conditions = new HashMap<>();
        // The branches each goto at the end of an if block belongs to: its else block depends on their condition.
        private final Map<Integer, List<Integer>> elseOf = new HashMap<>();

        Branches(List<CodeElement> elements, Map<Label, Integer> labels) {
            conditional = new boolean[elements.size()];
            repeated = new boolean[elements.size()];
            for (var i = 0; i < elements.size(); i++)
                containing.add(new ArrayList<>());

            for (var i = 0; i < elements.size(); i++) {
                List<Label> targets = switch (elements.get(i)) {
                    case BranchInstruction branch -> List.of(branch.target());
                    case TableSwitchInstruction table -> concat(table.defaultTarget(), table.cases());
                    case LookupSwitchInstruction lookup -> concat(lookup.defaultTarget(), lookup.cases());
                    default -> List.of();
                };
                for (var label : targets) {
                    var target = labels.get(label);
                    if (target <= i) {
                        // The jump back to a loop's test.
                        for (var j = target; j <= i; j++)
                            repeated[j] = true;
                        continue;
                    }

                    // A test that leaves a loop jumps to just after the loop's jump back.
                    var last = target - 1;
                    while (last > i && !(elements.get(last) instanceof Instruction))
                        last--;
                    var jump = last > i && elements.get(last) instanceof BranchInstruction branch
                        && (branch.opcode() == Opcode.GOTO || branch.opcode() == Opcode.GOTO_W) ? branch : null;
                    var leavesLoop = jump != null && labels.get(jump.target()) <= i;
                    var selects = elements.subList(i + 1, target).stream().noneMatch(element -> element instanceof InvokeInstruction);
                    for (var j = i + 1; j < target; j++) {
                        if (leavesLoop) {
                            repeated[j] = true;
                        } else {
                            conditional[j] = true;
                            if (selects)
                                containing.get(j).add(i);
                        }
                    }
                    if (jump != null && !leavesLoop)
                        elseOf.computeIfAbsent(last, key -> new ArrayList<>()).add(i);
                }
            }

            var exited = false;
            for (var i = 0; i < elements.size(); i++) {
                conditional[i] |= exited;
                if (conditional[i] && (elements.get(i) instanceof ReturnInstruction || elements.get(i) instanceof ThrowInstruction))
                    exited = true;
            }
        }

        /** Records the fields a branch's condition read when the interpreter reaches it. */
        void reached(int index, List<Node> operands) {
            var fields = new LinkedHashSet<String>();
            operands.forEach(operand -> fields.addAll(operand.fields()));
            for (var branch : elseOf.getOrDefault(index, List.of()))
                fields.addAll(conditions.getOrDefault(branch, Set.of()));
            conditions.put(index, fields);
        }

        /** The fields of the conditions that decide whether an instruction runs. */
        Set<String> conditionFields(int index) {
            var fields = new LinkedHashSet<String>();
            for (var branch : containing.get(index))
                fields.addAll(conditions.getOrDefault(branch, Set.of()));
            return fields;
        }

        private static List<Label> concat(Label first, List<SwitchCase> cases) {
            var labels = new ArrayList<Label>();
            labels.add(first);
            cases.forEach(item -> labels.add(item.target()));
            return labels;
        }
    }

    private MethodModel method(Class<?> type, String name, String descriptor) {
        var model = model(type);
        if (model == null)
            return null;
        for (var method : model.methods()) {
            if (method.methodName().equalsString(name) && method.methodType().equalsString(descriptor))
                return method;
        }

        return null;
    }

    private ClassModel model(Class<?> type) {
        if (models.containsKey(type.getName()))
            return models.get(type.getName());

        var loader = type.getClassLoader() != null ? type.getClassLoader() : ClassLoader.getSystemClassLoader();
        ClassModel model = null;
        try (var input = loader.getResourceAsStream(type.getName().replace('.', '/') + ".class")) {
            if (input != null)
                model = ClassFile.of().parse(input.readAllBytes());
        } catch (IOException e) {
            throw new UncheckedIOException(e);
        }
        models.put(type.getName(), model);
        return model;
    }

    /** A field's Mojang name, looked up on the class that declares it (the instruction may name a subclass). */
    private String fieldName(Class<?> owner, String obfuscated) {
        for (var type = owner; type != null; type = type.getSuperclass()) {
            try {
                return mojang.fieldName(type, type.getDeclaredField(obfuscated));
            } catch (NoSuchFieldException | LinkageError e) {
                // declared further up
            }
        }

        return obfuscated;
    }

    /** A method's Mojang name as {@code Owner.name}, looked up on the class or interface that declares it. */
    private String methodName(Class<?> owner, String obfuscated, MethodTypeDesc type) {
        var parameters = type.parameterList().stream().map(this::mojangTypeName).collect(Collectors.joining(","));
        var queue = new ArrayDeque<Class<?>>(List.of(owner));
        var seen = new HashSet<Class<?>>();
        while (!queue.isEmpty()) {
            var current = queue.removeFirst();
            if (!seen.add(current))
                continue;
            var name = mojang.methodName(current, obfuscated, parameters);
            if (name != null)
                return mojang.nestedName(current) + "." + name;
            if (current.getSuperclass() != null)
                queue.add(current.getSuperclass());
            queue.addAll(List.of(current.getInterfaces()));
        }

        return mojang.nestedName(owner) + "." + obfuscated;
    }

    /** A type as the mappings spell parameter types: {@code int}, {@code net.minecraft.core.BlockPos}, {@code byte[]}. */
    private String mojangTypeName(ClassDesc type) {
        if (type.isPrimitive())
            return type.displayName();
        if (type.isArray())
            return mojangTypeName(type.componentType()) + "[]";
        var descriptor = type.descriptorString();
        return mojang.className(descriptor.substring(1, descriptor.length() - 1).replace('/', '.'));
    }

    private Class<?> classOf(ClassDesc type) {
        if (type.isPrimitive()) {
            return switch (type.descriptorString()) {
                case "Z" -> boolean.class;
                case "B" -> byte.class;
                case "C" -> char.class;
                case "S" -> short.class;
                case "I" -> int.class;
                case "J" -> long.class;
                case "F" -> float.class;
                case "D" -> double.class;
                default -> void.class;
            };
        }

        var descriptor = type.descriptorString();
        var name = type.isArray() ? descriptor.replace('/', '.') : descriptor.substring(1, descriptor.length() - 1).replace('/', '.');
        try {
            return Class.forName(name, false, PacketFormats.class.getClassLoader());
        } catch (ClassNotFoundException | LinkageError e) {
            return null;
        }
    }

    private static Class<?> kindClass(TypeKind kind) {
        return switch (kind) {
            case LONG -> long.class;
            case DOUBLE -> double.class;
            case FLOAT -> float.class;
            case VOID -> void.class;
            case REFERENCE -> null;
            default -> int.class;
        };
    }
}
