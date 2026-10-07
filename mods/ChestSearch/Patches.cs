using HarmonyLib;
using UnityEngine;
namespace ChestSearch
{
    [HarmonyPatch(typeof(Container),"RPC_OpenResponse")]
    internal static class OpenResponse
    {private static bool Prefix(Container __instance,long uid,bool granted)=>Plugin.Instance?.Response(__instance,uid,granted)??true;}
    [HarmonyPatch(typeof(InventoryGui),"Update")]
    internal static class InventoryUpdate
    {private static void Prefix()=>Plugin.Instance?.BlockTyping();}
    [HarmonyPatch(typeof(InventoryGui),nameof(InventoryGui.Hide))]
    internal static class InventoryClose
    {private static void Postfix()=>Plugin.Instance?.Close(false);}
    [HarmonyPatch(typeof(InventoryGui),"OnSelectedItem")]
    internal static class NativeSelect
    {private static bool Prefix()=>!Plugin.Suppress;}
    [HarmonyPatch(typeof(InventoryGui),"OnReleasedItem")]
    internal static class NativeRelease
    {private static bool Prefix()=>!Plugin.Suppress;}
    [HarmonyPatch(typeof(InventoryGui),"OnRightClickItem")]
    internal static class NativeUse
    {private static bool Prefix()=>!Plugin.Suppress;}
    [HarmonyPatch(typeof(Player),"TakeInput")]
    internal static class PlayerInput
    {private static void Postfix(Player __instance,ref bool __result){if(__instance==Player.m_localPlayer&&Plugin.Blocking)__result=false;}}
    [HarmonyPatch(typeof(PlayerController),"TakeInput")]
    internal static class ControllerInput
    {private static void Postfix(ref bool __result){if(Plugin.Blocking)__result=false;}}
    [HarmonyPatch(typeof(Humanoid),nameof(Humanoid.StartAttack))]
    internal static class Attack
    {private static bool Prefix(Humanoid __instance,ref bool __result){if(__instance!=Player.m_localPlayer||!Plugin.Blocking)return true;__result=false;return false;}}
}
