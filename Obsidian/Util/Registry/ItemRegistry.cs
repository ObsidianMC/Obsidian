using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Obsidian.Blocks;
using Obsidian.Items;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace Obsidian.Util.Registry
{
    public class ItemRegistry
    {
        public static Dictionary<Materials, Item> Items = new Dictionary<Materials, Item>();

        public static async Task RegisterAll()
        {
            var file = new FileInfo("Assets/items.json");

            if (file.Exists)
            {
                using var fs = file.OpenRead();
                using var read = new StreamReader(fs, new UTF8Encoding(false));

                var json = await read.ReadToEndAsync();

                var type = JObject.Parse(json);

                using var enumerator = type.GetEnumerator();
                int registered = 0;

                while (enumerator.MoveNext())
                {
                    var (name, token) = enumerator.Current;

                    var itemName = name.Split(":")[1];

                    var item = JsonConvert.DeserializeObject<ItemJson>(token.ToString());

                    if (!Enum.TryParse(itemName.Replace("_", ""), true, out Materials material)) continue;

                    await Program.RegistryLogger.LogDebugAsync($"Registered item: {material} with id: {item.ProtocolId}");

                    Items.Add(material, new Item { Id = item.ProtocolId, Name = itemName });
                    registered++;

                    
                }

                await Program.RegistryLogger.LogDebugAsync($"Successfully registered {registered} items..");
            }

        }
    }

    public class ItemJson
    {
        [JsonProperty("protocol_id")]
        public int ProtocolId { get; set; }
    }
}
