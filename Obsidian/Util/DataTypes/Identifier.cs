using System.Linq;
using System.Text.RegularExpressions;

// https://wiki.vg/Protocol#Identifier
namespace Obsidian.Util.DataTypes
{
    public class Identifier
    {
        public string Namespace;
        public string Id;

        internal Identifier()
        {

        }

        public static Identifier FromString(string id)
        {
            var isidformat = new Regex("^[A-Za-z0-9 -_]:.+");

            string ns = "minecraft";
            string idf;
            if (isidformat.IsMatch(id))
            {
                var split = id.Split(':').ToList(); // im lazy and this is easier
                ns = split[0];
                split.RemoveAt(0);
                idf = string.Join(':', split);
            }
            else
            {
                idf = id;
            }
            // Checks whether given ID matches namespace:identifier format
            var idfier = new Identifier()
            {
                Namespace = ns,
                Id = idf
            };

            return idfier;
        }

        public override string ToString()
        {
            return $"{Namespace}:{Id}";
        }
    }
}
