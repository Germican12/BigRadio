using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using BepInEx;
using BepInEx.Configuration;
using Il2CppInterop.Runtime;
using UnityEngine;
using UnityEngine.Networking;

namespace BigRadio
{
    internal static class Core
    {
        private const string StationSection = "Stations";
        private const float PollInterval = 0.25f;

        // ---- config ----
        private static ConfigEntry<string> _musicFolderCfg;
        private static ConfigEntry<string> _reloadKeyCfg;
        private static ConfigEntry<bool> _verboseCfg;
        private static ConfigEntry<bool> _shuffleCfg;
        private static readonly System.Random Rng = new();

        internal static string MusicFolder { get; private set; }
        private static bool Verbose => _verboseCfg != null && _verboseCfg.Value;

        // ---- stations ----
        private static readonly Dictionary<string, ConfigEntry<string>> Stations = new();
        private static readonly Dictionary<IntPtr, string> GroupToKey = new();
        private static readonly HashSet<string> UsedKeys = new();
        private static readonly Dictionary<string, AudioClip> Clips = new();
        private static readonly List<string> AvailableFiles = new();

        // ---- live overrides, by native MusicPlayer pointer ----
        private static readonly Dictionary<IntPtr, AudioClip> Primary = new();
        private static readonly HashSet<IntPtr> Muted = new();
        private static bool _inOwnPlay;

        // ---- async loading ----
        private sealed class Pending
        {
            public string Key;
            public string File;
            public UnityWebRequest Request;
        }

        private static readonly List<Pending> PendingLoads = new();
        private static readonly List<AudioClip> ToDestroy = new();
        private static readonly Dictionary<string, List<MusicPlayer>> ByKey = new();

        // ---- hotkey ----
        private static int _reloadVk;
        private static bool _keyWasDown;
        private static float _nextPoll;

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        private static readonly HashSet<string> Reported = new();
        private static readonly Action TickAction = TickInner;

        // =====================================================================
        // Setup
        // =====================================================================

        internal static void Init()
        {
            var cfg = Plugin.Cfg;
            var defaultFolder = Path.Combine(Paths.ConfigPath, "BigRadio");
            _musicFolderCfg = cfg.Bind("General", "Music Folder", defaultFolder,
                "Folder that holds your music files. Put .ogg, .wav or .mp3 files here, then restart the game so they appear in the station lists below.");
            _reloadKeyCfg = cfg.Bind("General", "Reload Key", "F8",
                "Key that reloads this config while the game runs. F1-F24, or a single letter or digit.");
            _shuffleCfg = cfg.Bind("General", "Shuffle On Reload", true,
                "Reload key reshuffles: every audio file in the music folder is dealt out to a random station, never the same file twice. Turn off to keep your hand-written station list.");
            _verboseCfg = cfg.Bind("General", "Verbose Logging", false,
                "Log every radio playback change. Turn on when reporting a bug.");
            ApplyGeneralSettings();
            ScanFiles();
        }

        private static void ApplyGeneralSettings()
        {
            var custom = (_musicFolderCfg.Value ?? "").Trim().Trim('"');
            MusicFolder = custom.Length == 0 ? Path.Combine(Paths.ConfigPath, "BigRadio") : custom;

            try
            {
                Directory.CreateDirectory(MusicFolder);
                var hint = Path.Combine(MusicFolder, "PUT_YOUR_MUSIC_HERE.txt");
                if (!File.Exists(hint))
                    File.WriteAllText(hint,
                        "Put your .ogg, .wav or .mp3 files in this folder.\r\n" +
                        "Then open ArtemisCorp.BigRadio.cfg (mod manager: Config editor) and write a file name\r\n" +
                        "next to a station under [Stations], e.g.  musicGroup_DanceFM = my song.ogg\r\n" +
                        "Stations appear in the config after you have loaded into a walk once.\r\n" +
                        "Press F8 in-game to reload.\r\n");
            }
            catch (Exception e)
            {
                Plugin.L.LogWarning($"Could not create music folder '{MusicFolder}': {e.Message}");
            }

            _reloadVk = ParseKey(_reloadKeyCfg.Value);
        }

        private static int ParseKey(string raw)
        {
            var s = (raw ?? "").Trim().ToUpperInvariant();
            if (s.Length >= 2 && s[0] == 'F' && int.TryParse(s.Substring(1), out var n) && n >= 1 && n <= 24)
                return 0x70 + n - 1;
            if (s.Length == 1 && char.IsLetterOrDigit(s[0]))
                return s[0];
            Plugin.L.LogWarning($"ReloadKey '{raw}' not recognised - using F8.");
            return 0x77;
        }

        // =====================================================================
        // Stations
        // =====================================================================

        internal static void OnManagerInitialized(FmRadioManager manager)
        {
            var groups = manager.stationTrackGroups;
            if (groups == null) return;

            var fresh = new List<string>();
            foreach (var g in groups)
            {
                if (g == null || GroupToKey.ContainsKey(g.Pointer)) continue;
                var k = Register(g);
                if (k != null) fresh.Add(k);
            }
            if (fresh.Count > 0)
                Plugin.L.LogInfo($"Found {fresh.Count} radio stations: {string.Join(", ", fresh)}");
        }

        /// <summary>Builds the drop-down list of tracks offered for each station.</summary>
        private static void ScanFiles()
        {
            AvailableFiles.Clear();
            AvailableFiles.Add("");
            try
            {
                var names = new List<string>();
                foreach (var f in Directory.EnumerateFiles(MusicFolder))
                {
                    var ext = Path.GetExtension(f).ToLowerInvariant();
                    if (ext == ".ogg" || ext == ".wav" || ext == ".mp3") names.Add(Path.GetFileName(f));
                }
                names.Sort(StringComparer.OrdinalIgnoreCase);
                AvailableFiles.AddRange(names);
            }
            catch (Exception e) { Report("scan music folder", e); }
        }

        private static string Register(MusicGroup group)
        {
            if (group == null) return null;
            if (GroupToKey.TryGetValue(group.Pointer, out var existing)) return existing;

            var raw = group.name ?? "";
            var label = Regex.Replace(raw.Replace("musicGroup_", ""), "[^A-Za-z0-9 _-]", "").Trim();
            if (label.Length == 0) label = "unknown";

            var index = -1;
            try { index = FmRadioManager.GetIndex(group); } catch { }

            var key = index >= 0 ? $"Station {index + 1} ({label})" : $"Station ({label})";
            for (var n = 2; UsedKeys.Contains(key); n++) key = $"Station {index + 1} ({label}) {n}";

            var entry = Plugin.Cfg.Bind(StationSection, key, "",
                new ConfigDescription(
                    "Track for this station. Leave empty for the game's original music.",
                    new AcceptableValueList<string>(AvailableFiles.ToArray())));

            UsedKeys.Add(key);
            Stations[key] = entry;
            GroupToKey[group.Pointer] = key;
            QueueLoad(key);
            return key;
        }

        // =====================================================================
        // Loading
        // =====================================================================

        private static void QueueLoad(string key)
        {
            for (var i = PendingLoads.Count - 1; i >= 0; i--)
            {
                if (PendingLoads[i].Key != key) continue;
                try { PendingLoads[i].Request.Abort(); PendingLoads[i].Request.Dispose(); } catch { }
                PendingLoads.RemoveAt(i);
            }

            var value = (Stations[key].Value ?? "").Trim().Trim('"');
            if (value.Length == 0)
            {
                DropClip(key);
                if (Verbose) Plugin.L.LogInfo($"[{key}] original music");
                return;
            }

            var path = Path.IsPathRooted(value) ? value : Path.Combine(MusicFolder, value);
            if (!File.Exists(path))
            {
                DropClip(key);
                Plugin.L.LogWarning($"[{key}] file not found: {value} - playing original music");
                return;
            }

            var type = Path.GetExtension(path).ToLowerInvariant() switch
            {
                ".ogg" => AudioType.OGGVORBIS,
                ".wav" => AudioType.WAV,
                ".mp3" => AudioType.MPEG,
                _ => AudioType.UNKNOWN
            };
            if (type == AudioType.UNKNOWN)
            {
                DropClip(key);
                Plugin.L.LogWarning($"[{key}] unsupported format: {value} (use .ogg, .wav or .mp3) - playing original music");
                return;
            }

            var request = UnityWebRequestMultimedia.GetAudioClip(new Uri(path).AbsoluteUri, type);
            request.SendWebRequest();
            PendingLoads.Add(new Pending { Key = key, File = value, Request = request });
        }

        private static void DropClip(string key)
        {
            if (!Clips.Remove(key, out var old)) return;
            ToDestroy.Add(old);
            ClearOverrides();
        }

        private static void FinishLoads()
        {
            for (var i = PendingLoads.Count - 1; i >= 0; i--)
            {
                var p = PendingLoads[i];
                if (!p.Request.isDone) continue;
                PendingLoads.RemoveAt(i);

                try
                {
                    if (p.Request.result != UnityWebRequest.Result.Success)
                    {
                        DropClip(p.Key);
                        Plugin.L.LogWarning($"[{p.Key}] {p.File} failed to load ({p.Request.error}) - playing original music");
                        continue;
                    }

                    var clip = DownloadHandlerAudioClip.GetContent(p.Request);
                    if (clip == null || clip.length <= 0f)
                    {
                        DropClip(p.Key);
                        Plugin.L.LogWarning($"[{p.Key}] {p.File} could not be decoded - playing original music");
                        continue;
                    }

                    clip.name = "BigRadio_" + p.Key;
                    clip.hideFlags = HideFlags.DontUnloadUnusedAsset;
                    if (Clips.TryGetValue(p.Key, out var old)) ToDestroy.Add(old);
                    Clips[p.Key] = clip;
                    ClearOverrides();
                    Plugin.L.LogInfo($"[{p.Key}] {p.File} loaded ({FormatTime(clip.length)})");
                }
                finally
                {
                    p.Request.Dispose();
                }
            }

            if (PendingLoads.Count == 0 && ToDestroy.Count > 0)
            {
                foreach (var c in ToDestroy)
                    if (c != null) UnityEngine.Object.Destroy(c);
                ToDestroy.Clear();
            }
        }

        private static string FormatTime(float seconds)
        {
            var t = TimeSpan.FromSeconds(seconds);
            return t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");
        }

        private static void ClearOverrides()
        {
            Primary.Clear();
            Muted.Clear();
        }

        private static void Reload()
        {
            Plugin.Cfg.Reload();
            ApplyGeneralSettings();
            if (_shuffleCfg != null && _shuffleCfg.Value) Shuffle();
            Plugin.L.LogInfo($"Reloading {Stations.Count} stations from config...");
            foreach (var key in new List<string>(Stations.Keys))
                QueueLoad(key);
            ClearOverrides();
        }

        /// <summary>Deals every file in the music folder out to a random station, one file per station.</summary>
        private static void Shuffle()
        {
            if (Stations.Count == 0) return;

            var files = new List<string>();
            foreach (var f in AvailableFiles)
            {
                if (f.Length == 0) continue;
                try { if (File.Exists(Path.Combine(MusicFolder, f))) files.Add(f); } catch { }
            }

            for (var i = files.Count - 1; i > 0; i--)
            {
                var j = Rng.Next(i + 1);
                (files[i], files[j]) = (files[j], files[i]);
            }

            var keys = new List<string>(Stations.Keys);
            for (var i = keys.Count - 1; i > 0; i--)
            {
                var j = Rng.Next(i + 1);
                (keys[i], keys[j]) = (keys[j], keys[i]);
            }

            var dealt = 0;
            foreach (var key in keys)
            {
                var value = dealt < files.Count ? files[dealt] : "";
                Stations[key].Value = value;
                if (value.Length > 0) dealt++;
            }

            Plugin.L.LogInfo(files.Count == 0
                ? "Shuffle: no audio files in the music folder."
                : $"Shuffled {dealt} track(s) across {keys.Count} stations.");
        }

        // =====================================================================
        // Per-frame
        // =====================================================================

        internal static void Tick() => Safe(TickAction, "Tick");

        private static void TickInner()
        {
            var down = _reloadVk != 0 && (GetAsyncKeyState(_reloadVk) & 0x8000) != 0;
            if (down && !_keyWasDown && Application.isFocused) Reload();
            _keyWasDown = down;

            if (PendingLoads.Count > 0 || ToDestroy.Count > 0) FinishLoads();

            if (Clips.Count == 0) return;
            if (Time.unscaledTime < _nextPoll) return;
            _nextPoll = Time.unscaledTime + PollInterval;
            Poll();
        }

        /// <summary>
        /// The radio's own tuning method gets inlined by IL2CPP, so instead of hooking it
        /// we look at the live music players a few times per second and take over the ones
        /// whose station is mapped to a custom file.
        /// </summary>
        private static void Poll()
        {
            var found = UnityEngine.Object.FindObjectsOfType(Il2CppType.Of<MusicPlayer>());
            if (found == null) return;

            ByKey.Clear();

            foreach (var o in found)
            {
                if (o == null) continue;
                var mp = o.TryCast<MusicPlayer>();
                if (mp == null) continue;

                var group = mp.MusicConfig;
                string key = null;
                if (group != null) GroupToKey.TryGetValue(group.Pointer, out key);

                if (key == null || !Clips.ContainsKey(key))
                {
                    Release(mp);
                    continue;
                }

                if (!ByKey.TryGetValue(key, out var list))
                {
                    list = new List<MusicPlayer>();
                    ByKey[key] = list;
                }
                list.Add(mp);
            }

            foreach (var kv in ByKey)
                HandleStation(kv.Key, kv.Value);
        }

        private static void HandleStation(string key, List<MusicPlayer> players)
        {
            if (!Clips.TryGetValue(key, out var clip) || clip == null) return;

            // Each radio in the world is its own group of stems: the lowest index
            // there carries the custom track, its siblings are silenced.
            var perRadio = new Dictionary<IntPtr, List<MusicPlayer>>();
            foreach (var mp in players)
            {
                IntPtr owner;
                try
                {
                    var t = mp.transform != null ? mp.transform.parent : null;
                    owner = t != null ? t.Pointer : mp.Pointer;
                }
                catch { owner = mp.Pointer; }

                if (!perRadio.TryGetValue(owner, out var siblings))
                {
                    siblings = new List<MusicPlayer>();
                    perRadio[owner] = siblings;
                }
                siblings.Add(mp);
            }

            foreach (var radio in perRadio.Values)
            {
                MusicPlayer main = null;
                var best = int.MaxValue;
                foreach (var mp in radio)
                {
                    int idx;
                    try { idx = mp.Index; } catch { idx = 0; }
                    if (idx >= best) continue;
                    best = idx;
                    main = mp;
                }
                if (main == null) continue;

                foreach (var mp in radio)
                {
                    if (mp.Pointer == main.Pointer) Takeover(mp, key, clip);
                    else Silence(mp, key);
                }
            }
        }

        private static void Takeover(MusicPlayer mp, string key, AudioClip clip)
        {
            var ptr = mp.Pointer;
            AudioSourceController asc;
            try { asc = mp.ASC; } catch { return; }
            if (asc == null) return;

            if (Primary.TryGetValue(ptr, out var current))
            {
                if (!asc.IsPlaying && !asc.IsHibernating)
                {
                    // The radio was switched off or moved out of range - hand it back.
                    Primary.Remove(ptr);
                    return;
                }

                var needsRestart = current.Pointer != clip.Pointer;
                if (!needsRestart)
                {
                    var live = asc.Clip;
                    needsRestart = live == null || live.Pointer != clip.Pointer;
                }
                if (needsRestart)
                {
                    Primary[ptr] = clip;
                    PlayOurs(mp, clip);
                    if (Verbose) Plugin.L.LogInfo($"[{key}] restarted custom track");
                }
                EnsureLoop(mp);
                return;
            }

            // Only step in once the game itself is playing this station.
            if (!asc.IsPlaying) return;

            Primary[ptr] = clip;
            PlayOurs(mp, clip);
            EnsureLoop(mp);
            Plugin.L.LogInfo($"[{key}] custom track now playing on this radio");
        }

        private static void Silence(MusicPlayer mp, string key)
        {
            var ptr = mp.Pointer;
            if (!Muted.Add(ptr)) return;
            try { mp.Stop(0.25f); } catch (Exception e) { Report("stop stem", e); }
            if (Verbose) Plugin.L.LogInfo($"[{key}] silenced an extra stem");
        }

        private static void Release(MusicPlayer mp)
        {
            var ptr = mp.Pointer;
            var wasPrimary = Primary.Remove(ptr);
            var wasMuted = Muted.Remove(ptr);
            if (!wasPrimary && !wasMuted) return;

            try
            {
                var asc = mp.ASC;
                if (asc != null && !asc.IsPlaying) mp.Play(null);
            }
            catch (Exception e) { Report("restore original", e); }
        }

        private static void PlayOurs(MusicPlayer mp, AudioClip clip)
        {
            if (_inOwnPlay) return;
            _inOwnPlay = true;
            try { mp.Play(clip); }
            catch (Exception e) { Report("play custom clip", e); }
            finally { _inOwnPlay = false; }
        }

        private static void EnsureLoop(MusicPlayer mp)
        {
            try
            {
                var asc = mp.ASC;
                if (asc == null) return;
                if (!asc._loop) asc._loop = true;
                var src = asc.AudioSource;
                if (src != null && !src.loop) src.loop = true;
            }
            catch (Exception e) { Report("loop", e); }
        }

        // =====================================================================
        // MusicPlayer hooks (safety net - these keep the game from undoing us)
        // =====================================================================

        internal static bool IsOverridden(MusicPlayer mp)
        {
            var p = mp.Pointer;
            return Primary.ContainsKey(p) || Muted.Contains(p);
        }

        internal static bool OnPlay(MusicPlayer mp, AudioClip clipOverride)
        {
            var ptr = mp.Pointer;
            if (Muted.Contains(ptr)) return false;
            if (!Primary.TryGetValue(ptr, out var clip)) return true;
            if (clip == null) { Primary.Remove(ptr); return true; }
            if (_inOwnPlay) return true;
            if (!(clipOverride is null) && clipOverride.Pointer == clip.Pointer) return true;

            PlayOurs(mp, clip);
            EnsureLoop(mp);
            return false;
        }

        internal static void OnManualUpdate(MusicPlayer mp)
        {
            if (!Primary.ContainsKey(mp.Pointer)) return;
            EnsureLoop(mp);
        }

        internal static void Forget(MusicPlayer mp)
        {
            var ptr = mp.Pointer;
            Primary.Remove(ptr);
            Muted.Remove(ptr);
        }

        // =====================================================================
        // Error handling
        // =====================================================================

        internal static void Safe(Action action, string where)
        {
            try { action(); }
            catch (Exception e) { Report(where, e); }
        }

        internal static void Report(string where, Exception e)
        {
            var msg = $"{where}: {e.GetType().Name}: {e.Message}";
            if (Reported.Add(msg)) Plugin.L.LogError(msg + Environment.NewLine + e.StackTrace);
        }
    }
}
