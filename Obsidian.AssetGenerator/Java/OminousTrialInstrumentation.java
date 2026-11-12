import java.lang.classfile.ClassFile;
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

/** Supplies seeded private projectile randomness to release-to-impact probes in an unsigned scratch jar. */
public final class OminousTrialInstrumentation {
    public static void main(String[] args) throws Exception {
        var directory = Path.of(args[1]);
        Files.createDirectories(directory);
        var resource = "net/minecraft/world/entity/projectile/Projectile.class";
        var type = ClassDesc.of("net.minecraft.world.entity.projectile.Projectile");
        var hook = "obsidianSeedOminousShot";
        var signature = MethodTypeDesc.of(CD_void, CD_Object);
        try (var jar = new JarFile(args[0])) {
            byte[] original;
            try (var stream = jar.getInputStream(jar.getJarEntry(resource))) {
                original = stream.readAllBytes();
            }

            var classFile = ClassFile.of();
            var model = classFile.parse(original);
            var hooks = new int[1];
            var patched = classFile.transformClass(model, (builder, element) -> {
                if (element instanceof MethodModel method && method.methodName().equalsString("shoot")
                    && method.methodType().equalsString("(DDDFF)V")) {
                    builder.transformMethod(method, (methodBuilder, member) -> {
                        if (member instanceof CodeModel code) {
                            var first = new boolean[]{true};
                            methodBuilder.transformCode(code, (out, instruction) -> {
                                if (first[0]) {
                                    out.aload(0).invokestatic(type, hook, signature);
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
                    builder.withMethod(hook, signature, ClassFile.ACC_PRIVATE | ClassFile.ACC_STATIC, method ->
                        method.withCode(out -> {
                            var consumer = ClassDesc.of("java.util.function.Consumer");
                            out.invokestatic(ClassDesc.of("java.lang.System"), "getProperties",
                                MethodTypeDesc.of(ClassDesc.of("java.util.Properties")));
                            out.ldc("obsidian.ominous.shotSeed");
                            out.invokevirtual(ClassDesc.of("java.util.Properties"), "get",
                                MethodTypeDesc.of(CD_Object, CD_Object));
                            out.astore(1);
                            var absent = out.newLabel();
                            out.aload(1).ifnull(absent);
                            out.aload(1).checkcast(consumer).aload(0);
                            out.invokeinterface(consumer, "accept", MethodTypeDesc.of(CD_void, CD_Object));
                            out.labelBinding(absent).return_();
                        }));
                }
            });
            if (hooks[0] != 1)
                throw new IllegalStateException("Expected one projectile shoot seed hook, found " + hooks[0]);

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
}
