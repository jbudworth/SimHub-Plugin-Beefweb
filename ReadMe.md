# SimHub.Plugin.Beefweb

A SimHub plugin that reads media info from a Foobar2000 instance running the
**Beefweb** remote control component (https://github.com/hyperblast/beefweb),
and exposes it to SimHub as properties, plus a full set of playback controls
exposed both as mappable actions and as events of the same name.

## What you get

**Properties** (readable anywhere in SimHub: dashboards, formulas, etc.):

| Property | Type | Description |
|---|---|---|
| `Connected` | bool | Whether the last poll of Beefweb succeeded |
| `PlaybackState` | string | `playing`, `paused`, or `stopped` |
| `IsPlaying`, `IsPaused`, `IsStopped` | bool | Convenience flags for `PlaybackState` |
| `Artist`, `Title`, `Album`, `AlbumArtist` | string | Current track tags |
| `TrackNumber`, `Genre`, `Year` | string | Current track tags |
| `Codec`, `Bitrate`, `SampleRate` | string | Current track format info |
| `FilePath` | string | Full path of the file being played |
| `Position`, `Duration` | double | Seconds |
| `PositionText`, `DurationText` | string | Formatted as `mm:ss` |
| `Progress` | double | `Position / Duration`, 0 to 1 |
| `Volume` | double | Current volume, using whatever scale Beefweb reports |
| `IsMuted` | bool | Mute state |
| `PlaylistId`, `PlaylistName` | string | The playlist the current track belongs to |
| `PlaylistItemIndex`, `PlaylistItemCount` | int | Position within, and size of, that playlist |
| `ServerName`, `ServerVersion` | string | Reported by Beefweb itself |
| `AlbumArtPath` | string | Path to a cached copy of the current track's cover art (empty if none) |
| `HasAlbumArt` | bool | Whether `AlbumArtPath` currently points to a real file |
| `LastError` | string | Last connection error message, if any |

**Controls**, each registered twice, once as a SimHub action you can bind to
a button, key, or wheel input under Controls, and once as a same-named
SimHub event you can trigger any other way SimHub accepts an event trigger:

- `Play`, `Pause`, `PlayPauseToggle`, `Stop`
- `NextTrack`, `PreviousTrack`
- `SeekForward`, `SeekBackward` (jump by the configured seek step, default 10s)
- `VolumeUp`, `VolumeDown`, `ToggleMute` (volume step is configurable, default 5%)
- `NextPlaylist`, `PreviousPlaylist`

## Prerequisites

- foobar2000 with the **Beefweb** component installed and its HTTP server
  enabled (foobar2000 Preferences, Beefweb page). Note the port, default is
  `8880`. If you turned on authentication there, note the username/password
  too.
- Visual Studio 2022 (17.10 or later, for `.slnx` support) with the
  ".NET desktop development" workload.
- SimHub installed locally, since the project references
  `SimHub.Plugins.dll`, `SimHub.Logging.dll`, `GameReaderCommon.dll`,
  `MahApps.Metro.dll`, and `log4net.dll` straight from your SimHub install
  folder rather than from NuGet.

## Building

1. Open `SimHub.Plugin.Beefweb.slnx` in Visual Studio.
2. If SimHub is not installed at the default
   `C:\Program Files (x86)\SimHub`, either:
   - Add a `Directory.Build.props` file next to the `.slnx` with:
     ```xml
     <Project>
       <PropertyGroup>
         <SimHubInstallDir>C:\path\to\your\SimHub</SimHubInstallDir>
       </PropertyGroup>
     </Project>
     ```
   - Or pass `-p:SimHubInstallDir="C:\path\to\your\SimHub"` on the command
     line.
3. Build. `Newtonsoft.Json.dll` is restored from NuGet automatically and
   copied next to the plugin DLL; it is required at runtime alongside the
   plugin itself. `log4net.dll` is referenced straight from your SimHub
   install for compiling (see below) and is not shipped by this project.

A post-build step also copies the plugin DLL and its dependency into a
`build\` folder next to this ReadMe, for convenience.

## Installing into SimHub

A build target (`CopyToSimHub`) copies the built plugin DLL straight into
`$(SimHubInstallDir)` after every build, so in most cases you can skip the
manual copy below, just close SimHub before building so the file isn't
locked (the copy is set to continue on error, so a locked file won't fail
the build, it just won't have updated).

If you'd rather copy manually, or the automatic copy didn't run, close
SimHub, then copy these two files into SimHub's own installation folder
(the same folder your other `.dll` plugins live in, typically
`C:\Program Files (x86)\SimHub`):

- `SimHub.Plugin.Beefweb.dll`
- `Newtonsoft.Json.dll` (skip if SimHub already ships a compatible one)

`log4net.dll` is not shipped with this plugin, even though the project
references it at compile time (`SimHub.Logging`'s public API exposes
log4net's `ILog` type, so the compiler needs it even though this plugin
never calls log4net directly). That reference points at the copy already
sitting in your SimHub install folder and is marked as not-copied
(`Private=false`), so at runtime the plugin binds to whichever log4net.dll
SimHub itself is already using, at whatever version that is, rather than
introducing a second copy that could conflict with it.

Start SimHub, open Settings, Plugins, enable "Beefweb Media Control", then
open its settings page to set the host, port, and (if needed) credentials
that match your Beefweb configuration, and click "Test connection".

## A note on the Beefweb API

This plugin talks to Beefweb's documented REST endpoints (`GET /api/player`,
`GET /api/playlists`, `GET /api/artwork/{playlistId}/{index}`, and the
`POST`/`PUT` playback and state endpoints). The JSON field names and shapes
used in `BeefwebModels.cs` reflect Beefweb's own public API and documentation,
but this project could not be built and run against a live foobar2000/Beefweb
instance in the environment that produced it. If a property comes back empty
or a control does not respond as expected, the most likely cause is a small
mismatch between a field name here and what your specific Beefweb version
actually returns. Open `http://<host>:<port>/api/player` in a browser while a
track is playing, compare the JSON to `PlayerState` in `BeefwebModels.cs`, and
adjust the `[JsonProperty(...)]` names there if needed; nothing else in the
plugin needs to change.

## A note on the SimHub plugin SDK

This project could not be compiled against the real `SimHub.Plugins.dll` in
the environment that produced it, since that DLL only exists inside a SimHub
install. The plugin lifecycle (`IPlugin`, `IDataPlugin`, `IWPFSettingsV2`) and
the extension methods it relies on (`AttachDelegate`, `AddAction`, `AddEvent`,
`TriggerEvent`, `ReadCommonSettings`, `SaveCommonSettings`) follow the
conventions used by SimHub's own bundled demo plugin
(`User.PluginSdkDemo`, installed at
`C:\Program Files (x86)\SimHub\PluginSdk\User.PluginSdkDemo`) and by publicly
available community plugins. If Visual Studio reports any of these as
missing or with a different signature on your SimHub version, that demo
project is the fastest way to check the current exact names.

## Plugin icon

SimHub's plugin list shows an icon next to each plugin's name. This plugin
uses foobar2000's own logo for that (`Resources/foobar2000_icon.png`,
embedded in the plugin DLL). That file is sourced from Wikipedia
(https://en.wikipedia.org/wiki/File:Foobar2000_logo.png), where it's tagged
`{{PD-textlogo}}`: simple enough geometrically that it doesn't meet the
threshold of originality for copyright, and is therefore in the public
domain. That covers copyright only, not any separate trademark in the logo;
showing it here to identify the program this plugin talks to is the same
kind of use every third-party plugin or tool makes when displaying the logo
of whatever it integrates with.

## Notes on this build

- Media info is refreshed on its own background timer (default every 500ms,
  configurable), not tied to SimHub's game data tick, so it keeps updating
  even when no game is running.
- Album art is written to a temp file
  (`%TEMP%\SimHub.Plugin.Beefweb\cover.jpg`) and re-downloaded only when the
  playing track changes, not on every poll.
- `NextPlaylist`/`PreviousPlaylist` work by reading the playlist list from
  Beefweb, finding the current one, and switching to its neighbor; Beefweb
  itself has no single "next playlist" endpoint.
