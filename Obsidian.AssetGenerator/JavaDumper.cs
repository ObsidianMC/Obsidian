namespace Obsidian.AssetGenerator;

/// <summary>
/// Runs <c>Java/VanillaDumper.java</c> against a vanilla server, which writes the assets the data generators don't
/// report: per block state light, physics, transforms, map colors and wall shape covers, entity data, and the vanilla
/// types packets write (<c>packet_types.json</c>). It also writes <see cref="PacketFieldsPath"/> into the server's
/// scratch directory for <see cref="Datagen.DatagenAssets"/>.
/// </summary>
/// <remarks>
/// The dumper runs from source (java's source launcher) with the server on its class path, and finds vanilla's classes
/// and members by name through reflection (the jars aren't obfuscated since 26.1), so it needs no build step.
/// </remarks>
internal static class JavaDumper
{
    private static readonly string SourcePath = Path.Combine(AppContext.BaseDirectory, "Java", "VanillaDumper.java");

    /// <summary>
    /// The fields of each packet's class and how vanilla writes them, as
    /// <c>direction → packet id → [{ name, type, sent, encoding, conditional, repeated, packed }]</c>, which are added to
    /// <c>packets.json</c>.
    /// </summary>
    public static string PacketFieldsPath(VanillaServer server) =>
        Path.Combine(server.ScratchDirectory, "packet_fields.json");

    public static Task RunAsync(VanillaServer server, string outputDirectory) =>
        VanillaServer.RunJavaAsync(server.ScratchDirectory,
            "-cp", string.Join(Path.PathSeparator, server.ClassPath),
            SourcePath, outputDirectory, server.ScratchDirectory);
}
