using System.Collections.Generic;
using UnityEngine;

namespace Gary
{
    internal static class PlayerCollision
    {
        private sealed class Pair
        {internal Collider Gary,Other;internal bool Original;}
        private static readonly List<Pair> Pairs=new List<Pair>();
        // Quad clones Player but replaces the Player component with Humanoid. Use his native saved
        // prefab identity, so compatibility remains optional and survives either mod's F6 reload.
        private static readonly int QuadCompanionPrefab="dhack_companion".GetStableHashCode();
        private static bool WalkThrough(Character character) => character!=null&&
            (character is Player||Companion.Data(character)?.GetPrefab()==QuadCompanionPrefab);
        internal static void Scan()
        {
            for(int i=Pairs.Count-1;i>=0;i--)
            {
                Pair pair=Pairs[i];
                if(pair.Gary==null||pair.Other==null){Pairs.RemoveAt(i);continue;}
                Character c=pair.Gary.GetComponent<Character>();
                if(c==null||!Companion.Is(c)||!WalkThrough(pair.Other.GetComponent<Character>()))
                {Physics.IgnoreCollision(pair.Gary,pair.Other,pair.Original);Pairs.RemoveAt(i);}
            }
            foreach(Companion.State st in Companion.States.Values)
            {
                if(st.Body==null||!Companion.Is(st.Body))continue;
                Collider gary=st.Body.GetCollider();if(gary==null)continue;
                foreach(Character character in Character.GetAllCharacters())
                {
                    if(!WalkThrough(character))continue;
                    Collider body=character.GetCollider();if(body==null)continue;
                    bool recorded=false;
                    foreach(Pair pair in Pairs)if(pair.Gary==gary&&pair.Other==body){recorded=true;break;}
                    if(!recorded)Pairs.Add(new Pair{Gary=gary,Other=body,Original=Physics.GetIgnoreCollision(gary,body)});
                    // Unity can discard pair ignores when a collider is disabled during teleport/respawn.
                    Physics.IgnoreCollision(gary,body,true);
                }
            }
        }
        internal static void Clear()
        {
            foreach(Pair pair in Pairs)if(pair.Gary!=null&&pair.Other!=null)Physics.IgnoreCollision(pair.Gary,pair.Other,pair.Original);
            Pairs.Clear();
        }
    }
}
