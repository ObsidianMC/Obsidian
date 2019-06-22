using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Obsidian.BlockData;
using Obsidian.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Obsidian.Util.Registry
{
    public class BlockRegistry
    {
        public static Dictionary<Materials, Block> BLOCK_STATES = new Dictionary<Materials, Block>();

        private static readonly Logger Logger = new Logger("Registry", LogLevel.Debug);

        public static async Task RegisterAll()
        {
            var file = new FileInfo("blocks.json");

            if (file == null)
            {
                await Logger.LogErrorAsync("file is null");
                return;
            }
            if (file.Exists)
            {
                var json = "";
                using (var fs = file.OpenRead())
                {
                    using (var read = new StreamReader(fs, new UTF8Encoding(false)))
                    {
                        json = await read.ReadToEndAsync();
                    }
                }

                var type = JObject.Parse(json);

                int registered = 0;

                using (var enumerator = type.GetEnumerator())
                {
                    while (enumerator.MoveNext())
                    {
                        var (name, token) = enumerator.Current;

                        var blockName = name.Split(":")[1];

                        var states = token.ToObject<BlockJson>();

                        if (Enum.TryParse(typeof(Materials), blockName.Replace("_", ""), true, out var result))
                        {
                            var material = (Materials)result;
                            int id = states.States.FirstOrDefault().Id;
                            await Logger.LogWarningAsync($"Registered block: {material.ToString()} with id: {id.ToString()}");

                            switch (material)
                            {
                                case Materials.Air:
                                    BLOCK_STATES.Add(material, new BlockAir());
                                    break;
                                case Materials.Stone:
                                    BLOCK_STATES.Add(material, new Block(blockName, id));
                                    break;
                                case Materials.Granite:
                                    BLOCK_STATES.Add(material, new Block(blockName, id));
                                    break;
                                case Materials.PolishedGranite:
                                    BLOCK_STATES.Add(material, new Block(blockName, id));
                                    break;
                                case Materials.Diorite:
                                    BLOCK_STATES.Add(material, new Block(blockName, id));
                                    break;
                                case Materials.PolishedDiorite:
                                    BLOCK_STATES.Add(material, new Block(blockName, id));
                                    break;
                                case Materials.Andesite:
                                    BLOCK_STATES.Add(material, new Block(blockName, id));
                                    break;
                                case Materials.PolishedAndesite:
                                    BLOCK_STATES.Add(material, new Block(blockName, id));
                                    break;
                                case Materials.GrassBlock:
                                    BLOCK_STATES.Add(material, new BlockGrass(blockName, id));
                                    break;
                                case Materials.Dirt:
                                    BLOCK_STATES.Add(material, new Block(blockName, id));
                                    break;
                                case Materials.CoarseDirt:
                                    BLOCK_STATES.Add(material, new Block(blockName, id));
                                    break;
                                case Materials.Podzol:
                                    BLOCK_STATES.Add(material, new BlockDirtSnow());
                                    break;
                                case Materials.Cobblestone:
                                    BLOCK_STATES.Add(material, new Block(blockName, id));
                                    break;
                                case Materials.OakPlanks:
                                    BLOCK_STATES.Add(material, new Block(blockName, id));
                                    break;
                                case Materials.SprucePlanks:
                                    BLOCK_STATES.Add(material, new Block(blockName, id));
                                    break;
                                case Materials.BirchPlanks:
                                    BLOCK_STATES.Add(material, new Block(blockName, id));
                                    break;
                                case Materials.JunglePlanks:
                                    BLOCK_STATES.Add(material, new Block(blockName, id));
                                    break;
                                case Materials.AcaciaPlanks:
                                    BLOCK_STATES.Add(material, new Block(blockName, id));
                                    break;
                                case Materials.DarkOakPlanks:
                                    BLOCK_STATES.Add(material, new Block(blockName, id));
                                    break;
                                case Materials.OakSapling:
                                    BLOCK_STATES.Add(material, new BlockSapling(blockName, id));
                                    break;
                                case Materials.SpruceSapling:
                                    BLOCK_STATES.Add(material, new BlockSapling(blockName, id));
                                    break;
                                case Materials.BirchSapling:
                                    BLOCK_STATES.Add(material, new BlockSapling(blockName, id));
                                    break;
                                case Materials.JungleSapling:
                                    BLOCK_STATES.Add(material, new BlockSapling(blockName, id));
                                    break;
                                case Materials.AcaciaSapling:
                                    BLOCK_STATES.Add(material, new BlockSapling(blockName, id));
                                    break;
                                case Materials.DarkOakSapling:
                                    BLOCK_STATES.Add(material, new BlockSapling(blockName, id));
                                    break;
                                case Materials.Bedrock:
                                    BLOCK_STATES.Add(material, new Block(blockName, id));
                                    break;
                                default:
                                    break;
                            }
                            registered++;
                        }
                    }
                }

                await Logger.LogDebugAsync($"Successfully registered {registered} blocks..");
            }
        }

        public static async Task<Block> RegisterAsync(Block block)
        {
            //BLOCK_STATES.Add(block);
            //await Logger.LogDebugAsync($"Registered: {block.UnlocalizedName} with id {block.Id}");
            return block;
        }

        public static async Task<Block> RegisterAsync(string name, int id)
        {
            var block = new Block(name, id);
           //BLOCK_STATES.Add(block);
            //await Logger.LogDebugAsync($"Registered: {name} with id {id}");
            return block;
        }

        public static Block G(Materials mat)
        {
            if (BLOCK_STATES.TryGetValue(mat, out Block result))
                return result;

            return null;
        }
    }

    public class BlockJson
    {
        [JsonProperty("states")]
        public BlockStateJson[] States { get; set; }

        [JsonProperty("properties")]
        public BlockPropertiesExtraJson Properties { get; set; }
    }

    public class BlockStateJson
    {
        [JsonProperty("id")]
        public int Id { get; set; }

        [JsonProperty("default")]
        public bool Default { get; set; }

        [JsonProperty("properties")]
        public BlockPropertiesJson Properties { get; set; }
    }

    public class BlockPropertiesJson
    {
        [JsonProperty("stage")]
        public int Stage { get; set; }

        [JsonProperty("snowy")]
        public bool Snowy { get; set; }
    }

    public class BlockPropertiesExtraJson
    {
        [JsonProperty("snowy")]
        public bool[] Snowy { get; set; }
    }
}
