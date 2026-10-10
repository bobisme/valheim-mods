using System;

namespace PassengerCart
{
    // Where things go in the passenger cart, in metres in the cart's own frame: x right, y up from its origin, z forward (the handle).
    // Kept free of the game so the layout can be checked without it.
    internal static class Policy
    {
        internal const double Scale=1.3;            // the vanilla cart, this much bigger every way (round wheels need a uniform scale)
        internal const double HealthFactor=2;       // twice the vanilla cart's health
        internal const double MassFactor=1.2;       // a little heavier than the vanilla cart: steadier, and one player still pulls it
        internal const double CenterOfMassHeight=0.4; // below the axle, so it rights itself instead of tipping
        internal const double AngularDamping=0.6;   // calms rocking and spinning after bumps
        internal const double UseDistance=3;        // metres from a seat to sit in it
        internal const double TakenRadius=0.3;      // a player this close to a seat's attach point is sitting in it
        internal const double TipLimit=0.3;         // passengers get off when the cart's up vector points lower than this

        // The vanilla bed, scaled: floor top, side walls' inner faces, front and back walls' inner faces, wall top.
        internal static readonly double Floor=0.52*Scale,WallX=(0.73-0.025)*Scale,FrontZ=(0.98-0.03)*Scale,BackZ=(-1.02+0.03)*Scale,WallTop=0.945*Scale;
        internal const double SeatRise=0.42;        // seat surface above the floor
        internal const double SitDrop=0.45;         // the sitting pose puts the hips this far above the attach point (vanilla bench)
        internal const double SeatWidth=0.75,SeatDepth=0.45,BenchWidth=1.6;
        internal static readonly double[] Rows={0.6,-0.35}; // front and back bench centres
        internal const double SeatX=0.4;
        internal const double CargoZ=-0.98,CargoDepth=0.57,CargoWidth=1.36,CargoHeight=0.62;
        internal const double CrateSize=0.57,CrateX=0.35,CrateTurn=2.5; // two crates with a clear gap: touching faces flicker

        internal static double SeatTop=>Floor+SeatRise;

        internal struct Seat
        {
            internal double X,Z;        // centre of the seat
            internal double DetachX;    // where its passenger steps off, beside the cart
        }
        internal static Seat[] Seats()
        {
            var seats=new Seat[Rows.Length*2];
            for(int r=0;r<Rows.Length;r++)
                for(int s=0;s<2;s++)
                {
                    double side=s==0?-1:1;
                    seats[r*2+s]=new Seat{X=side*SeatX,Z=Rows[r],DetachX=side*(WallX+0.7)};
                }
            return seats;
        }

        internal static double Mass(double vanilla)=>double.IsNaN(vanilla)||vanilla<=0?50*MassFactor:vanilla*MassFactor;
        internal static double Health(double vanilla)=>double.IsNaN(vanilla)||vanilla<=0?500*HealthFactor:vanilla*HealthFactor;
        internal static bool Taken(double distance)=>distance<TakenRadius;
        internal static bool Tipped(double upY)=>!(upY>=TipLimit); // NaN counts as tipped
    }
}
