using System.Collections.Generic;

namespace Obsidian.API.Advancements
{
    public sealed class Advancement
    {
        /// <summary>
        /// The identifier of the advancement.
        /// If a valid identifier is not detected this advancement will register with the obsidian namespace.
        /// </summary>
        public string Identifier { get; init; }

        public string Parent { get; init; }

        public AdvancementDisplay? Display { get; init; }

        public List<Criteria> Criterias { get; init;  }
    }
}
