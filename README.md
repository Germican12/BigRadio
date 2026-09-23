# BigRadio

Swap the music on each in-game FM radio station for your own songs.

- One track per station, looping.
- Plays through the radio itself: distance, walls and the radio sound all still apply.
- Stations you leave empty keep their original music.
- **F8 reshuffles**: every track in your music folder is dealt out to a random station, never the same file twice in one deal.
- **Client-side.** Only you hear your songs. Nobody else needs the mod; other players hear the original stations.
- No music is included. You bring your own files.

## Install

Install with Thunderstore Mod Manager, r2modman or Gale. BepInExPack IL2CPP is installed automatically.

## Add your music

1. Start the game through your mod manager and load into a walk once, then quit. This creates the station list.
2. Open your profile folder in the mod manager (r2modman / Thunderstore Mod Manager: *Settings -> Locations -> Browse profile folder*).
3. Put your .ogg, .wav or .mp3 files into BepInEx/config/BigRadio/.
4. Restart the game so the new files appear in the station drop-downs.
5. Either press **F8** in-game to deal them out at random, or pick a track per station yourself in the mod settings menu.

Each station is listed by its dial position, e.g. Station 1 (bobby) ... Station 7 (Bristol). Stations you have not unlocked in-game still play static, mod or not.

## Config

| Setting | Default | What it does |
|---|---|---|
| General.Music Folder | BepInEx/config/BigRadio in your profile | Folder holding your tracks. |
| General.Reload Key | F8 | Reloads the config in-game. F1-F24 or a letter/digit. |
| General.Shuffle On Reload | true | Reload key deals your tracks out to random stations. Turn off to keep a hand-picked list. |
| General.Verbose Logging | false | Logs every radio change. Turn on for bug reports. |
| Stations.Station N (name) | *(empty)* | Track for that station. Empty = original music. |

## Known limits

- Client-side only: other players hear the original stations, even the host's.
- New files added to the folder need a game restart before they show up in the drop-downs. F8 shuffle picks them up either way.
- Avoid apostrophes in file names; they get escaped in the config and the station may not match.

## Troubleshooting

Check BepInEx/LogOutput.log in your profile. Healthy lines say "Found 7 radio stations", then "loaded (3:42)" for each mapped track, then "custom track now playing on this radio" once you tune in.

- file not found: check the file name in the config.
- failed to load / could not be decoded: re-export as .ogg (Vorbis) or 16-bit .wav.
- Station silent: that station may be locked in your save, or the radio is out of earshot.

## Notes

- Please don't report bugs to House House while using mods. Reproduce without mods first.
- Don't redistribute copyrighted music with this mod or in modpacks.

## License

MIT