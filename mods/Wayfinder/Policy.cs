using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Wayfinder
{
    // Pure rules shared with the standalone tests: no Unity or game types. World x is east, z is north.
    internal static class Policy
    {
        internal const double Fathom=1.83;            // a faðmr: the stretch of a man's arms, how a sounding line is marked
        internal const double LineLength=40;          // fathoms of line on the sounding lead: deeper than that, "no bottom"
        internal const double Draught=1.1;            // metres of keel under a laden longship
        internal const int MaxWaypoints=64;

        // Compass bearing of a direction: 0 north, 90 east, always in [0, 360).
        internal static double Bearing(double dx,double dz)
        {
            if(dx==0&&dz==0)return 0;
            double b=Math.Atan2(dx,dz)*180/Math.PI;
            if(b<0)b+=360;
            return b>=360?b-360:b;
        }
        // The signed turn from one bearing to another, in (-180, 180].
        internal static double Turn(double from,double to)
        {
            double d=(to-from)%360;
            if(d<=-180)d+=360;else if(d>180)d-=360;
            return d;
        }
        private static readonly string[] Points={"N","NNE","NE","ENE","E","ESE","SE","SSE","S","SSW","SW","WSW","W","WNW","NW","NNW"};
        internal static string Point(double bearing)=>Points[(int)Math.Round(Norm(bearing)/22.5)%16];
        private static readonly string[] Winds={"north","north-east","east","south-east","south","south-west","west","north-west"};
        // Where a wind blows FROM, in words, given the direction it blows to.
        internal static string WindFrom(double blowsToward)=>Winds[(int)Math.Round(Norm(blowsToward+180)/45)%8];
        internal static double Norm(double bearing){double b=bearing%360;return b<0?b+360:b;}

        // How much farther a lookout sees: as the horizon does, with the square root of the height of the eye over the sea. The
        // tallest seat gets the whole bonus, lower ones less, none at sea level.
        internal static double MastheadSight(double max,double height,double tallest)=>1+(max-1)*Math.Sqrt(Math.Max(0,Math.Min(1,height/tallest)));
        // How much of the sail's pull a heading gets from the wind, the game's own reckoning (Ship.GetWindAngleFactor and
        // GetSailForce): full with the wind behind or on the beam, 70% close to the beam's edge, nothing into it.
        internal static double SailPower(double windX,double windZ,double intensity,double headingX,double headingZ)
        {
            double wl=Math.Sqrt(windX*windX+windZ*windZ),hl=Math.Sqrt(headingX*headingX+headingZ*headingZ);
            if(wl<1e-9||hl<1e-9)return 0;
            double d=-(windX*headingX+windZ*headingZ)/(wl*hl); // 1: straight into the wind
            double side=0.7+0.3*(1-Math.Abs(d));
            double into=1-LerpStep(0.75,0.8,d);
            return side*into*(0.25+0.75*Clamp01(intensity));
        }
        internal static double LerpStep(double a,double b,double t)=>Clamp01((t-a)/(b-a));
        internal static double Clamp01(double v)=>v<0?0:v>1?1:double.IsNaN(v)?0:v;
        // The wind's angle off the stern for a heading: 0 dead astern, 180 dead ahead.
        internal static double OffStern(double windX,double windZ,double headingX,double headingZ)
        {
            double wl=Math.Sqrt(windX*windX+windZ*windZ),hl=Math.Sqrt(headingX*headingX+headingZ*headingZ);
            if(wl<1e-9||hl<1e-9)return 90;
            double c=(windX*headingX+windZ*headingZ)/(wl*hl);
            return Math.Acos(Math.Max(-1,Math.Min(1,c)))*180/Math.PI;
        }
        internal static string WindWord(double offStern)=>
            offStern<35?"fair wind astern":offStern<80?"good wind on the quarter":offStern<115?"wind on the beam":offStern<140?"wind forward of the beam":"headwind: row, or call on Moder";

        internal static double Fathoms(double metres)=>metres/Fathom;
        internal static string Depth(double waterLevel,double bottom)
        {
            double m=waterLevel-bottom;
            if(m<=0)return "aground";
            double f=Fathoms(m);
            if(f>LineLength)return "no bottom at "+LineLength+" fathoms";
            return f<10?f.ToString("0.0",CultureInfo.InvariantCulture)+" fathoms":Math.Round(f)+" fathoms";
        }
        internal static bool Shoal(double depthMetres)=>depthMetres<Draught+0.9;

        // Liang–Barsky: the part of segment a–b inside the box, or false when none of it is.
        internal static bool Clip(ref double ax,ref double ay,ref double bx,ref double by,double xmin,double ymin,double xmax,double ymax)
        {
            double dx=bx-ax,dy=by-ay,t0=0,t1=1;
            double[] p={-dx,dx,-dy,dy},q={ax-xmin,xmax-ax,ay-ymin,ymax-ay};
            for(int i=0;i<4;i++)
            {
                if(p[i]==0){if(q[i]<0)return false;continue;}
                double t=q[i]/p[i];
                if(p[i]<0){if(t>t1)return false;if(t>t0)t0=t;}
                else{if(t<t0)return false;if(t<t1)t1=t;}
            }
            double x0=ax+t0*dx,y0=ay+t0*dy,x1=ax+t1*dx,y1=ay+t1*dy;
            ax=x0;ay=y0;bx=x1;by=y1;
            return true;
        }

        // Distance from a point to a segment, and where along it (0..1) the nearest point lies.
        internal static double ToSegment(double px,double pz,double ax,double az,double bx,double bz,out double t)
        {
            double dx=bx-ax,dz=bz-az,l=dx*dx+dz*dz;
            t=l<1e-12?0:Math.Max(0,Math.Min(1,((px-ax)*dx+(pz-az)*dz)/l));
            double x=ax+t*dx-px,z=az+t*dz-pz;
            return Math.Sqrt(x*x+z*z);
        }
        // Where a new waypoint clicked at p goes: into the leg it was clicked on (index of the waypoint it goes before), or at the end.
        internal static int InsertAt(IList<(double x,double z)> course,double px,double pz,double nearLeg)
        {
            int best=course.Count;double bestD=nearLeg;
            for(int i=0;i+1<course.Count;i++)
            {
                double d=ToSegment(px,pz,course[i].x,course[i].z,course[i+1].x,course[i+1].z,out double t);
                if(d<bestD&&t>0.02&&t<0.98){bestD=d;best=i+1;}
            }
            return best;
        }
        internal static int Nearest(IList<(double x,double z)> course,double px,double pz,double within)
        {
            int best=-1;double bestD=within;
            for(int i=0;i<course.Count;i++)
            {
                double d=Math.Sqrt((course[i].x-px)*(course[i].x-px)+(course[i].z-pz)*(course[i].z-pz));
                if(d<bestD){bestD=d;best=i;}
            }
            return best;
        }
        internal static double Length(IList<(double x,double z)> course,int from)
        {
            double sum=0;
            for(int i=Math.Max(0,from);i+1<course.Count;i++)
                sum+=Math.Sqrt((course[i+1].x-course[i].x)*(course[i+1].x-course[i].x)+(course[i+1].z-course[i].z)*(course[i+1].z-course[i].z));
            return sum;
        }
        // The next waypoint after the traveller at p: past any within the arrival radius, the last such one counts (they may have
        // cut a corner and reached a later one first).
        internal static int Advance(IList<(double x,double z)> course,int next,double px,double pz,double radius)
        {
            int reached=-1;
            for(int i=Math.Max(0,next);i<course.Count;i++)
            {
                double dx=course[i].x-px,dz=course[i].z-pz;
                if(dx*dx+dz*dz<=radius*radius)reached=i;
            }
            return reached>=0?reached+1:next;
        }
        internal static string Distance(double m)=>m<1000?Math.Round(m/10)*10+" m":(m/1000).ToString(m<10000?"0.0":"0",CultureInfo.InvariantCulture)+" km";
        internal static string Duration(double seconds)
        {
            if(double.IsNaN(seconds)||double.IsInfinity(seconds)||seconds<0)return "—";
            int s=(int)Math.Round(seconds);
            return s<60?s+" s":s<3600?(s/60)+":"+(s%60).ToString("00"):(s/3600)+" h "+(s/60%60).ToString("00");
        }

        // The course as text, saved with the character and written to cartography tables: "1|next|x,z,name;x,z,name".
        internal static string Save(IList<(double x,double z,string name)> course,int next)
        {
            var sb=new StringBuilder("1|").Append(next).Append('|');
            for(int i=0;i<course.Count;i++)
            {
                if(i>0)sb.Append(';');
                sb.Append(Math.Round(course[i].x,1).ToString(CultureInfo.InvariantCulture)).Append(',')
                  .Append(Math.Round(course[i].z,1).ToString(CultureInfo.InvariantCulture)).Append(',').Append(Clean(course[i].name));
            }
            return sb.ToString();
        }
        // What goes on a ship for the others aboard: from the leg being sailed, the next few waypoints ("" with no course).
        internal const int AheadWaypoints=6;
        internal static string Ahead(IList<(double x,double z,string name)> course,int next)
        {
            if(course==null||course.Count==0)return "";
            int from=Math.Max(0,Math.Min(course.Count-1,next-1));
            // (an unnamed waypoint keeps its number in the whole course)
            return Save(course.Skip(from).Take(AheadWaypoints).Select((w,i)=>(w.x,w.z,string.IsNullOrEmpty(w.name)?"waypoint "+(from+i+1):w.name)).ToList(),next-from);
        }
        internal static string Clean(string name)=>new string((name??"").Where(c=>c!=';'&&c!='|'&&c!=','&&!char.IsControl(c)).Take(40).ToArray()).Trim();
        internal static bool Load(string text,List<(double x,double z,string name)> into,out int next)
        {
            next=0;into.Clear();
            if(string.IsNullOrEmpty(text))return false;
            string[] parts=text.Split('|');
            if(parts.Length!=3||parts[0]!="1"||!int.TryParse(parts[1],NumberStyles.Integer,CultureInfo.InvariantCulture,out next))return false;
            foreach(string w in parts[2].Split(new[]{';'},StringSplitOptions.RemoveEmptyEntries))
            {
                string[] f=w.Split(',');
                if(f.Length<2||!double.TryParse(f[0],NumberStyles.Float,CultureInfo.InvariantCulture,out double x)||!double.TryParse(f[1],NumberStyles.Float,CultureInfo.InvariantCulture,out double z))continue;
                if(double.IsNaN(x)||double.IsNaN(z)||Math.Abs(x)>12000||Math.Abs(z)>12000)continue;
                into.Add((x,z,f.Length>2?Clean(f[2]):""));
                if(into.Count>=MaxWaypoints)break;
            }
            next=Math.Max(0,Math.Min(next,into.Count));
            return true;
        }

        // The sky, for a sun-compass: by day the sun's shadow, or through cloud the sunstone's glint; by night the lodestar.
        internal enum Sky{Sun,Sunstone,Star,Hidden,Dark}
        internal static Sky Read(bool day,bool overcast,bool hasSunstone,bool indoors)
        {
            if(indoors)return Sky.Dark;
            if(day)return !overcast?Sky.Sun:hasSunstone?Sky.Sunstone:Sky.Hidden;
            return !overcast?Sky.Star:Sky.Dark;
        }
        // Weather that hides the sun and stars: wet, misty, dark or thick with fog.
        internal static bool Overcast(string name,bool wet,bool alwaysDark,double fogDay)
        {
            string n=(name??"").ToLowerInvariant();
            if(wet||alwaysDark)return true;
            if(n.Contains("mist")||n.Contains("fog")||n.Contains("storm")||n.Contains("snow")||n.Contains("rain")||n.Contains("dark")||n.Contains("ash"))
                return !n.Contains("clear");
            return fogDay>0.02;
        }
        internal static string Weather(string name)
        {
            string n=(name??"").ToLowerInvariant();
            if(n.Contains("thunder"))return "thunderstorm";
            if(n.Contains("snowstorm"))return "snowstorm";
            if(n.Contains("snow"))return "snow";
            if(n.Contains("lightrain"))return "light rain";
            if(n.Contains("ash"))return "ash rain";
            if(n.Contains("rain"))return "rain";
            if(n.Contains("mist")&&!n.Contains("clear"))return "mist";
            if(n.Contains("dark"))return "gloom";
            if(n.Contains("clear")||n.Contains("heath"))return "clear skies";
            return n.Length==0?"unknown":n.Replace('_',' ');
        }
        internal static string WindStrength(double intensity)=>
            intensity<0.15?"calm":intensity<0.35?"a light breeze":intensity<0.55?"a fresh breeze":intensity<0.75?"a strong wind":intensity<0.9?"a gale":"a storm wind";

        // A ship's name until someone gives it one: kennings for ships, and the names of famous ones.
        private static readonly string[] ShipNames={"Wave-Steed","Sea-Stallion","Long Serpent","Short Serpent","the Crane","the Bison","Surf-Elk",
            "Fjord-Horse","Brine-Raven","Whale-Road Rider","Sail-Steed","Foam-Throat","Spray-Dragon","Gull's Wing","Storm-Goat","Ægir's Elk",
            "Wind-Mare","Oar-Steed","Swan of the Bay","Sea-Bear","Keel-Wolf","Ran's Darling","Tide-Hart","Mast-Hawk"};
        internal static string ShipName(long seed)=>ShipNames[(int)(((seed%ShipNames.Length)+ShipNames.Length)%ShipNames.Length)];

        // The sea's floor, as the tallow in the lead's cup brings it up.
        internal static string Bottom(string biome)
        {
            switch(biome)
            {
                case "Meadows":return "pale sand";
                case "BlackForest":return "stones and needles";
                case "Swamp":return "black mud";
                case "Mountain":return "bare rock";
                case "Plains":return "sand and shell";
                case "Mistlands":return "black sand";
                case "AshLands":return "cinders";
                case "DeepNorth":return "ice-scoured gravel";
                case "Ocean":return "nothing: the cup comes up clean";
                default:return "sand";
            }
        }
    }
}
