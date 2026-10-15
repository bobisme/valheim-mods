using System.Text;
using UnityEngine;

namespace Factorheim
{
    // One lane of items riding 1 m of belt: positions 0 (start) .. 1 (end), front item first, at least Gap apart. Fixed arrays (no
    // allocation while it runs); the pictures (ItemView) sit at the same index as their items.
    internal sealed class Lane
    {
        internal const int Max=4;
        internal const float Gap=0.25f;
        internal readonly string[] Item=new string[Max];
        internal readonly float[] Pos=new float[Max];
        internal readonly GameObject[] View=new GameObject[Max];
        internal int Count;

        internal bool AtEnd=>Count>0&&Pos[0]>=0.999f;

        // Every item moves d metres toward the end, stopping behind the one ahead of it (or at the end).
        internal void Move(float d)
        {
            for(int i=0;i<Count;i++)
            {
                float limit=i==0?1f:Pos[i-1]-Gap;
                float to=Mathf.Min(Pos[i]+d,limit);
                if(to>Pos[i])Pos[i]=to;   // (never backwards: an item put in close behind another just waits)
            }
        }

        internal bool CanInsert(float p)
        {
            if(Count>=Max)return false;
            for(int i=0;i<Count;i++)if(Mathf.Abs(Pos[i]-p)<Gap*0.9f)return false;
            return true;
        }
        internal bool Insert(string item,float p)
        {
            p=Mathf.Clamp01(p);
            if(!CanInsert(p))return false;
            int at=0;
            while(at<Count&&Pos[at]>p)at++;
            for(int i=Count;i>at;i--){Item[i]=Item[i-1];Pos[i]=Pos[i-1];View[i]=View[i-1];}
            Item[at]=item;Pos[at]=p;View[at]=null;Count++;
            return true;
        }
        // Takes item i off; its picture goes back to the pool.
        internal string RemoveAt(int i)
        {
            string item=Item[i];
            ItemView.Give(View[i]);
            for(int j=i;j<Count-1;j++){Item[j]=Item[j+1];Pos[j]=Pos[j+1];View[j]=View[j+1];}
            Count--;Item[Count]=null;View[Count]=null;
            return item;
        }
        // The item nearest p (within reach) that is wanted, or -1.
        internal int Nearest(float p,float reach,System.Func<string,bool> wants)
        {
            int best=-1;float bd=reach;
            for(int i=0;i<Count;i++)
            {
                float d=Mathf.Abs(Pos[i]-p);
                if(d<=bd&&(wants==null||wants(Item[i]))){best=i;bd=d;}
            }
            return best;
        }

        internal void HideAll(){for(int i=0;i<Count;i++){ItemView.Give(View[i]);View[i]=null;}}
        internal void Forget(){for(int i=0;i<Count;i++)View[i]=null;}   // (the pictures die with their belt)

        // "Prefab@pos" joined by ';' (pos in hundredths): what goes in the ZDO.
        internal void Write(StringBuilder sb)
        {
            for(int i=0;i<Count;i++){if(i>0)sb.Append(';');sb.Append(Item[i]).Append('@').Append(Mathf.RoundToInt(Pos[i]*100));}
        }
        internal void Read(string s)
        {
            HideAll();Count=0;
            if(string.IsNullOrEmpty(s))return;
            foreach(string part in s.Split(';'))
            {
                int at=part.LastIndexOf('@');
                if(at<=0||Count>=Max||!int.TryParse(part.Substring(at+1),out int n))continue;
                Item[Count]=part.Substring(0,at);Pos[Count]=n/100f;View[Count]=null;Count++;
            }
        }
    }
}
