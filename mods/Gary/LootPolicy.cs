using System;
using System.Collections.Generic;
using System.Globalization;

namespace Gary
{
    internal enum LootState { Unknown, Remaining, Empty }
    internal static class LootPolicy
    {
        internal const int MaxRooms=512,MaxLedger=512;
        internal static LootState Chest(byte[] data,bool initialized)
        {
            // Read only the native inventory header. Loading an Inventory can instantiate items.
            if(!initialized||data==null||data.Length<6||data.Length>1024*1024)return LootState.Unknown;
            int version=Int(data,0),header,count;
            if(version==108||version==109){header=6;count=data[4]|data[5]<<8;}
            else if(version>=101&&version<=107&&data.Length>=8){header=8;count=Int(data,4);}
            else return LootState.Unknown;
            if(count<0||count>4096)return LootState.Unknown;
            if(count==0)return data.Length==header?LootState.Empty:LootState.Unknown;
            return data.Length>header?LootState.Remaining:LootState.Unknown;
        }
        internal static int RoomCount(byte[] data)
        {
            if(data==null||data.Length<4||data.Length>4+MaxRooms*32)return 0;
            int count=Int(data,0);
            return count>0&&count<=MaxRooms&&data.Length==4+count*32?count:0;
        }
        private static int Int(byte[] b,int i) => b[i]|b[i+1]<<8|b[i+2]<<16|b[i+3]<<24;
        internal static LootState Merge(LootState a,LootState b) => a==LootState.Remaining||b==LootState.Remaining?LootState.Remaining:
            a==LootState.Unknown||b==LootState.Unknown?LootState.Unknown:LootState.Empty;
        internal static bool ValidId(string id)
        {
            if(string.IsNullOrEmpty(id)||id.Length>25)return false;
            string[] p=id.Split(',');
            return p.Length==2&&int.TryParse(p[0],NumberStyles.AllowLeadingSign,CultureInfo.InvariantCulture,out int x)&&
                int.TryParse(p[1],NumberStyles.AllowLeadingSign,CultureInfo.InvariantCulture,out int z)&&Math.Abs((long)x)<=1000000&&Math.Abs((long)z)<=1000000;
        }
        internal static List<string> Ledger(string text)
        {
            var list=new List<string>();
            if(text==null||text.Length>16384)return list;
            foreach(string id in text.Split(';'))if(ValidId(id)&&!list.Contains(id))
            {list.Add(id);if(list.Count>MaxLedger)list.RemoveAt(0);}
            return list;
        }
        internal static bool Update(List<string> list,string id,LootState state)
        {
            if(!ValidId(id)||state==LootState.Unknown)return false;
            if(state==LootState.Remaining)return list.Remove(id);
            if(list.Contains(id))return false;
            list.Add(id);while(list.Count>MaxLedger)list.RemoveAt(0);return true;
        }
    }
}
