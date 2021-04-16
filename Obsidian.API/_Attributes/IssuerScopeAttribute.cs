using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Obsidian.API
{

    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
    public class IssuerScopeAttribute  : Attribute
    {
        

        public IssuerScopeAttribute(CommandIssuer issuers)
        {
            Issuers = issuers;
        }

        public CommandIssuer Issuers { get; }
    }
}
