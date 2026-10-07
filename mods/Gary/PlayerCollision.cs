using System.Collections.Generic;
using UnityEngine;

namespace Gary
{
    internal static class PlayerCollision
    {
        private sealed class Pair
        {internal Collider Gary,Player;internal bool Original;}
        private static readonly List<Pair> Pairs=new List<Pair>();
        internal static void Scan()
        {
            for(int i=Pairs.Count-1;i>=0;i--)
            {
                Pair pair=Pairs[i];
                if(pair.Gary==null||pair.Player==null){Pairs.RemoveAt(i);continue;}
                Character c=pair.Gary.GetComponent<Character>();
                if(c==null||!Companion.Is(c))
                {Physics.IgnoreCollision(pair.Gary,pair.Player,pair.Original);Pairs.RemoveAt(i);}
            }
            foreach(Companion.State st in Companion.States.Values)
            {
                if(st.Body==null||!Companion.Is(st.Body))continue;
                Collider gary=st.Body.GetCollider();if(gary==null)continue;
                foreach(Player player in Player.GetAllPlayers())
                {
                    if(player==null)continue;
                    Collider body=player.GetCollider();if(body==null)continue;
                    bool recorded=false;
                    foreach(Pair pair in Pairs)if(pair.Gary==gary&&pair.Player==body){recorded=true;break;}
                    if(!recorded)Pairs.Add(new Pair{Gary=gary,Player=body,Original=Physics.GetIgnoreCollision(gary,body)});
                    // Unity can discard pair ignores when a collider is disabled during teleport/respawn.
                    Physics.IgnoreCollision(gary,body,true);
                }
            }
        }
        internal static void Clear()
        {
            foreach(Pair pair in Pairs)if(pair.Gary!=null&&pair.Player!=null)Physics.IgnoreCollision(pair.Gary,pair.Player,pair.Original);
            Pairs.Clear();
        }
    }
}
