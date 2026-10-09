using System.Reflection;
public sealed class ZNetView
{
    public bool Valid=true,Owner,AllowClaim=true;public int Claims;public Action OnClaim;public List<string> Events=new();
    public bool IsValid()=>Valid;public bool IsOwner()=>Owner;
    public void ClaimOwnership(){Events.Add("claim");Claims++;if(AllowClaim)Owner=true;OnClaim?.Invoke();}
}
public sealed class TerrainComp
{
    public ZNetView View=new();public int DataRevision,LoadedRevision=-1,SavedHeight,Height;public int Loads;
    public T GetComponent<T>() where T:class=>View as T;
    private void CheckLoad(){View.Events.Add("load");Loads++;if(LoadedRevision!=DataRevision){Height=SavedHeight;LoadedRevision=DataRevision;}}
}
namespace BuildShapes
{
    public sealed partial class Plugin
    {
        private static readonly MethodInfo HallLoad=typeof(TerrainComp).GetMethod("CheckLoad",BindingFlags.Instance|BindingFlags.NonPublic);
        public static void Own(TerrainComp comp,bool acquire=false)=>HallOwner(comp,acquire);
        public static void Read(TerrainComp comp)=>LoadHallGround(comp);
    }
}
