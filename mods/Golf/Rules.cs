using System;
using System.Collections.Generic;
using System.Globalization;

namespace MeadowGolf
{
    // Kept independent of Unity so the scoring and shot bounds can be checked without a running game.
    internal static class Rules
    {
        internal const int MaxStrokes=99;
        internal sealed class Hole
        {
            internal string Course;
            internal int Number,Par;
            internal string Label=>Course+":"+Number+":"+Par;
            internal bool Matches(Hole other)=>other!=null&&Number==other.Number&&string.Equals(Course,other.Course,StringComparison.OrdinalIgnoreCase);
        }
        internal static bool Parse(string text,out Hole hole)
        {
            hole=null;
            if(text==null||text.Length>64)return false;
            string[] parts=text.Trim().Split(':');
            if(parts.Length!=3)return false;
            string course=parts[0].Trim();
            if(course.Length==0||course.Length>48)return false;
            foreach(char c in course)if(char.IsControl(c)||c=='<'||c=='>')return false;
            if(!int.TryParse(parts[1],NumberStyles.None,CultureInfo.InvariantCulture,out int n)||n<1||n>18||
               !int.TryParse(parts[2],NumberStyles.None,CultureInfo.InvariantCulture,out int par)||par<2||par>8)return false;
            hole=new Hole{Course=course,Number=n,Par=par};return true;
        }
        internal static bool Finite(double n)=>!double.IsNaN(n)&&!double.IsInfinity(n);
        internal static bool ValidShot(int mode,float power,double directionLength,double vertical)=>
            mode>=0&&mode<=2&&Finite(power)&&power>=0&&power<=1&&Finite(directionLength)&&
            directionLength>=0.98&&directionLength<=1.02&&Finite(vertical)&&Math.Abs(vertical)<0.01;
        internal static double Speed(int mode,double power)
        {
            power=Math.Max(0,Math.Min(1,power));
            return mode==2?0.5+7.5*power:mode==1?3+10*power:5+21*power;
        }
        internal static double Loft(int mode)=>mode==0?24:mode==1?48:0;
        internal static bool Captures(double horizontal,double vertical,double speed,int strokes)=>
            Finite(horizontal)&&Finite(vertical)&&Finite(speed)&&horizontal<=0.29&&Math.Abs(vertical)<=0.23&&speed<=2.2&&strokes>0;
        internal sealed class BeginState{internal string Card="";internal int Strokes;}
        internal static BeginState Begin(string oldLabel,string oldCard,int oldStrokes,bool done,Hole hole,bool fresh)
        {
            var state=new BeginState();
            if(fresh||!Parse(oldLabel,out var previous)||!string.Equals(previous.Course,hole.Course,StringComparison.OrdinalIgnoreCase))return state;
            List<Result> rows=ReadCard(oldCard);
            state.Card=string.Join(";",rows.ConvertAll(r=>r.Hole+","+r.Par+","+r.Strokes));
            if(previous.Matches(hole)&&!done)state.Strokes=Math.Min(MaxStrokes,Math.Max(0,oldStrokes)+1);
            return state;
        }
        internal sealed class Result{internal int Hole,Par,Strokes;}
        internal static List<Result> ReadCard(string text)
        {
            var result=new List<Result>();
            if(string.IsNullOrEmpty(text)||text.Length>256)return result;
            var seen=new HashSet<int>();
            foreach(string row in text.Split(';'))
            {
                string[] p=row.Split(',');
                if(p.Length!=3||!int.TryParse(p[0],out int h)||!int.TryParse(p[1],out int par)||!int.TryParse(p[2],out int strokes)||
                   h<1||h>18||par<2||par>8||strokes<1||strokes>MaxStrokes||!seen.Add(h))return new List<Result>();
                result.Add(new Result{Hole=h,Par=par,Strokes=strokes});
            }
            result.Sort((a,b)=>a.Hole.CompareTo(b.Hole));return result;
        }
        internal static string Record(string card,int hole,int par,int strokes)
        {
            if(hole<1||hole>18||par<2||par>8||strokes<1||strokes>MaxStrokes)throw new ArgumentOutOfRangeException();
            List<Result> rows=ReadCard(card);rows.RemoveAll(r=>r.Hole==hole);rows.Add(new Result{Hole=hole,Par=par,Strokes=strokes});
            rows.Sort((a,b)=>a.Hole.CompareTo(b.Hole));
            return string.Join(";",rows.ConvertAll(r=>r.Hole+","+r.Par+","+r.Strokes));
        }
        internal static string Outcome(int strokes,int par)=>strokes==1?"Hole in one!":strokes==par?"Par":strokes==par-1?"Birdie":
            strokes==par-2?"Eagle":strokes==par+1?"Bogey":(strokes-par>0?"+":"")+(strokes-par)+" to par";
    }
}
