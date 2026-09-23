using System;
using HarmonyLib;
using UnityEngine;

namespace BigRadio
{
    /// <summary>Station discovery.</summary>
    [HarmonyPatch]
    internal static class RadioManagerPatches
    {
        [HarmonyPostfix, HarmonyPatch(typeof(FmRadioManager), "Initialize")]
        private static void InitializePostfix(FmRadioManager __instance)
            => Core.Safe(() => Core.OnManagerInitialized(__instance), "FmRadioManager.Initialize");
    }

    /// <summary>
    /// Keeps taken-over players on the custom clip and out of the time-of-day sync.
    /// These are a safety net only; the takeover itself is done by polling, because
    /// FmRadioPlayer.SetMusicPlayer gets inlined and can't be hooked.
    /// </summary>
    [HarmonyPatch]
    internal static class MusicPlayerPatches
    {
        [HarmonyPrefix, HarmonyPatch(typeof(MusicPlayer), "Play")]
        private static bool PlayPrefix(MusicPlayer __instance, AudioClip clipOverride)
        {
            try { return Core.OnPlay(__instance, clipOverride); }
            catch (Exception e) { Core.Report("MusicPlayer.Play", e); return true; }
        }

        [HarmonyPrefix, HarmonyPatch(typeof(MusicPlayer), "Sync")]
        private static bool SyncPrefix(MusicPlayer __instance) => !Core.IsOverridden(__instance);

        [HarmonyPrefix, HarmonyPatch(typeof(MusicPlayer), "SetSyncPitch")]
        private static bool SetSyncPitchPrefix(MusicPlayer __instance) => !Core.IsOverridden(__instance);

        [HarmonyPostfix, HarmonyPatch(typeof(MusicPlayer), "ManualUpdate")]
        private static void ManualUpdatePostfix(MusicPlayer __instance) => Core.OnManualUpdate(__instance);

        [HarmonyPrefix, HarmonyPatch(typeof(MusicPlayer), "OnDestroy")]
        private static void OnDestroyPrefix(MusicPlayer __instance) => Core.Forget(__instance);
    }
}
