using System;
using System.Collections.Generic;

namespace LocalPortals
{
    // The portal's shape and the rules for linking, looking and stepping through, in metres in a portal's own frame:
    // x right (as the portal sees it), y up from the ground, z out of its front face. Kept free of the game so it can be checked without it.
    internal static class Policy
    {
        // The opening: the glass of a tall mirror, from the ground up, straight-sided to the spring line, then a rounded arch.
        internal const double HalfWidth=0.62,Bottom=-0.1,Spring=1.85,Rise=0.65; // (the glass reaches a little into the ground: it is never level)
        internal static double Top=>Spring+Rise;
        internal static double CentreY=>(Bottom+Top)/2;
        internal const double BackZ=-0.09,BackThickness=0.04;   // the solid board behind the glass
        internal const double FrameDepth=0.17;                  // the frame, front to back
        internal static double LinkRange=200;       // the farthest two portals link (a setting, the host's): the far one is kept loaded (Far)
        internal const double MinLinkRange=20,MaxLinkRange=2000;
        internal const double ViewRange=90;         // portals farther from the camera than this keep their last picture
        internal const double BodyHeight=1.0;       // the point of the body that has to pass through
        internal const double StepDepth=1.5;        // a crossing counts only from this close to the surface (not a jump across the world)

        // Inside the glass, and at least `room` from its sides and arch (the ground is no edge).
        internal static bool InOpening(double x,double y,double room=0)
        {
            double hw=HalfWidth-room,rise=Rise-room;
            if(hw<=0||rise<=0)return false;
            if(Math.Abs(x)>hw||y<Bottom-0.3)return false; // (a little below the ground counts)
            if(y<=Spring)return true;
            double u=x/hw,v=(y-Spring)/rise;
            return u*u+v*v<=1;
        }

        // Within the mirror's whole outline as seen from in front: glass, frame and crest. A camera looking at someone through
        // that outline from behind would see only wood, so it looks through the glass of the other mirror instead.
        internal static bool InOutline(double x,double y)=>Math.Abs(x)<=HalfWidth+0.3&&y>=Bottom-0.3&&y<=Top+0.6;

        // The edge of the glass grown outward by some distance, as a path in the portal's x,y: up the left side from the ground,
        // over the arch, down the right side (clockwise seen from the front of the portal's own axes).
        internal static List<(double x,double y)> Edge(double grow,int arch=28,double bottom=Bottom)
        {
            var path=new List<(double x,double y)>();
            double hw=HalfWidth+grow,rise=Rise+grow;
            path.Add((-hw,bottom));
            for(int i=0;i<=arch;i++)
            {
                double t=Math.PI-i*Math.PI/arch; // from the left spring, over the top, to the right spring
                path.Add((hw*Math.Cos(t),Spring+rise*Math.Sin(t)));
            }
            path.Add((hw,bottom));
            return path;
        }

        // Where a point in one portal's frame comes out of its partner, in the partner's frame: in through the front, out of the front.
        internal static (double x,double y,double z) Through(double x,double y,double z)=>(-x,y,-z);

        // A body went through the surface between two frames: it was in front, is now behind, and passed inside the opening.
        internal static bool Crossed((double x,double y,double z) before,(double x,double y,double z) now)
        {
            if(!(before.z>0&&now.z<=0))return false;
            if(before.z>StepDepth||now.z<-StepDepth)return false;
            double t=before.z/(before.z-now.z); // where along the step it met the surface
            double x=before.x+(now.x-before.x)*t,y=before.y+(now.y-before.y)*t;
            return InOpening(x,y);
        }

        // Something small in flight (an arrow, a bomb, a dropped item) moving from `before` to `after` in one step: the part of
        // the step at which its front, `radius` ahead of its middle, reaches the glass with room to pass the frame, or -1 if it
        // does not go through in this step. It is put through there, before it can touch the board behind the glass.
        internal const double FlightDepth=5;    // the longest step that counts (not a jump across the world)
        internal static double Pass((double x,double y,double z) before,(double x,double y,double z) after,double radius)=>Pass(before,after,radius,radius);
        // The same for something longer than it is wide: its front `depth` ahead of its middle, and `room` across.
        internal static double Pass((double x,double y,double z) before,(double x,double y,double z) after,double depth,double room)
        {
            double radius=depth;
            if(!(before.z>0&&after.z<=radius&&after.z<before.z))return -1;
            if(before.z>FlightDepth+radius||after.z<-FlightDepth)return -1;
            double t=before.z>radius?(before.z-radius)/(before.z-after.z):0;
            double x=before.x+(after.x-before.x)*t,y=before.y+(after.y-before.y)*t;
            return InOpening(x,y,room)?t:-1;
        }

        // A shot appearing at `shot`, loosed by someone whose middle is at `body`: if the body is in front and the shot already past
        // the glass, inside the opening, the part of the way from body to shot at which it crossed; otherwise -1.
        internal static double Loosed((double x,double y,double z) body,(double x,double y,double z) shot)
        {
            if(!(shot.z<=0&&shot.z>-1&&body.z>0&&body.z<StepDepth))return -1;
            double t=body.z/(body.z-shot.z);
            return InOpening(body.x+(shot.x-body.x)*t,body.y+(shot.y-body.y)*t)?t:-1;
        }

        // A creature's way to somewhere by a mirror: to the front of one mirror, through, and from the front of the other.
        internal static double Detour(double toMirror,double fromMirror)=>toMirror+1+fromMirror;
        // It takes the mirror only when that saves a good deal of walking (or, once it has chosen it, while it saves any), so it
        // does not dither between two ways of about the same length.
        internal static bool TakeMirror(double direct,double byMirror,bool alreadyChosen)=>alreadyChosen?byMirror<direct-1:byMirror<direct*0.7-2;

        // Views drawn inside views (a mirror seen in a mirror's view) use what is left of the frame's budget of views after the
        // mirrors the game camera sees: never more views in all than the player allows.
        internal static int NestBudget(int maxViews,int direct,bool nested)=>nested?Math.Max(0,maxViews-direct):0;

        // How long a mirror waiting for its turn at a view keeps its last picture: a little longer than a round of turns.
        internal static int KeepPictureFrames(int sharing)=>Math.Max(2,sharing)+2;

        // The light falling out of a mirror from the far side, from the mean colour of its view (linear): its hue, paled a
        // little toward white, and a strength from its brightness (none from a dark night out there).
        internal static (double r,double g,double b,double intensity) SpillLight(double r,double g,double b)
        {
            r=Math.Max(0,r);g=Math.Max(0,g);b=Math.Max(0,b);
            double max=Math.Max(r,Math.Max(g,b));
            if(max<1e-4)return (0,0,0,0);
            const double pale=0.3;
            double lum=0.2126*r+0.7152*g+0.0722*b;
            return (r/max*(1-pale)+pale,g/max*(1-pale)+pale,b/max*(1-pale)+pale,Math.Min(1.3,lum*1.4));
        }

        // Whether someone may step through carrying what they carry: unless the host keeps to the game's portal rule (no ore,
        // metal or eggs) and the world does not allow everything through portals anyway.
        internal static bool MayCarry(bool blockUnportable,bool worldAllowsAll,bool carriesOnlyPortable)=>!blockUnportable||worldAllowsAll||carriesOnlyPortable;

        // The colour a new portal takes, opposite to the one it links to (0 blue, 1 orange).
        internal static int ColourFor(int partnerColour)=>partnerColour==0?1:0;

        // Picture size for a portal covering this many screen pixels: rounded up to steps of 64, never past the screen, never tiny.
        internal static int PictureSize(double pixels,int screen,double scale)
        {
            if(double.IsNaN(pixels)||pixels<=0)return 64;
            double want=Math.Min(pixels*scale,screen);
            int size=(int)Math.Ceiling(want/64)*64;
            return Math.Max(64,Math.Min(size,Math.Max(64,screen)));
        }
        // Keep the current picture unless it is too small, or more than twice as big as needed (no reallocating every frame).
        internal static bool Resize(int current,int wanted)=>current<wanted||current>wanted*2;
    }
}
