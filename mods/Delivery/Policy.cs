using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Delivery
{
    // A note on the horn: which of its five notes (a goat horn's finger-holes: 2 is the lowest, 6 the highest), and whether it is held.
    public readonly struct Note
    {
        public readonly int Partial;
        public readonly bool Long;
        public Note(int partial,bool held){Partial=partial;Long=held;}
        public override string ToString()=>Partial.ToString(CultureInfo.InvariantCulture)+(Long?"L":"S");
    }

    // What the courier is to you: how well he thinks of you (0 to 1, tips raise it), what he's called and how he looks (a seed),
    // and how often he's run for you.
    public sealed class Courier
    {
        public float Regard=0.5f;
        public int Seed;
        public int Runs,Tipped;
        public string Name=>Policy.Names[(Seed&0x7fffffff)%Policy.Names.Length];
    }

    // The rules: prices, fees, how long he takes, what tips do, each meal's call, and the words he says. No Unity here (tested alone).
    public static class Policy
    {
        public const int LowPartial=2,HighPartial=6,MaxPortions=10;
        public const double ShortBeat=0.17,LongBeat=0.46,Gap=0.06;

        // "That's all": the call that ends an order (two long low blasts), and what the courier blows back when he's heard you.
        public static readonly Note[] EndCall={new Note(2,true),new Note(2,true)};
        public static readonly Note[] Answer={new Note(3,false),new Note(4,false),new Note(5,true)};
        public static readonly Note[] Arrived={new Note(4,false),new Note(6,true)};

        public static string Key(IEnumerable<Note> tune)=>string.Join(" ",tune.Select(n=>n.ToString()));
        public static Note[] Parse(string key)
        {
            var notes=new List<Note>();
            foreach(string part in (key??"").Split(new[]{' '},StringSplitOptions.RemoveEmptyEntries))
            {
                if(part.Length<2||!int.TryParse(part.Substring(0,part.Length-1),NumberStyles.Integer,CultureInfo.InvariantCulture,out int p))continue;
                if(p<LowPartial||p>HighPartial)continue;
                notes.Add(new Note(p,part[part.Length-1]=='L'));
            }
            return notes.ToArray();
        }
        public static double Length(IEnumerable<Note> tune)=>tune.Sum(n=>(n.Long?LongBeat:ShortBeat)+Gap);

        // Every meal its own call, the same in every game: the meals are taken in name order, and each tries tunes from its own name's hash
        // until one isn't taken (nor the end call or the answer). Calls start on a middle note, as a call does, and have three or four notes.
        public static Dictionary<string,Note[]> Tunes(IEnumerable<string> meals)
        {
            var taken=new HashSet<string>{Key(EndCall),Key(Answer),Key(Arrived)};
            var tunes=new Dictionary<string,Note[]>();
            foreach(string meal in meals.Distinct().OrderBy(m=>m,StringComparer.Ordinal))
            {
                uint seed=Hash(meal);
                for(int attempt=0;;attempt++)
                {
                    Note[] tune=Make(seed+(uint)attempt*2654435761u,attempt>=40);
                    if(taken.Add(Key(tune))){tunes[meal]=tune;break;}
                }
            }
            return tunes;
        }
        private static Note[] Make(uint seed,bool longer)
        {
            var rng=new Random((int)(seed&0x7fffffff));
            int count=longer?4+rng.Next(2):(rng.Next(3)==0?4:3);
            var notes=new Note[count];
            int p=3+rng.Next(2);
            for(int i=0;i<count;i++)
            {
                if(i>0)
                {
                    int step=rng.Next(5)-2; // (down two notes to up two)
                    if(step==0&&notes[i-1].Long)step=1;
                    p=Math.Max(LowPartial,Math.Min(HighPartial,p+step));
                }
                notes[i]=new Note(p,rng.Next(3)==0||i==count-1&&rng.Next(2)==0);
            }
            if(notes.All(n=>n.Partial==notes[0].Partial))notes[count-1]=new Note(notes[0].Partial==HighPartial?HighPartial-2:notes[0].Partial+2,true);
            return notes;
        }
        public static uint Hash(string s)
        {
            uint h=2166136261;
            foreach(char c in s??""){h^=c;h*=16777619;}
            return h;
        }

        // A portion's price in coins: what it fills (health, stamina and eitr together), a coin for every six, two at the least.
        public static int Price(float health,float stamina,float eitr)=>Math.Max(2,(int)Math.Ceiling((health+stamina+eitr)/6f));

        // The run's fee: five coins in the meadows, more where the way is dangerous, half again by night.
        public static int Fee(string biome,bool night)
        {
            int fee;
            switch(biome)
            {
                case "Meadows":fee=5;break;
                case "BlackForest":fee=10;break;
                case "Ocean":fee=12;break;
                case "Swamp":fee=15;break;
                case "Mountain":fee=20;break;
                case "Plains":fee=25;break;
                case "Mistlands":fee=30;break;
                case "AshLands":case "DeepNorth":fee=45;break;
                default:fee=10;break;
            }
            return night?(int)Math.Ceiling(fee*1.5):fee;
        }
        public static string BiomeName(string biome)
        {
            switch(biome)
            {
                case "BlackForest":return "Black Forest";
                case "AshLands":return "Ashlands";
                case "DeepNorth":return "Deep North";
                default:return biome;
            }
        }

        // How long before he sets out, in seconds: the base at middling regard, half again as long when he thinks little of you,
        // half as long when he'd run through fire for you.
        public static float Wait(float baseSeconds,float regard)=>baseSeconds*(1.5f-Clamp01(regard));
        // He runs a little faster for those he likes.
        public static float RunSpeed(float regard)=>6.5f+2f*Clamp01(regard);

        // What a tip does to his regard: generosity is measured against what the order cost. No tip at all costs a little.
        public static float AfterTip(float regard,int tip,int total)
        {
            float change=tip<=0?-0.04f:Math.Min(0.25f,0.8f*tip/Math.Max(1,total));
            return Clamp01(regard+change);
        }
        // Sending him away without paying (not enough coins when he gets there).
        public static float AfterStiffed(float regard)=>Clamp01(regard-0.15f);
        public static string RegardName(float regard)=>regard<0.2f?"Sullen":regard<0.4f?"Grudging":regard<0.6f?"Steady":regard<0.8f?"Eager":"Devoted";
        // Tips to offer: about a tenth, a fifth and a half of the order, at least 1, 2 and 5 coins, all different.
        public static int[] TipChoices(int total)
        {
            var tips=new List<int>();
            foreach(var (share,least) in new[]{(0.1,1),(0.2,2),(0.5,5)})
            {
                int t=Math.Max(least,(int)Math.Round(total*share));
                while(tips.Contains(t))t++;
                tips.Add(t);
            }
            return tips.ToArray();
        }
        // A devoted courier sometimes throws in a little extra.
        public static bool Extra(float regard,double roll)=>regard>=0.8f&&roll<0.2;

        public static string Save(Courier c)=>string.Join("|","1",c.Regard.ToString("0.###",CultureInfo.InvariantCulture),c.Seed.ToString(CultureInfo.InvariantCulture),c.Runs.ToString(CultureInfo.InvariantCulture),c.Tipped.ToString(CultureInfo.InvariantCulture));
        public static Courier Load(string text,int newSeed)
        {
            string[] p=(text??"").Split('|');
            if(p.Length<5||p[0]!="1")return new Courier{Seed=newSeed};
            var c=new Courier();
            c.Regard=float.TryParse(p[1],NumberStyles.Float,CultureInfo.InvariantCulture,out float r)?Clamp01(r):0.5f;
            c.Seed=int.TryParse(p[2],NumberStyles.Integer,CultureInfo.InvariantCulture,out int s)?s:newSeed;
            c.Runs=int.TryParse(p[3],NumberStyles.Integer,CultureInfo.InvariantCulture,out int n)?Math.Max(0,n):0;
            c.Tipped=int.TryParse(p[4],NumberStyles.Integer,CultureInfo.InvariantCulture,out int t)?Math.Max(0,t):0;
            return c;
        }

        // Where he comes from, as a word: the direction from you to him.
        public static string Compass(double dx,double dz)
        {
            string[] names={"north","north-east","east","south-east","south","south-west","west","north-west"};
            double a=Math.Atan2(dx,dz)*180/Math.PI;
            if(a<0)a+=360;
            return names[(int)Math.Round(a/45)%8];
        }
        public static string Order(IEnumerable<KeyValuePair<string,int>> lines)
        {
            var sb=new StringBuilder();
            foreach(var kv in lines){if(sb.Length>0)sb.Append(", ");sb.Append(kv.Value).Append("× ").Append(kv.Key);}
            return sb.ToString();
        }

        public static readonly string[] Names={"Hrolf","Bjarni","Ketil","Ulf","Grim","Orm","Thorvald","Hallbjorn","Eyvind","Skeggi","Toki","Vigfus","Atli","Bard","Hauk","Svein"};

        // What he says. {0} is your name.
        public static string Greeting(float regard,int seed)=>Pick(regard<0.2f?Sullen:regard<0.5f?Plain:regard<0.8f?Warm:Devoted,seed);
        private static readonly string[] Sullen={"Here. Your food. Again.","Ran all this way, I did. As usual.","Hot, mostly. Don't complain."};
        private static readonly string[] Plain={"Hot from the hearth, {0}!","Delivery! Mind, it's hot.","One order, as called."};
        private static readonly string[] Warm={"Ran the whole way, {0}!","Still steaming! Eat it while it is.","Your call carried far today, {0}."};
        private static readonly string[] Devoted={"For you, {0}, I'd outrun a troll!","Fastest legs in the tenth world, at your service!","Heard your horn and dropped everything, {0}!"};
        public static string Thanks(int tip,int total,int seed)
        {
            if(tip<=0)return Pick(new[]{"...Right. Bye, then.","No tip? The Meadows are wide, you know.","Hmph. Next time I might stop for berries."},seed);
            if(tip*5>=total*2)return Pick(new[]{"By Odin's eye! I'll fly next time!","Generous! Call and I'll be there in a blink.","You're my favourite, {0}. Don't tell the others."},seed);
            return Pick(new[]{"Thanks, {0}!","Much obliged. Call again!","Kind of you. My legs thank you."},seed);
        }
        public static readonly string[] Stiffed={"No coin, no food! Back it goes.","You can't pay? I'm eating this myself on the way back.","I don't run for free, {0}."};
        public static readonly string[] Leaving={"Off I go!","Call again!","Mind the greylings!"};
        public static string Pick(string[] lines,int seed)=>lines[(seed&0x7fffffff)%lines.Length];

        private static float Clamp01(float v)=>v<0?0:v>1?1:v;
    }
}
