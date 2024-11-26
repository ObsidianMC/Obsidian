using Obsidian.SourceGenerators.Registry.Models;

namespace Obsidian.SourceGenerators.Registry;
public partial class RegistryAssetsGenerator
{
    private static void GenerateSounds(Assets assets, SourceProductionContext context)
    {
        //if (!Debugger.IsAttached)
        //    Debugger.Launch();

        var builder = new CodeBuilder();
        builder.Namespace("Obsidian.API");
        builder.Line();

        builder.Type("public static class SoundId");

        foreach (var kv in assets.Sounds)
        {
            var parentName = kv.Key;
            var sounds = kv.Value;
            builder.Type($"public static class {parentName.ToPascalCase()}");

            foreach (var sound in sounds)
            {
                var actualName = string.Join("_", sound.Name.Split('.').Skip(1));

                if (actualName == "13")
                    actualName = "thirteen";
                else if (actualName == "11")
                    actualName = "eleven";
                else if (actualName == "5")
                    actualName = "five";
                else if (actualName == string.Empty)
                    actualName = "i_dont_know_what_my_use_is";//WHY MOJANG????????????????????????

                builder.Line($"public const string {actualName.ToPascalCase()} = \"{sound.Name}\";");
            }

            builder.EndScope();
        }

        builder.EndScope();

        context.AddSource("SoundId.g.cs", builder.ToString());
    }
}
