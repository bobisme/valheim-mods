// Boundary doubles: geometry and reflection dispatch run without Unity's native runtime.
namespace UnityEngine { public struct Vector3 {} public struct Quaternion {} }
public class Player {}
namespace BepInEx
{
    public class BaseUnityPlugin {}
    public class PluginInfo { public BaseUnityPlugin Instance; }
}
namespace BepInEx.Bootstrap
{
    public static class Chainloader { public static readonly System.Collections.Generic.Dictionary<string,BepInEx.PluginInfo> PluginInfos = new(); }
}
