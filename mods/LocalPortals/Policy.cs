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
        internal const double LinkRange=80;         // a new portal links to an unlinked one this close: both stay loaded together
        internal const double ViewRange=90;         // portals farther from the camera than this keep their last picture
        internal const double BodyHeight=1.0;       // the point of the body that has to pass through
        internal const double StepDepth=1.5;        // a crossing counts only from this close to the surface (not a jump across the world)

        internal static bool InOpening(double x,double y)
        {
            if(Math.Abs(x)>HalfWidth||y<Bottom-0.3)return false; // (a little below the ground counts)
            if(y<=Spring)return true;
            double u=x/HalfWidth,v=(y-Spring)/Rise;
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
