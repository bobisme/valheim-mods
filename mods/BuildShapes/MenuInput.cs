using HarmonyLib;
using UnityEngine;

namespace BuildShapes
{
    // While the shape radial is open: the player doesn't move or attack, the camera lets the cursor go, and the wheel is the
    // radial's (and a ready shape's, so the camera doesn't zoom while it turns).
    [HarmonyPatch(typeof(Player),"TakeInput")]
    internal static class MenuPlayerInput
    {
        private static void Postfix(Player __instance,ref bool __result)
        {if(__instance==Player.m_localPlayer && Plugin.OptionsMenuOpen && !Plugin.ProbingInput)__result=false;}
    }
    [HarmonyPatch(typeof(PlayerController),"TakeInput")]
    internal static class MenuControllerInput
    {
        private static void Postfix(ref bool __result) {if(Plugin.OptionsMenuOpen)__result=false;}
    }
    [HarmonyPatch(typeof(GameCamera),"UpdateMouseCapture")]
    internal static class MenuMouseCapture
    {
        private static bool Prefix()
        {if(!Plugin.OptionsMenuOpen)return true;Cursor.lockState=CursorLockMode.None;Cursor.visible=true;return false;}
    }
    [HarmonyPatch(typeof(ZInput),"GetMouseScrollWheel")]
    internal static class ShapeScroll
    {
        private static void Postfix(ref float __result) {if(Plugin.OptionsMenuOpen || Plugin.ShapeTakesScroll)__result=0;}
    }
    [HarmonyPatch(typeof(Humanoid),nameof(Humanoid.StartAttack))]
    internal static class MenuAttack
    {
        private static bool Prefix(Humanoid __instance,ref bool __result)
        {if(__instance!=Player.m_localPlayer || !Plugin.OptionsMenuOpen)return true;__result=false;return false;}
    }
}
