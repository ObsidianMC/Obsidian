import java.lang.classfile.ClassFile;
import java.lang.classfile.CodeBuilder;
import java.lang.classfile.CodeModel;
import java.lang.classfile.MethodModel;
import java.lang.constant.ClassDesc;
import java.lang.constant.MethodTypeDesc;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.jar.JarEntry;
import java.util.jar.JarFile;
import java.util.jar.JarOutputStream;
import static java.lang.constant.ConstantDescs.*;

/** Observes native chunk requests in an unsigned scratch copy, without changing the shared vanilla jar. */
public final class JigsawBlockInstrumentation {
    private static final ClassDesc Cache = ClassDesc.of("net.minecraft.server.level.ServerChunkCache");
    private static final ClassDesc Consumer = ClassDesc.of("java.util.function.Consumer");
    private static final String Hook = "obsidianObserveJigsawChunk";
    private static final MethodTypeDesc Callback = MethodTypeDesc.of(CD_void, CD_int, CD_int, CD_Object, CD_boolean);

    public static void main(String[] args) throws Exception {
        var directory = Path.of(args[1]);
        Files.createDirectories(directory);
        var resource = "net/minecraft/server/level/ServerChunkCache.class";
        try (var jar = new JarFile(args[0])) {
            byte[] original;
            try (var stream = jar.getInputStream(jar.getJarEntry(resource))) {
                original = stream.readAllBytes();
            }

            var classFile = ClassFile.of();
            var model = classFile.parse(original);
            var hooks = new int[1];
            var patched = classFile.transformClass(model, (builder, element) -> {
                if (element instanceof MethodModel method && method.methodName().equalsString("getChunk")
                    && method.methodType().equalsString("(IILnet/minecraft/world/level/chunk/status/ChunkStatus;Z)"
                        + "Lnet/minecraft/world/level/chunk/ChunkAccess;")) {
                    builder.transformMethod(method, (methodBuilder, member) -> {
                        if (member instanceof CodeModel code) {
                            var first = new boolean[]{true};
                            methodBuilder.transformCode(code, (out, instruction) -> {
                                if (first[0]) {
                                    out.iload(1).iload(2).aload(3).iload(4).invokestatic(Cache, Hook, Callback);
                                    first[0] = false;
                                    hooks[0]++;
                                }
                                out.with(instruction);
                            });
                        } else {
                            methodBuilder.with(member);
                        }
                    });
                } else {
                    builder.with(element);
                }

                if (element == model.elementList().getLast()) {
                    builder.withMethod(Hook, Callback, ClassFile.ACC_PRIVATE | ClassFile.ACC_STATIC, method ->
                        method.withCode(JigsawBlockInstrumentation::callback));
                }
            });
            if (hooks[0] != 1)
                throw new IllegalStateException("Expected one chunk-request observation point, found " + hooks[0]);

            try (var output = new JarOutputStream(Files.newOutputStream(directory.resolve("vanilla-observed.jar")))) {
                for (var entry : jar.stream().toList()) {
                    if (entry.getName().matches("META-INF/[^/]+\\.(SF|RSA|DSA|EC)"))
                        continue;

                    output.putNextEntry(new JarEntry(entry.getName()));
                    if (entry.getName().equals(resource)) {
                        output.write(patched);
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

    private static void callback(CodeBuilder code) {
        code.invokestatic(ClassDesc.of("java.lang.System"), "getProperties",
            MethodTypeDesc.of(ClassDesc.of("java.util.Properties")));
        code.ldc("obsidian.jigsaw.chunks");
        code.invokevirtual(ClassDesc.of("java.util.Properties"), "get", MethodTypeDesc.of(CD_Object, CD_Object));
        code.checkcast(Consumer).astore(4);
        var absent = code.newLabel();
        code.aload(4).ifnull(absent);
        code.aload(4).iconst_4().anewarray(CD_Object);
        code.dup().iconst_0().iload(0).invokestatic(CD_Integer, "valueOf", MethodTypeDesc.of(CD_Integer, CD_int))
            .aastore();
        code.dup().iconst_1().iload(1).invokestatic(CD_Integer, "valueOf", MethodTypeDesc.of(CD_Integer, CD_int))
            .aastore();
        code.dup().iconst_2().aload(2).aastore();
        code.dup().iconst_3().iload(3).invokestatic(CD_Boolean, "valueOf", MethodTypeDesc.of(CD_Boolean, CD_boolean))
            .aastore();
        code.invokeinterface(Consumer, "accept", MethodTypeDesc.of(CD_void, CD_Object));
        code.labelBinding(absent).return_();
    }
}
