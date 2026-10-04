namespace Obsidian.AssetGenerator;

/// <summary>
/// Runs <c>Java/VanillaDumper.java</c> against a vanilla server, which writes the assets the data generators don't
/// report: per block state light, physics, transforms, map colors and wall shape covers, and entity data. It also writes
/// <see cref="WorldgenGroupsPath"/> and <see cref="PacketFieldsPath"/> into the server's directory for
/// <see cref="Datagen.DatagenAssets"/>.
/// </summary>
/// <remarks>
/// The dumper runs from source (java's source launcher) with the server on its class path, and finds vanilla's classes
/// and members by their Mojang names through the server's mappings, so it needs no build step or remapping tool.
/// </remarks>
internal static class JavaDumper
{
    private static readonly string SourcePath = Path.Combine(AppContext.BaseDirectory, "Java", "VanillaDumper.java");

    /// <summary>
    /// The vanilla class (e.g. <c>TreeFeatures</c>, <c>TreePlacements</c>) that registers each configured and placed
    /// feature, which the worldgen assets are grouped by.
    /// </summary>
    public static string WorldgenGroupsPath(VanillaServer server) => Path.Combine(server.Directory, "worldgen_groups.json");

    /// <summary>
    /// The fields of each packet's class and how vanilla writes them, as
    /// <c>direction → packet id → [{ name, type, sent, encoding, conditional, repeated, packed }]</c>, which are added to
    /// <c>packets.json</c>.
    /// </summary>
    public static string PacketFieldsPath(VanillaServer server) => Path.Combine(server.Directory, "packet_fields.json");

    public static Task RunAsync(VanillaServer server, string outputDirectory) =>
        VanillaServer.RunJavaAsync(server.Directory,
            "-cp", string.Join(Path.PathSeparator, server.ClassPath),
            SourcePath, server.MappingsPath, outputDirectory, server.Directory);
}
