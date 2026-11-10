import java.lang.classfile.ClassFile;
import java.lang.classfile.CodeBuilder;
import java.lang.classfile.CodeModel;
import java.lang.classfile.MethodModel;
import java.lang.classfile.Opcode;
import java.lang.classfile.instruction.InvokeInstruction;
import java.lang.classfile.instruction.ReturnInstruction;
import java.lang.constant.ClassDesc;
import java.lang.constant.MethodTypeDesc;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.jar.JarFile;
import java.util.jar.JarEntry;
import java.util.jar.JarOutputStream;
import static java.lang.constant.ConstantDescs.*;

/**
 * Adds observation callbacks to two cached vanilla classes in a scratch directory. The original jar is never
 * changed. Recording happens just after a changed state is stored, before its placement and neighbor effects,
 * and when destroyBlock succeeds. Callbacks are absent outside a selected case and cannot change the return value.
 * No vanilla implementation is bundled with this source or committed with the fixtures.
 */
public final class BlockUseInstrumentation {
    private static final ClassDesc Chunk = ClassDesc.of("net.minecraft.world.level.chunk.LevelChunk");
    private static final ClassDesc Level = ClassDesc.of("net.minecraft.world.level.Level");
    private static final ClassDesc Consumer = ClassDesc.of("java.util.function.Consumer");
    private static final MethodTypeDesc Callback = MethodTypeDesc.of(CD_void, CD_Object, CD_Object, CD_Object, CD_int);
    private static final String Method = "obsidianObserveBlockUse";

    public static void main(String[] args) throws Exception {
        try (var jar = new JarFile(args[0])) {
            var directory = Path.of(args[1]);
            patch(jar, directory, Chunk, true);
            patch(jar, directory, Level, false);
            // Mixing unsigned observed classes with signed classes of the same package is forbidden by the JVM.
            // Keep an unsigned scratch copy of the jar; the shared, signed original stays untouched.
            try (var output = new JarOutputStream(Files.newOutputStream(directory.resolve("vanilla-observed.jar")))) {
                for (var entry : jar.stream().toList()) {
                    if (entry.getName().matches("META-INF/[^/]+\\.(SF|RSA|DSA|EC)"))
                        continue;

                    output.putNextEntry(new JarEntry(entry.getName()));
                    var observed = directory.resolve(entry.getName());
                    if (!entry.isDirectory() && Files.isRegularFile(observed)) {
                        Files.copy(observed, output);
                    } else if (!entry.isDirectory()) {
                        try (var content = jar.getInputStream(entry)) {
                            content.transferTo(output);
                        }
                    }
                    output.closeEntry();
                }
            }
        }
    }

    private static void patch(JarFile jar, Path directory, ClassDesc type, boolean storage) throws Exception {
        var name = type.descriptorString();
        var resource = name.substring(1, name.length() - 1) + ".class";
        byte[] original;
        try (var stream = jar.getInputStream(jar.getJarEntry(resource))) {
            original = stream.readAllBytes();
        }

        var hooks = new int[1];
        var classFile = ClassFile.of();
        var model = classFile.parse(original);
        var patched = classFile.transformClass(model, (builder, element) -> {
            var target = storage ? "setBlockState" : "destroyBlock";
            var descriptor = storage
                ? "(Lnet/minecraft/core/BlockPos;Lnet/minecraft/world/level/block/state/BlockState;I)"
                    + "Lnet/minecraft/world/level/block/state/BlockState;"
                : "(Lnet/minecraft/core/BlockPos;ZLnet/minecraft/world/entity/Entity;I)Z";
            if (element instanceof MethodModel method && method.methodName().equalsString(target)
                && method.methodType().equalsString(descriptor)) {
                builder.transformMethod(method, (methodBuilder, member) -> {
                    if (member instanceof CodeModel code) {
                        methodBuilder.transformCode(code, (out, instruction) -> {
                            if (storage) {
                                out.with(instruction);
                                if (instruction instanceof InvokeInstruction call
                                    && call.owner().asInternalName().equals("net/minecraft/world/level/chunk/LevelChunkSection")
                                    && call.name().equalsString("setBlockState")) {
                                    var unchanged = out.newLabel();
                                    out.dup().aload(2).if_acmpeq(unchanged);
                                    out.aload(0).getfield(Chunk, "level", Level);
                                    out.aload(1).aload(2).iload(3).invokestatic(type, Method, Callback);
                                    out.labelBinding(unchanged);
                                    hooks[0]++;
                                }
                            } else {
                                if (instruction instanceof ReturnInstruction returned
                                    && returned.opcode() == Opcode.IRETURN) {
                                    var failed = out.newLabel();
                                    out.dup().ifeq(failed);
                                    out.aload(0).aload(1).aload(5).iload(2).invokestatic(type, Method, Callback);
                                    out.labelBinding(failed);
                                    hooks[0]++;
                                }
                                out.with(instruction);
                            }
                        });
                    } else {
                        methodBuilder.with(member);
                    }
                });
            } else {
                builder.with(element);
            }

            if (element == model.elementList().getLast()) {
                builder.withMethod(Method, Callback, ClassFile.ACC_PRIVATE | ClassFile.ACC_STATIC, method ->
                    method.withCode(code -> callback(code, storage ? "writes" : "destroyed")));
            }
        });

        var expectedHooks = storage ? 1 : 2;
        if (hooks[0] != expectedHooks) {
            throw new IllegalStateException("Expected " + expectedHooks + " observation points in " + resource
                + ", found " + hooks[0]);
        }

        var destination = directory.resolve(resource);
        Files.createDirectories(destination.getParent());
        Files.write(destination, patched);
    }

    // Properties hold a bootstrap Consumer, so the patched classes do not depend on the probe's source classloader.
    private static void callback(CodeBuilder code, String kind) {
        code.invokestatic(ClassDesc.of("java.lang.System"), "getProperties",
            MethodTypeDesc.of(ClassDesc.of("java.util.Properties")));
        code.ldc("obsidian.blockUse." + kind);
        code.invokevirtual(ClassDesc.of("java.util.Properties"), "get", MethodTypeDesc.of(CD_Object, CD_Object));
        code.checkcast(Consumer).astore(4);
        var absent = code.newLabel();
        code.aload(4).ifnull(absent);
        code.aload(4).iconst_4().anewarray(CD_Object);
        for (int index = 0; index < 4; index++) {
            code.dup().loadConstant(index);
            if (index == 3) {
                code.iload(3).invokestatic(CD_Integer, "valueOf", MethodTypeDesc.of(CD_Integer, CD_int));
            } else {
                code.aload(index);
            }
            code.aastore();
        }
        code.invokeinterface(Consumer, "accept", MethodTypeDesc.of(CD_void, CD_Object));
        code.labelBinding(absent).return_();
    }
}
