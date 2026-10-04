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
     * How a field is sent: its position among the packet's writes and its encodings in order; whether it's only
     * sometimes written, written in a loop, and written together with other fields in one value (a flag byte).
     */
    record Write(int order, String encoding, boolean conditional, boolean repeated, boolean packed) {}

    // The same primitive encodings named alike, whether written by a buffer method (writeUtf) or a codec.
    private static final Map<String, String> ALIASES = Map.ofEntries(
        Map.entry("ByteBufCodecs.BOOL", "Boolean"), Map.entry("ByteBufCodecs.BYTE", "Byte"),
        Map.entry("ByteBufCodecs.SHORT", "Short"), Map.entry("ByteBufCodecs.UNSIGNED_SHORT", "UnsignedShort"),
        Map.entry("ByteBufCodecs.INT", "Int"), Map.entry("ByteBufCodecs.VAR_INT", "VarInt"),
        Map.entry("ByteBufCodecs.LONG", "Long"), Map.entry("ByteBufCodecs.VAR_LONG", "VarLong"),
        Map.entry("ByteBufCodecs.FLOAT", "Float"), Map.entry("ByteBufCodecs.DOUBLE", "Double"),
        Map.entry("ByteBufCodecs.STRING_UTF8", "String"), Map.entry("ByteBufCodecs.stringUtf8", "String"),
        Map.entry("ByteBufCodecs.BYTE_ARRAY", "ByteArray"), Map.entry("ByteBufCodecs.byteArray", "ByteArray"),
        Map.entry("ByteBufCodecs.LONG_ARRAY", "LongArray"), Map.entry("UUIDUtil.STREAM_CODEC", "UUID"),
        Map.entry("VarInt.write", "VarInt"), Map.entry("VarLong.write", "VarLong"), Map.entry("Utf", "String"));

    // Codec factories that wrap other codecs, written as generics: ByteBufCodecs.optional(X) is Optional<X>.
    private static final Map<String, String> WRAPPERS = Map.of(
        "ByteBufCodecs.optional", "Optional", "ByteBufCodecs.list", "List",
        "ByteBufCodecs.collection", "Collection", "ByteBufCodecs.map", "Map");

    // The marker field of a collection's element, for describing what a writer lambda writes per element.
    private static final String ELEMENT = "\0element";
    private static final int MAX_DEPTH = 8;

    private final VanillaDumper.Mojang mojang;
    private final Class<?> byteBuf;
    private final Class<?> streamEncoder;
    private final Class<?> codec;
    private final Class<?> codecOperation;
    private final Map<String, ClassModel> models = new HashMap<>();

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
        var analysis = new Analysis(packet);
        var initializer = method(packet, "<clinit>", "()V");
        if (initializer != null)
            interpret(analysis, packet, initializer, List.of(), Flow.ALWAYS, new ArrayList<>());

        var codecs = analysis.statics.keySet().stream().filter(name -> name.endsWith("STREAM_CODEC")).sorted().toList();
        if (codecs.isEmpty())
            return null;
        var events = new ArrayList<Event>();
        var name = codecs.contains("STREAM_CODEC") ? "STREAM_CODEC" : codecs.getFirst();
        if (!readCodec(analysis, analysis.statics.get(name), events))
            return null;

        var writes = new LinkedHashMap<String, List<Event>>();
        for (var event : events) {
            for (var field : event.fields())
                writes.computeIfAbsent(field, key -> new ArrayList<>()).add(event);
        }

        var result = new HashMap<String, Write>();
        for (var entry : writes.entrySet()) {
            var encodings = new ArrayList<String>();
            for (var event : entry.getValue()) {
                if (encodings.isEmpty() || !encodings.getLast().equals(event.encoding()))
                    encodings.add(event.encoding());
            }
            var fieldEvents = entry.getValue();
            result.put(entry.getKey(), new Write(events.indexOf(fieldEvents.getFirst()), String.join(", ", encodings),
                fieldEvents.stream().allMatch(event -> event.flow().conditional()),
                fieldEvents.stream().allMatch(event -> event.flow().repeated()),
                fieldEvents.stream().anyMatch(event -> event.fields().size() > 1)));
        }

        return result;
    }

    /** Records what a codec expression writes of the packet, or returns false for codecs this doesn't understand. */
    private boolean readCodec(Analysis analysis, Node codec, List<Event> events) {
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
                arguments.add(new Node(Kind.VALUE, "element", null, List.of(), Set.of(ELEMENT), parameter, null));
        }

        return interpret(analysis, owner, method, arguments, Flow.ALWAYS, events);
    }

    // The interpreter.

    /** One packet's analysis: its class and the static fields its initializer sets. */
    private static final class Analysis {
        final Class<?> packet;
        final Map<String, Node> statics = new HashMap<>();
        final Set<String> running = new HashSet<>();

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
                        DirectMethodHandleDesc handle) {
        static Node of(Kind kind, Class<?> type) {
            return new Node(kind, null, null, List.of(), Set.of(), type, null);
        }

        static Node value(Set<String> fields, Class<?> type) {
            return new Node(Kind.VALUE, null, null, List.of(), fields, type, null);
        }

        boolean wide() {
            return type == long.class || type == double.class;
        }
    }

    /** A call that got the buffer: the packet fields it was given, how it encodes them, and when it runs. */
    private record Event(Set<String> fields, String encoding, Flow flow) {}

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
                    List.of(), branches.conditionFields(i), kindClass(constant.typeKind()), null));
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
                var own = owner == analysis.packet ? analysis.statics.get(name) : null;
                var ownerName = owner == null ? field.owner().asInternalName() : mojang.nestedName(owner);
                stack.add(own != null ? own : new Node(Kind.STATIC, ownerName + "." + name, null, List.of(), Set.of(), type, null));
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
            case PUTFIELD -> popArguments(stack, 2);
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

        // A constructed object stands for the values it was constructed from.
        if (obfuscated.equals("<init>")) {
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
                events.add(new Event(fields, encoding(analysis, owner, method, receiver, arguments), flow));
            // Buffer writes return the buffer, for chaining.
            result = receiver != null && receiver.kind() == Kind.BUFFER && returnType != null && byteBuf.isAssignableFrom(returnType)
                ? receiver : Node.value(Set.of(), returnType);
        } else if (owner != null && (isCodec(returnType) || returnType == codecOperation)) {
            result = new Node(Kind.CALL, methodName(owner, obfuscated, type), receiver, arguments, Set.of(), returnType, null);
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
            return new Node(Kind.LAMBDA, reference, null, captured, Set.of(), classOf(dynamic.typeSymbol().returnType()), handle);
        }

        // String concatenation and other dynamic values.
        return derive(captured, classOf(dynamic.typeSymbol().returnType()));
    }

    // Describing encodings.

    /** How a call that got the buffer encodes its values. */
    private String encoding(Analysis analysis, Class<?> owner, String method, Node receiver, List<Node> arguments) {
        var simpleName = method.substring(method.lastIndexOf('.') + 1);
        var isBufferMethod = receiver != null ? receiver.kind() == Kind.BUFFER : byteBuf.isAssignableFrom(owner);
        if (isBufferMethod) {
            // buf.writeVarInt(value), buf.writeCollection(list, writer)
            var name = simpleName.startsWith("write") && simpleName.length() > 5 ? simpleName.substring(5) : simpleName;
            name = ALIASES.getOrDefault(name, name);
            var writers = arguments.stream()
                .filter(node -> node.kind() == Kind.LAMBDA || isCodec(node.type()))
                .map(node -> describeWriter(analysis, node))
                .toList();
            return writers.isEmpty() ? name : name + "<" + String.join(", ", writers) + ">";
        }
        if (receiver != null && isCodec(receiver.type()) && simpleName.equals("encode"))
            return describe(receiver);

        // value.write(buf), Helper.write(buf, value)
        return ALIASES.getOrDefault(method, method);
    }

    /** What a writer passed to a buffer method writes: a method reference, a codec or a lambda writing each element. */
    private String describeWriter(Analysis analysis, Node writer) {
        if (writer.kind() != Kind.LAMBDA)
            return describe(writer);

        var owner = classOf(writer.handle().owner());
        if (owner != null && byteBuf.isAssignableFrom(owner)) {
            var name = writer.name().substring(writer.name().lastIndexOf(':') + 1);
            name = name.startsWith("write") && name.length() > 5 ? name.substring(5) : name;
            return ALIASES.getOrDefault(name, name);
        }
        // A bound codec::encode
        if (writer.name().endsWith("::encode") && !writer.arguments().isEmpty() && isCodec(writer.arguments().getFirst().type()))
            return describe(writer.arguments().getFirst());

        var events = new ArrayList<Event>();
        if (owner != null && owner.isAssignableFrom(analysis.packet) && runLambda(analysis, writer, events) != null) {
            var encodings = events.stream().filter(event -> event.fields().contains(ELEMENT)).map(Event::encoding).toList();
            if (encodings.size() == 1)
                return encodings.getFirst();
            if (!encodings.isEmpty())
                return "(" + String.join(", ", encodings) + ")";
        }

        return writer.name();
    }

    /** A codec expression, like {@code VarInt}, {@code Optional<ComponentSerialization.TRUSTED_STREAM_CODEC>}. */
    private String describe(Node node) {
        return switch (node.kind()) {
            case STATIC -> ALIASES.getOrDefault(node.name(), node.name());
            case CALL -> describeCall(node);
            case LAMBDA, CONSTANT -> node.name();
            default -> "?";
        };
    }

    private String describeCall(Node call) {
        var simpleName = call.name().substring(call.name().lastIndexOf('.') + 1);
        if (call.receiver() != null && isCodec(call.receiver().type())) {
            switch (simpleName) {
                // Mapping a codec's values doesn't change how they're written.
                case "map", "cast", "mapStream" -> {
                    return describe(call.receiver());
                }
                // codec.apply(ByteBufCodecs.list()) is a list of the codec; other operations stay as written.
                case "apply" -> {
                    var operation = call.arguments().getFirst();
                    var inner = describe(call.receiver());
                    var wrapper = operation.name() == null ? null : WRAPPERS.get(operation.name().replace("::", "."));
                    if (wrapper != null && (operation.kind() == Kind.CALL || operation.kind() == Kind.LAMBDA))
                        return wrapper + "<" + inner + ">";
                    return inner + ".apply(" + describe(operation) + ")";
                }
                default -> { }
            }
        }
        if (ALIASES.containsKey(call.name()))
            return ALIASES.get(call.name());

        if (WRAPPERS.containsKey(call.name())) {
            var codecs = call.arguments().stream().filter(node -> isCodec(node.type())).map(this::describe).toList();
            return WRAPPERS.get(call.name()) + "<" + String.join(", ", codecs) + ">";
        }

        // Other factories by name, with the arguments that describe something (not plain values).
        var arguments = new ArrayList<String>();
        if (call.receiver() != null)
            arguments.add(describe(call.receiver()));
        for (var argument : call.arguments()) {
            if (argument.kind() != Kind.VALUE)
                arguments.add(describe(argument));
        }
        return call.name() + "(" + String.join(", ", arguments) + ")";
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
