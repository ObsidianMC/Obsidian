using SharpNoise.Modules;


namespace Obsidian.WorldData.Generators.Overworld.Carvers
{
    public class CavesCarver : BaseCarver
    {
        public CavesCarver(OverworldTerrainSettings ots) : base(ots)
        {
            this.Result =  new ScalePoint
                {
                    XScale = 1 / 1024.0,
                    YScale = 1 / 512.0,
                    ZScale = 1 / 1024.0,
                    Source0 = new Turbulence
                    {
                        Frequency = 33.1415,
                        Roughness = 2,
                        Power = 0.3654321,
                        Source0 = new RidgedMulti
                        {
                            Seed = ots.Seed,
                            Frequency = 3.334,
                            OctaveCount = 8,
                            Lacunarity = 1.33444
                        }
                    }

            };
        }
    }
}
