using System;
using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using UnityEngine;

namespace BuildShapes
{
    internal sealed class Planner
    {
        internal const string Guid = "com.quad.buildorders";
        internal const string OldGuid = "com.dhack.buildorders"; // (BuildOrders before 1.12.1)
        private BaseUnityPlugin _instance;
        private MethodInfo _create, _shell, _remove, _ray, _available;
        internal bool WholeShell => Ready() && _shell!=null;
        internal bool Extended => Ready() && _ray != null && _available != null;
        internal string Status { get; private set; } = "Update BuildOrders to a version with add-on support.";
        internal bool Ready()
        {
            BaseUnityPlugin current = null;
            if (Chainloader.PluginInfos.TryGetValue(Guid, out var info) || Chainloader.PluginInfos.TryGetValue(OldGuid, out info)) current = info.Instance;
            if (current == null) { _instance = null; _create = _shell = _remove = _ray = _available = null; Status = "BuildOrders is missing or reloading."; return false; }
            if (current == _instance && _create != null && _remove != null) return true;
            _instance = current; _create = _shell = _remove = _ray = _available = null;
            Type type = current.GetType();
            if (!Equals(type.GetField("PlanningApiVersion", BindingFlags.Public | BindingFlags.Static)?.GetRawConstantValue(), 1))
            { Status = "Update BuildOrders to a version with add-on support."; return false; }
            _create = type.GetMethod("TryCreateGhostPlan", new[] { typeof(Player), typeof(string), typeof(string[]), typeof(Vector3[]), typeof(Quaternion[]), typeof(string).MakeByRefType(), typeof(string).MakeByRefType() });
            _shell = type.GetMethod("TryCreateBuildingShell", new[] { typeof(Player), typeof(string), typeof(string[]), typeof(Vector3[]), typeof(Quaternion[]), typeof(string).MakeByRefType(), typeof(string).MakeByRefType() });
            if (_shell?.ReturnType != typeof(bool)) _shell=null;
            _remove = type.GetMethod("TryRemoveGhostPlan", new[] { typeof(Player), typeof(string), typeof(int).MakeByRefType(), typeof(string).MakeByRefType() });
            if (_create?.ReturnType != typeof(bool) || _remove?.ReturnType != typeof(bool))
            { _create = _remove = null; Status = "BuildOrders has an incompatible planning interface."; return false; }
            _available = type.GetMethod("IsPlanningInputAvailable", new[] { typeof(Player) });
            _ray = type.GetMethod("TryGetGhostAtRay", new[] { typeof(Player), typeof(Vector3), typeof(Vector3), typeof(string).MakeByRefType(),
                typeof(string).MakeByRefType(), typeof(Vector3).MakeByRefType(), typeof(Quaternion).MakeByRefType(), typeof(float).MakeByRefType() });
            if (_available?.ReturnType != typeof(bool)) _available = null;
            if (_ray?.ReturnType != typeof(bool)) _ray = null;
            Status = null; return true;
        }
        internal bool Create(Player player, string prefab, Vector3[] positions, Quaternion[] rotations, out string key, out string error)
        {
            key = null; error = null;
            if (!Ready()) { error = Status; return false; }
            var names = new string[positions.Length]; for (int i = 0; i < names.Length; i++) names[i] = prefab;
            return Create(player, "Curve", names, positions, rotations, out key, out error);
        }
        internal bool Create(Player player, string title, string[] names, Vector3[] positions, Quaternion[] rotations, out string key, out string error)
        {
            key = null; error = null;
            if (!Ready()) { error = Status; return false; }
            object[] args = { player, title, names, positions, rotations, null, null };
            try { bool ok = (bool)_create.Invoke(_instance, args); key = args[5] as string; error = args[6] as string; return ok; }
            catch (Exception ex) { error = ex.GetBaseException().Message; return false; }
        }
        internal bool CanCreateShell(int pieces,out string error)
        {
            error=null;if(!Ready()){error=Status;return false;}
            if(pieces<=256)return true;
            string version=_instance.GetType().GetField("Version",BindingFlags.Public|BindingFlags.Static)?.GetRawConstantValue() as string??"unknown";
            if(_shell!=null)
            {
                object limit=_instance.GetType().GetField("MaximumShellPieces",BindingFlags.Public|BindingFlags.Static)?.GetRawConstantValue();
                if(!(limit is int maximum) || pieces<=maximum)return true;
                error=$"Loaded BuildOrders {version} supports at most {maximum} shell pieces; this draft has {pieces}. Install the 2,048-piece Hallwright-compatible build and reload only BuildOrders in F7. Your draft stays open.";return false;
            }
            error=$"Loaded BuildOrders {version} lacks the whole-building API for this {pieces}-piece shell. Install BuildOrders 1.11.0 or newer with the whole-building API and reload only BuildOrders in F7. Your draft stays open.";
            return false;
        }
        internal bool CreateShell(Player player, string title, string[] names, Vector3[] positions, Quaternion[] rotations, out string key, out string error)
        {
            key=null; if(!CanCreateShell(names.Length,out error))return false;
            if(_shell==null)return Create(player,title,names,positions,rotations,out key,out error);
            object[] args={player,title,names,positions,rotations,null,null};
            try { bool ok=(bool)_shell.Invoke(_instance,args);key=args[5] as string;error=args[6] as string;return ok; }
            catch(Exception ex){error=ex.GetBaseException().Message;return false;}
        }
        internal bool Available(Player player)
        {
            if (!Ready()) return false;
            if (_available == null) return true; // Preserve Curve compatibility with API v1's original release.
            try { return (bool)_available.Invoke(_instance, new object[] { player }); }
            catch (Exception ex) { Status = ex.GetBaseException().Message; return false; }
        }
        internal bool AtRay(Player player, Vector3 origin, Vector3 direction, out string id, out string prefab,
            out Vector3 position, out Quaternion rotation, out float distance)
        {
            id = prefab = null; position = default; rotation = default; distance = 0;
            if (!Extended) return false;
            object[] args = { player, origin, direction, null, null, default(Vector3), default(Quaternion), 0f };
            try
            {
                if (!(bool)_ray.Invoke(_instance, args)) return false;
                id = args[3] as string; prefab = args[4] as string; position = (Vector3)args[5]; rotation = (Quaternion)args[6]; distance = (float)args[7];
                return true;
            }
            catch (Exception ex) { Status = ex.GetBaseException().Message; return false; }
        }
        internal bool Remove(Player player, string key, out int removed, out string error)
        {
            removed = 0; error = null;
            if (!Ready()) { error = Status; return false; }
            object[] args = { player, key, 0, null };
            try { bool ok = (bool)_remove.Invoke(_instance, args); removed = (int)args[2]; error = args[3] as string; return ok; }
            catch (Exception ex) { error = ex.GetBaseException().Message; return false; }
        }
    }
}
