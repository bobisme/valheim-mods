using LocalPortals;

int checks=0;
void Check(bool ok,string name){checks++;if(!ok)throw new Exception(name);}

// A player (capsule 0.4 m round, 1.85 m tall) walks into the glass from the ground up.
for(double h=0;h<=1.85;h+=0.05)Check(Policy.InOpening(0.25,h),"The body fits the opening at "+h.ToString("0.00")+" m");
Check(Policy.HalfWidth>0.4+0.1,"The glass is wider than a player, with room to spare");
Check(Policy.Top>=1.85+0.3,"A player's head clears the top rail");
Check(Policy.Bottom<=0,"The glass reaches the ground, so it is walked straight into");
Check(Policy.InOpening(0,Policy.BodyHeight),"The body's middle is in the opening when walking through the centre");
Check(!Policy.InOpening(Policy.HalfWidth+0.01,1)&&!Policy.InOpening(0,Policy.Top+0.01),"Nothing outside the frame counts");
Check(Policy.BackZ+Policy.BackThickness/2<0&&Policy.BackZ-Policy.BackThickness/2>-Policy.FrameDepth,"The back board is behind the glass and inside the frame");
Check(Policy.InOpening(0.4,2.2)&&Policy.InOpening(0,Policy.Top-0.01),"A head slightly off-centre still fits under the arch");
Check(!Policy.InOpening(0.55,Policy.Top-0.05),"The arch's corners are frame, not glass");
var edge=Policy.Edge(0);
Check(edge[0]==(-Policy.HalfWidth,Policy.Bottom)&&edge[^1]==(Policy.HalfWidth,Policy.Bottom),"The edge runs from the ground on the left to the ground on the right");
Check(edge.All(e=>Math.Abs(e.x)<=Policy.HalfWidth+1e-9&&e.y<=Policy.Top+1e-9),"The edge stays inside the glass's box");
Check(Math.Abs(edge.Max(e=>e.y)-Policy.Top)<1e-9,"The arch reaches the top");
Check(edge.Skip(1).Take(edge.Count-2).All(e=>Policy.InOpening(e.x*0.999,e.y*0.999)),"Every point on the arch is the opening's own edge");

// Through: in front of one portal comes out in front of the other, mirrored so left stays left for the walker.
var p=Policy.Through(0.3,1.0,-0.05);
Check(p.x==-0.3&&p.y==1.0&&p.z==0.05,"Just behind the surface comes out just in front of the other");
var twice=Policy.Through(p.x,p.y,p.z);
Check(twice==(0.3,1.0,-0.05),"Going through and back again returns to the same spot");

Check(Policy.Crossed((0.1,1,0.2),(0.1,1,-0.05)),"Walking in through the middle crosses");
Check(!Policy.Crossed((0.1,1,-0.2),(0.1,1,0.05)),"Walking out of the back does not cross (the back is solid wood)");
Check(!Policy.Crossed((1.5,1,0.2),(1.5,1,-0.05)),"Walking past the side of the frame does not cross");
Check(!Policy.Crossed((0,1,0.2),(0,1,0.2)),"Standing in front does not cross");
Check(!Policy.Crossed((0,1,30),(0,1,-30)),"A jump across a long way is not a step through");
Check(Policy.Crossed((0.7,1.3,0.1),(0.0,1.3,-0.1)),"Where the step meets the surface decides, not where it ends");
Check(!Policy.Crossed((0.7,0.3,0.1),(0.7,0.3,-0.1)),"Walking into a post does not cross");

Check(Policy.ColourFor(0)==1&&Policy.ColourFor(1)==0,"A linked pair is one blue and one orange");

Check(Policy.PictureSize(300,1920,1)==320,"Pictures round up to steps of 64");
Check(Policy.PictureSize(5000,1920,1)==1920,"Pictures never exceed the screen");
Check(Policy.PictureSize(0,1920,1)==64&&Policy.PictureSize(double.NaN,1920,1)==64,"A portal off screen gets a tiny picture");
Check(Policy.PictureSize(1000,1920,0.5)==512,"Lower resolution halves the picture");
Check(!Policy.Resize(512,500)&&Policy.Resize(512,600)&&Policy.Resize(1280,600)&&!Policy.Resize(1024,600),"Pictures are only remade when much too small or too big");
Check(Policy.LinkRange<Policy.ViewRange,"A linked portal is always close enough to show its view");

Console.WriteLine($"LocalPortals: {checks} checks passed");
