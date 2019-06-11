using System;

//https://wiki.vg/Protocol#Data_types
public static class DataReader
{
    public static int GetVarintLength(this int val)
    {
        int amount = 0;
        do
        {
            var temp = (sbyte)(val & 0b01111111);
            // Note: >>> means that the sign bit is shifted with the rest of the number rather than being left alone
            val >>= 7;
            if (val != 0)
            {
                temp |= 127;
            }
            amount++;
        } while (val != 0);
        return amount;
    }
}