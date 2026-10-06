using System;
using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using UnityEngine;

namespace BuildShapes
{
    internal sealed class Planner
    {
        internal const string Guid = "com.dhack.buildorders";
        private BaseUnityPlugin _instance;
        private MethodInfo _create, _remove;
        internal string Status { get; private set; } = "Update BuildOrders to a version with add-on support.";
        internal bool Ready()
        {
            BaseUnityPlugin current = null;
            if (Chainloader.PluginInfos.TryGetValue(Guid, out var info)) current = info.Instance;
            if (current == null) { _instance = null; _create = _remove = null; Status = "BuildOrders is missing or reloading."; return false; }
            if (current == _instance && _create != null && _remove != null) return true;
            _instance = current; _create = _remove = null;
            Type type = current.GetType();
            if ((int?)type.GetField("PlanningApiVersion", BindingFlags.Public | BindingFlags.Static)?.GetRawConstantValue() != 1)
            { Status = "Update BuildOrders to a version with add-on support."; return false; }
            _create = type.GetMethod("TryCreateGhostPlan", new[] { typeof(Player), typeof(string), typeof(string[]), typeof(Vector3[]), typeof(Quaternion[]), typeof(string).MakeByRefType(), typeof(string).MakeByRefType() });
            _remove = type.GetMethod("TryRemoveGhostPlan", new[] { typeof(Player), typeof(string), typeof(int).MakeByRefType(), typeof(string).MakeByRefType() });
            if (_create?.ReturnType != typeof(bool) || _remove?.ReturnType != typeof(bool))
            { _create = _remove = null; Status = "BuildOrders has an incompatible planning interface."; return false; }
            Status = null; return true;
        }
        internal bool Create(Player player, string prefab, Vector3[] positions, Quaternion[] rotations, out string key, out string error)
        {
            key = null; error = null;
            if (!Ready()) { error = Status; return false; }
            var names = new string[positions.Length]; for (int i = 0; i < names.Length; i++) names[i] = prefab;
            object[] args = { player, "Curve", names, positions, rotations, null, null };
            try { bool ok = (bool)_create.Invoke(_instance, args); key = args[5] as string; error = args[6] as string; return ok; }
            catch (Exception ex) { error = ex.GetBaseException().Message; return false; }
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
