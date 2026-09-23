using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;

namespace BigRadio
{
    [BepInPlugin(Guid, Name, Version)]
    public class Plugin : BasePlugin
    {
        public const string Guid = "ArtemisCorp.BigRadio";
        public const string Name = "BigRadio";
        public const string Version = "1.0.0";

        internal static ManualLogSource L;
        internal static ConfigFile Cfg;

        public override void Load()
        {
            L = Log;
            Cfg = Config;
            Core.Init();

            // Patch each set separately. If the game changed and a target is gone,
            // undo everything so the radio stays fully vanilla instead of half-patched.
            var harmony = new Harmony(Guid);
            var patchSets = new[] { typeof(RadioManagerPatches), typeof(MusicPlayerPatches) };
            foreach (var set in patchSets)
            {
                try { harmony.PatchAll(set); }
                catch (Exception e)
                {
                    L.LogWarning($"Could not patch {set.Name}: {e.GetType().Name}: {e.Message}");
                    L.LogWarning("BigRadio keeps running; some vanilla audio behaviour may not be suppressed.");
                }
            }

            if (!ClassInjector.IsTypeRegisteredInIl2Cpp<RadioDriver>())
                ClassInjector.RegisterTypeInIl2Cpp<RadioDriver>();
            AddComponent<RadioDriver>();

            L.LogInfo($"{Name} {Version} loaded. Music folder: {Core.MusicFolder}");
        }
    }
}
