using HarmonyLib;
using LmpClient.Pimp;

// ReSharper disable All

namespace LmpClient.Harmony
{
    /// <summary>
    /// PiMP (14 Nov 2026): KSP deletes every manoeuvre node when flight ends. See <see cref="PimpNodeTeardown"/>.
    /// </summary>
    [HarmonyPatch(typeof(PatchedConicRenderer))]
    [HarmonyPatch("OnSceneSwitch")]
    public class PatchedConicRenderer_OnSceneSwitch
    {
        [HarmonyPrefix]
        private static void PrefixOnSceneSwitch(PatchedConicRenderer __instance) => PimpNodeTeardown.Begin(__instance);
    }

    [HarmonyPatch(typeof(PatchedConicRenderer))]
    [HarmonyPatch("OnDestroy")]
    public class PatchedConicRenderer_OnDestroy
    {
        [HarmonyPrefix]
        private static void PrefixOnDestroy(PatchedConicRenderer __instance) => PimpNodeTeardown.Begin(__instance);
    }
}
