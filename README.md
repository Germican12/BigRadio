# BigRadio

Swap the music on each in-game FM radio station for your own songs.

- One file per station, looping.
- Plays through the radio itself: distance, walls and the radio sound all still apply.
- Stations you leave empty keep their original music.
- **Client-side.** Only you hear your songs. Nobody else needs the mod; other players hear the original stations.
- No music is included. You bring your own files.

## Install

Install with **Thunderstore Mod Manager**, **r2modman** or **Gale**. BepInExPack IL2CPP is installed automatically.

## Add your music

1. Start the game through your mod manager and load into a walk once, then quit. This creates the station list.
2. Open your profile folder in the mod manager (r2modman / Thunderstore Mod Manager: *Settings → Locations → Browse profile folder*).
3. Put your `.ogg`, `.wav` or `.mp3` files into `BepInEx/config/BigRadio/`.
4. Open the config `ArtemisCorp.BigRadio.cfg` (mod manager: *Config editor*). Under `[Stations]`, write a file name next to a station:

```ini
[Stations]
DanceFm = my favourite song.ogg
Kosmische = D:\Music\ambient loop.mp3
SleuthFm =
```

5. Start the game. While playing, press **F8** after editing the config to reload without restarting.

Station names come from the game files, so they may look slightly different from the example.

## Config

| Setting | Default | What it does |
|---|---|---|
| `General.MusicFolder` | *(empty)* | Folder for your files. Empty = `BepInEx/config/BigRadio` in your profile. |
| `General.ReloadKey` | `F8` | Reloads config and music in-game. F1–F24 or a letter/digit. |
| `General.VerboseLogging` | `false` | Logs every radio change. Turn on for bug reports. |
| `Stations.<name>` | *(empty)* | File name (in the music folder) or full path. Empty = original music. |

## Troubleshooting

Check `BepInEx/LogOutput.log` in your profile. Healthy lines look like:

```
[Info   :  BigRadio] Found 10 radio stations: ...
[Info   :  BigRadio] [DanceFm] my favourite song.ogg loaded (3:42)
```

- `file not found` – check the file name and extension in the config.
- `failed to load` / `could not be decoded` – re-export the file as `.ogg` (Vorbis) or 16-bit `.wav`.
- `BigRadio is disabled` – a Big Walk update changed the radio code. Radios play their original music until BigRadio is updated.

## Notes

- Please don't report bugs to House House while using mods. Reproduce without mods first.
- Don't redistribute copyrighted music with this mod or in modpacks.

## License

MIT
