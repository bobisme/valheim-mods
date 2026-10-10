using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace Shieldwall
{
    // The saga of a siege: who slew how many (players, companions, each stave), the stone's closest call and what each warshard was
    // for, told at its end in the game's own rune-stone window to everyone near, and kept on the stone to be read again.
    internal static class Saga
    {
        private static readonly AccessTools.FieldRef<Character,HitData> LastHit=AccessTools.FieldRefAccess<Character,HitData>("m_lastHit");
        internal const string StaveMark="stave:",Wilds="~";

        // Who struck the killing blow (on the game that owns the raider): a stave, a player, a companion or tame, else the wilds.
        internal static string Killer(Character dead,Planted stave)
        {
            if(stave!=null)return StaveMark+Clean(stave.Title);
            Character attacker=LastHit(dead)?.GetAttacker();
            if(attacker is Player player)return Clean(player.GetPlayerName());
            if(attacker!=null&&(attacker.IsTamed()||attacker.GetFaction()==Character.Faction.Players))return Clean(Localization.instance.Localize(attacker.m_name));
            return Wilds;
        }
        private static string Clean(string name)=>new string((name??"").Where(c=>c!='='&&c!=';').ToArray()).Trim();

        // ---- the tally, kept on the stone by its owner ----
        internal static Dictionary<string,int> Read(string saved)
        {
            var tally=new Dictionary<string,int>();
            foreach(string part in (saved??"").Split(new[]{';'},StringSplitOptions.RemoveEmptyEntries))
            {
                int eq=part.LastIndexOf('=');
                if(eq>0&&int.TryParse(part.Substring(eq+1),out int n))tally[part.Substring(0,eq)]=n;
            }
            return tally;
        }
        private static string Write(Dictionary<string,int> tally)=>string.Join(";",tally.Select(e=>e.Key+"="+e.Value));
        internal static void Count(Warstone stone,long siege,string who)
        {
            ZDO z=stone.Z;
            if(z==null||stone.Phase!=Phase.Battle||stone.Siege!=siege)return;
            var tally=Read(z.GetString(Stone.TallyKey,""));
            tally[who]=(tally.TryGetValue(who,out int n)?n:0)+1;
            z.Set(Stone.TallyKey,Write(tally));
            // Bloodstone: every raider slain mends the stone a little.
            if(stone.Boons.Contains("blood"))Director.OnDamage(stone,-z.GetFloat(Stone.MaxHealthKey,0)*Policy.BloodMend,stone.transform.position);
        }

        // ---- telling it ----
        internal static void Tell(Warstone stone,Director.Outcome outcome,Roster roster,int kills,float health,(string why,int amount)[] parts,int shards)
        {
            ZDO z=stone.Z;
            var plan=Policy.Load(z.GetString(Stone.PlanKey,""));
            int boasts=z.GetInt(Stone.BoastsKey,0),siege=z.GetInt(Stone.HeldKey,0)+z.GetInt(Stone.FallenKey,0);
            var tally=Read(z.GetString(Stone.TallyKey,"")).OrderByDescending(e=>e.Value).ToList();
            string topic=outcome==Director.Outcome.Held?$"The {Ordinal(siege)} siege: the stone held":$"The {Ordinal(siege)} siege: the stone cracked";
            var lines=new List<string>();
            string sworn=boasts!=0?", under boasts of "+And(Policy.Sworn(boasts).Select(b=>b.Name)):"";
            lines.Add($"The {roster.Name} came in {plan.Count} waves, {plan.Sum(w=>w.Count)} strong{sworn}.");
            lines.Add(outcome==Director.Outcome.Held?"It broke against the stone.":"It broke the stone, and melted away.");
            lines.Add("");
            if(tally.Count>0)
                lines.Add($"{kills} slain: "+string.Join(", ",tally.Select(e=>$"{Name(e.Key)} {e.Value}"))+".");
            else lines.Add($"{kills} slain.");
            var staves=tally.Where(e=>e.Key.StartsWith(StaveMark)).ToList();
            if(staves.Count>0)lines.Add($"Mightiest stave: the {Name(staves[0].Key).ToLowerInvariant()}, {staves[0].Value} slain.");
            float low=z.GetFloat(Stone.LowKey,1);
            if(outcome==Director.Outcome.Held)
                lines.Add(low>=0.999f?"No raider laid a blow on the stone.":$"Closest call: the stone fell to {Mathf.Max(1,Mathf.RoundToInt(low*100))}% in wave {Mathf.Max(1,z.GetInt(Stone.LowWaveKey,1))}.");
            lines.Add("");
            var earned=parts.Where(p=>p.amount>0).Select(p=>$"{p.amount} for {p.why}").ToList();
            lines.Add($"Warshards: {shards}"+(earned.Count>0?" ("+string.Join(", ",earned)+")":"")+".");
            string text=string.Join("\n",lines);
            z.Set(Stone.SagaKey,text);z.Set(Stone.SagaTopicKey,topic);
            Net.Saga(topic,text,stone.transform.position,120);
            Plugin.Log($"Saga: {topic} | {text.Replace("\n"," | ")}");
        }
        private static string Name(string key)=>key==Wilds?"the wilds":key.StartsWith(StaveMark)?key.Substring(StaveMark.Length):key;
        private static string And(IEnumerable<string> items)
        {
            var list=items.ToList();
            return list.Count<=1?string.Join("",list):string.Join(", ",list.Take(list.Count-1))+" and "+list[list.Count-1];
        }
        private static string Ordinal(int n)
        {
            string[] words={"first","second","third","fourth","fifth","sixth","seventh","eighth","ninth","tenth","eleventh","twelfth"};
            if(n>=1&&n<=words.Length)return words[n-1];
            int teen=n%100;string suffix=teen>=11&&teen<=13?"th":(n%10)switch{1=>"st",2=>"nd",3=>"rd",_=>"th"};
            return n+suffix;
        }
        // In the game's own rune-stone window (Use or Escape closes it).
        internal static void Show(string topic,string text)
        {
            if(TextViewer.instance==null||Player.m_localPlayer==null||string.IsNullOrEmpty(text))return;
            TextViewer.instance.ShowText(TextViewer.Style.Rune,topic,text,false);
        }
    }
}
