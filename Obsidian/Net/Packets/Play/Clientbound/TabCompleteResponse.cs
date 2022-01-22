using Obsidian.Serialization.Attributes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Play.Clientbound;

public partial class TabCompleteResponse : IClientboundPacket
{
    public TabCompleteResponse(int transactionId, int start, int length, int count, List<MatchItem> matches)
    {
        TransactionId = transactionId;
        Start = start;
        Length = length;
        Count = count;
        Matches = matches;
    }

    [Field(0), VarLength]
    public int TransactionId { get; init; }
    
    [Field(1), VarLength]
    public int Start { get; init; }
    
    [Field(2), VarLength]
    public int Length { get; init; }

    [Field(3), VarLength]
    public int Count { get; init; }
    
    [Field(4)]
    public List<MatchItem> Matches { get; init; }

    public int Id => 0x11;
    
}
