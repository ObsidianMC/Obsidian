using Obsidian;
using Obsidian.Entities;
using System;
using System.Collections.Generic;
using System.Text;

namespace NbsPlayerPlugin
{
    public class PlayerTask
    {
        public PlayerTask(NbsFile nbs, Client client, int tickStart)
        {
            this.NBS = nbs ?? throw new ArgumentNullException(nameof(nbs));
            this.Client = client ?? throw new ArgumentNullException(nameof(client));
            this.TickStart = tickStart;
            this.Position = 0;
        }

        public Client Client { get; }
        public NbsFile NBS { get; }
        public int Position { get; set; }
        public int TickStart { get; }
    }
}