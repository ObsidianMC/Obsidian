using Newtonsoft.Json;
using Obsidian.Chat;
using Obsidian.Entities;
using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;

//https://wiki.vg/Protocol#Data_types
public static class DataReader
{
    public static int GetVarintLength(this int val)
    {
        int amount = 0;
        int value = val;
        do
        {
            var temp = (sbyte)(value & 0b01111111);
            // Note: >>> means that the sign bit is shifted with the rest of the number rather than being left alone
            value >>= 7;
            if (value != 0)
            {
                temp |= 127;
            }
            amount++;
        } while (value != 0);
        return amount;
    }
}