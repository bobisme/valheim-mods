using BobsPipes;
using BepInEx;
using BepInEx.Bootstrap;
using System.Globalization;

int checks=0;
void Check(bool condition,string message){checks++;if(!condition)throw new Exception(message);}
void Throws(Action action,string message){bool caught=false;try{action();}catch(ArgumentException){caught=true;}Check(caught,message);}
var bowl=Bowl.Pack(2,300,20).Burn(37.125);
CultureInfo.CurrentCulture=new CultureInfo("de-DE");
var saved=Bowl.Read(bowl.Save());
Check(saved.Blend==2&&saved.Remaining==262.875&&saved.Smoked==57.125,"A stopped bowl survives non-English saves");
var completed=saved.Burn(1000);
Check(completed.Empty&&completed.Smoked==320,"Overshoot spends only actual tobacco");
Check(Bowl.Read(completed.Save()).Smoked==320,"Empty bowls retain seasoning");
Check(Bowl.Pack(0,300,completed.Smoked).Smoked==320,"Repacking keeps seasoning");
var incremental=Bowl.Pack(1,300);for(int i=0;i<100;i++)incremental=Bowl.Read(incremental.Burn(0.125).Save());
Check(incremental.Remaining==287.5&&incremental.Smoked==12.5,"Reloads do not refill or lose bowl accounting");
Check(Bowl.Pack(0,300,9999999).Burn(10).Smoked==10000000,"Seasoning is bounded");
foreach(string invalid in new[]{null,"","2|0|300|0","1|3|300|0","1|-1|300|0","1|0|3601|0","1|0|-1|0","1|0|NaN|0","1|0|Infinity|0","1|0|300|-1","1|0|300|NaN","1|0|300|10000001","1|0|300|0|extra",new string('x',129)})
 Check(Bowl.Read(invalid).Empty,"Reject malformed state: "+invalid);
foreach(double invalid in new[]{-1,double.NaN,double.PositiveInfinity})Throws(()=>bowl.Burn(invalid),"Invalid burn duration");
foreach(double invalid in new[]{29,3601,double.NaN,double.NegativeInfinity})Throws(()=>Bowl.Pack(0,invalid),"Invalid pack duration");
Throws(()=>Bowl.Pack(3,300),"Unknown blend");

var bridge=new Tobacco();
Check(!bridge.Ready(),"Missing dependency disables lighting");
void Install(BaseUnityPlugin plugin)=>Chainloader.PluginInfos[Tobacco.Guid]=new PluginInfo{Instance=plugin};
Install(new OldCigars());Check(bridge.Ready()&&!bridge.Shared&&bridge.Exclusive(new Character(),Blend.All[0].Effect),"Cigars without the API: pipes still light, on their own");
Install(new NonConstantVersion());Check(bridge.Ready()&&!bridge.Shared,"Nonconstant API field cannot throw in the inventory hook");
Install(new FutureCigars());Check(bridge.Ready()&&bridge.Shared,"A newer smoking API version counts as v1 or newer");bridge.Release();
Install(new BadSignatures());Check(bridge.Ready()&&!bridge.Shared,"Matching names with wrong signatures are not used");
var cigars=new Cigars();Install(cigars);
Check(bridge.Ready()&&bridge.Shared&&cigars.Names.SetEquals(Blend.All.Select(b=>b.Effect)),"Register each smoke with the live dependency");
Check(bridge.Ready()&&cigars.RegisterCalls==3,"Cached lookups do not register every frame");
var character=new Character();Check(bridge.Exclusive(character,Blend.All[1].Effect)&&cigars.Character==character&&cigars.Keep==Blend.All[1].Effect,"Exclusive request uses actual character and effect");
var replacement=new Cigars();Install(replacement);
Check(bridge.Ready()&&replacement.RegisterCalls==3,"F6 reconnects and registers with the replacement instance");
Check(bridge.Exclusive(character,Blend.All[2].Effect)&&replacement.Keep==Blend.All[2].Effect,"Stop calls follow new dependency instance");
bridge.Release();Check(replacement.Names.Count==0,"Unload unregisters add-on names");
Check(bridge.Ready()&&replacement.RegisterCalls==6,"Reloaded add-on can register again");
Chainloader.PluginInfos.Clear();Check(!bridge.Ready(),"Missing live instance is not satisfied by stale MethodInfo");
Install(cigars);Check(bridge.Ready()&&cigars.RegisterCalls==6,"Reappearing instance gets fresh registration");bridge.Release();
var refusing=new Cigars{RefuseAfter=1};Install(refusing);Check(bridge.Ready()&&!bridge.Shared&&refusing.Names.Count==0,"Failed partial registration rolls back and falls back to pipes alone");
var throwing=new Cigars{Throw=true};Install(throwing);Check(bridge.Ready()&&!bridge.Shared&&throwing.Names.Count==0,"Registration failure falls back to pipes alone");
Install(new Cigars{ThrowOnStop=true});Check(!bridge.Exclusive(character,Blend.All[0].Effect),"Dependency callback failure cannot start a pipe");
Console.WriteLine($"Bob's Pipes: {checks} state and dependency checks passed");

public class NonConstantVersion:BaseUnityPlugin{public static int SmokingApiVersion=1;}
public class OldCigars:BaseUnityPlugin { }
public class FutureCigars:Cigars{public new const int SmokingApiVersion=2;}
public class BadSignatures:BaseUnityPlugin
{
 public const int SmokingApiVersion=1;
 public string RegisterSmokingEffect(string name)=>name;
 public void UnregisterSmokingEffect(string name){}
 public bool StopOtherSmoking(Character character,string keep)=>true;
}
public class Cigars:BaseUnityPlugin
{
 public const int SmokingApiVersion=1;
 public HashSet<string> Names=new();public int RegisterCalls,RefuseAfter=int.MaxValue;
 public bool Throw,ThrowOnStop;public Character Character;public string Keep;
 public bool RegisterSmokingEffect(string name){RegisterCalls++;if(Throw)throw new Exception();if(RegisterCalls>RefuseAfter)return false;Names.Add(name);return true;}
 public void UnregisterSmokingEffect(string name)=>Names.Remove(name);
 public bool StopOtherSmoking(Character character,string keep){if(ThrowOnStop)throw new Exception();Character=character;Keep=keep;return Names.Contains(keep);}
}
public class Character{}
namespace BepInEx { public class BaseUnityPlugin{} public class PluginInfo{public BaseUnityPlugin Instance;} }
namespace BepInEx.Bootstrap {public static class Chainloader{public static Dictionary<string,BepInEx.PluginInfo> PluginInfos=new();}}
