using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GameReaderCommon;
using SimHub.Plugins;

namespace SimHub.Plugin.Beefweb
{
    /// <summary>
    /// SimHub plugin that talks to a Foobar2000 instance running the Beefweb
    /// HTTP remote control component. Media info is exposed as SimHub
    /// properties; playback controls are exposed both as SimHub actions
    /// (so they can be mapped to a button/wheel input in Controls) and as
    /// SimHub events of the same name (so they can be triggered from
    /// anywhere else an event trigger is accepted).
    /// </summary>
    [PluginDescription("Reads Foobar2000 media info and exposes playback controls through the Beefweb HTTP plugin")]
    [PluginAuthor("Claude.ai")]
    [PluginName("Beefweb Media Control")]
    public class BeefwebPlugin : IPlugin, IDataPlugin, IWPFSettingsV2
    {
        internal const string SettingsFileName = "BeefwebPluginSettings";

        public PluginManager PluginManager { get; set; }

        internal BeefwebPluginSettings Settings { get; private set; }

        internal readonly BeefwebClient Client = new BeefwebClient();

        private Timer _pollTimer;
        private volatile bool _isPolling;
        private volatile MediaSnapshot _snapshot = new MediaSnapshot();

        private Dictionary<string, PlaylistInfo> _playlistCache = new Dictionary<string, PlaylistInfo>();
        private string _lastArtworkKey;
        private int _pollCount;

        private readonly string _artworkCachePath =
            Path.Combine(Path.GetTempPath(), "SimHub.Plugin.Beefweb", "cover.jpg");

        private ImageSource _pictureIcon;

        public ImageSource PictureIcon => _pictureIcon;

        public string LeftMenuTitle => "Beefweb Media Control";

        public void Init(PluginManager pluginManager)
        {
            PluginManager = pluginManager;

            Settings = this.ReadCommonSettings<BeefwebPluginSettings>(SettingsFileName, () => new BeefwebPluginSettings());

            Directory.CreateDirectory(Path.GetDirectoryName(_artworkCachePath));

            ApplySettings();
            AttachProperties();
            AttachActions();
            RefreshPluginIcon();

            _pollTimer = new Timer(OnPollTimerElapsed, null, 0, Math.Max(100, Settings.PollingIntervalMs));
        }

        public void End(PluginManager pluginManager)
        {
            _pollTimer?.Dispose();
            _pollTimer = null;
            Client.Dispose();
            this.SaveCommonSettings(SettingsFileName, Settings);
        }

        public void DataUpdate(PluginManager pluginManager, ref GameData data)
        {
            // Media info is refreshed on its own timer (see OnPollTimerElapsed), not on the
            // game data tick, so there is nothing to do here. The property delegates attached
            // in AttachProperties always read the latest cached snapshot directly.
        }

        public Control GetWPFSettingsControl(PluginManager pluginManager)
        {
            return new SettingsControl(this);
        }

        /// <summary>Re-applies the current Settings to the HTTP client and polling timer. Called on load and after Save.</summary>
        internal void ApplySettings()
        {
            try
            {
                Client.Configure(Settings);
            }
            catch (Exception ex)
            {
                SimHub.Logging.Current.Info($"[Beefweb] Invalid connection settings: {ex.Message}");
                _snapshot = new MediaSnapshot { Connected = false, LastError = $"Invalid settings: {ex.Message}" };
            }

            _pollTimer?.Change(0, Math.Max(100, Settings.PollingIntervalMs));
        }

        internal void SaveSettings()
        {
            this.SaveCommonSettings(SettingsFileName, Settings);
            ApplySettings();
        }

        /// <summary>Human readable one-line summary of the current snapshot, for the settings screen.</summary>
        internal string GetNowPlayingSummary()
        {
            var s = _snapshot;
            if (!s.Connected)
            {
                return string.IsNullOrEmpty(s.LastError) ? "Not connected." : $"Not connected. {s.LastError}";
            }

            if (string.IsNullOrEmpty(s.Title))
            {
                return $"Connected. Playback state: {s.PlaybackState}.";
            }

            return $"{s.PlaybackState}: {s.Artist} - {s.Title} ({FormatTime(s.Position)} / {FormatTime(s.Duration)}), playlist: {s.PlaylistName}";
        }

        /// <summary>
        /// Loads the plugin's list icon from the foobar2000 logo embedded in this assembly
        /// (Resources/foobar2000_icon.png). See the EmbeddedResource entry in the .csproj
        /// for where that file comes from and why it can be embedded here.
        /// </summary>
        private void RefreshPluginIcon()
        {
            _pictureIcon = LoadEmbeddedIcon();
        }

        private static ImageSource LoadEmbeddedIcon()
        {
            try
            {
                var assembly = typeof(BeefwebPlugin).Assembly;
                var resourceName = assembly.GetManifestResourceNames()
                    .FirstOrDefault(n => n.EndsWith("foobar2000_icon.png", StringComparison.OrdinalIgnoreCase));

                if (resourceName == null)
                {
                    return null;
                }

                using (var stream = assembly.GetManifestResourceStream(resourceName))
                {
                    if (stream == null)
                    {
                        return null;
                    }

                    var decoder = new PngBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                    var frame = decoder.Frames[0];
                    frame.Freeze();
                    return frame;
                }
            }
            catch
            {
                // Best effort only; a missing/unreadable resource just leaves SimHub's default plugin icon in place.
                return null;
            }
        }

        internal async Task<string> TestConnectionAsync()
        {
            try
            {
                var state = await Client.GetPlayerStateAsync().ConfigureAwait(false);
                if (state == null)
                {
                    return "Connected, but the server returned no player state.";
                }

                return $"Connected to {state.Info?.Name} {state.Info?.Version}. Playback state: {state.PlaybackState}.";
            }
            catch (Exception ex)
            {
                return $"Connection failed. {ex.Message}";
            }
        }

        // --- Properties ---

        private void AttachProperties()
        {
            this.AttachDelegate("Connected", () => _snapshot.Connected);
            this.AttachDelegate("PlaybackState", () => _snapshot.PlaybackState);
            this.AttachDelegate("IsPlaying", () => _snapshot.PlaybackState == "playing");
            this.AttachDelegate("IsPaused", () => _snapshot.PlaybackState == "paused");
            this.AttachDelegate("IsStopped", () => _snapshot.PlaybackState == "stopped");

            this.AttachDelegate("Artist", () => _snapshot.Artist);
            this.AttachDelegate("Title", () => _snapshot.Title);
            this.AttachDelegate("Album", () => _snapshot.Album);
            this.AttachDelegate("AlbumArtist", () => _snapshot.AlbumArtist);
            this.AttachDelegate("TrackNumber", () => _snapshot.TrackNumber);
            this.AttachDelegate("Genre", () => _snapshot.Genre);
            this.AttachDelegate("Year", () => _snapshot.Year);
            this.AttachDelegate("Codec", () => _snapshot.Codec);
            this.AttachDelegate("Bitrate", () => _snapshot.Bitrate);
            this.AttachDelegate("SampleRate", () => _snapshot.SampleRate);
            this.AttachDelegate("FilePath", () => _snapshot.FilePath);

            this.AttachDelegate("Position", () => _snapshot.Position);
            this.AttachDelegate("Duration", () => _snapshot.Duration);
            this.AttachDelegate("PositionText", () => FormatTime(_snapshot.Position));
            this.AttachDelegate("DurationText", () => FormatTime(_snapshot.Duration));
            this.AttachDelegate("Progress", () => _snapshot.Duration > 0 ? _snapshot.Position / _snapshot.Duration : 0d);

            this.AttachDelegate("Volume", () => _snapshot.Volume);
            this.AttachDelegate("IsMuted", () => _snapshot.IsMuted);

            this.AttachDelegate("PlaylistId", () => _snapshot.PlaylistId);
            this.AttachDelegate("PlaylistName", () => _snapshot.PlaylistName);
            this.AttachDelegate("PlaylistItemIndex", () => _snapshot.PlaylistItemIndex);
            this.AttachDelegate("PlaylistItemCount", () => _snapshot.PlaylistItemCount);

            this.AttachDelegate("ServerName", () => _snapshot.ServerName);
            this.AttachDelegate("ServerVersion", () => _snapshot.ServerVersion);

            this.AttachDelegate("AlbumArtPath", () => _snapshot.AlbumArtPath);
            this.AttachDelegate("HasAlbumArt", () => _snapshot.HasAlbumArt);

            this.AttachDelegate("LastError", () => _snapshot.LastError);
        }

        private static string FormatTime(double seconds)
        {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0)
            {
                seconds = 0;
            }

            return TimeSpan.FromSeconds(seconds).ToString(@"mm\:ss");
        }

        // --- Actions and events (playback controls) ---

        private void AttachActions()
        {
            AddControl("Play", () => Client.PlayAsync());
            AddControl("Pause", () => Client.PauseAsync());
            AddControl("PlayPauseToggle", () => Client.PauseToggleAsync());
            AddControl("Stop", () => Client.StopAsync());

            AddControl("NextTrack", () => Client.NextTrackAsync());
            AddControl("PreviousTrack", () => Client.PreviousTrackAsync());

            AddControl("SeekForward", () => Client.SeekAsync(_snapshot.Position + Settings.SeekStepSeconds));
            AddControl("SeekBackward", () => Client.SeekAsync(_snapshot.Position - Settings.SeekStepSeconds));

            AddControl("VolumeUp", () => Client.SetVolumeAsync(Math.Min(_snapshot.VolumeMax, _snapshot.Volume + Settings.VolumeStepPercent)));
            AddControl("VolumeDown", () => Client.SetVolumeAsync(Math.Max(_snapshot.VolumeMin, _snapshot.Volume - Settings.VolumeStepPercent)));
            AddControl("ToggleMute", () => Client.SetMuteAsync(!_snapshot.IsMuted));

            AddControl("NextPlaylist", () => ChangePlaylistAsync(1));
            AddControl("PreviousPlaylist", () => ChangePlaylistAsync(-1));
        }

        /// <summary>
        /// Registers one playback control as both a mappable SimHub action and a same-named
        /// SimHub event, and fires the event once the action completes without error.
        /// </summary>
        private void AddControl(string name, Func<Task> action)
        {
            this.AddEvent(name);
            this.AddAction(name, async (a, b) =>
            {
                try
                {
                    await action().ConfigureAwait(false);
                    this.TriggerEvent(name);
                }
                catch (Exception ex)
                {
                    SimHub.Logging.Current.Info($"[Beefweb] Control '{name}' failed: {ex.Message}");

                    // Replace the snapshot wholesale (same pattern PollAsync uses) rather than
                    // mutating a field on whatever instance _snapshot currently references -
                    // that instance may be concurrently replaced by the poll timer thread.
                    var copy = _snapshot.Clone();
                    copy.LastError = $"{name}: {ex.Message}";
                    _snapshot = copy;
                }
            });
        }

        private async Task ChangePlaylistAsync(int direction)
        {
            var playlists = await Client.GetPlaylistsAsync().ConfigureAwait(false);
            if (playlists?.Playlists == null || playlists.Playlists.Count == 0)
            {
                return;
            }

            var currentIndex = playlists.Playlists.FindIndex(p => p.IsCurrent);
            if (currentIndex < 0)
            {
                currentIndex = 0;
            }

            var count = playlists.Playlists.Count;
            var nextIndex = ((currentIndex + direction) % count + count) % count;

            // Beefweb has no "switch to this playlist" endpoint that affects playback on its own;
            // starting playback at the first item of the target playlist is what actually moves
            // playback there. player/next and player/previous only step within the currently
            // played playlist.
            await Client.PlayItemAsync(playlists.Playlists[nextIndex].Id, 0).ConfigureAwait(false);
        }

        // --- Polling loop ---

        private void OnPollTimerElapsed(object state)
        {
            if (_isPolling)
            {
                return;
            }

            _isPolling = true;
            _ = PollAsync();
        }

        private async Task PollAsync()
        {
            try
            {
                var playerState = await Client.GetPlayerStateAsync().ConfigureAwait(false);
                var snapshot = BuildSnapshot(playerState);
                _pollCount++;

                var activeItem = playerState?.ActiveItem;

                var needsPlaylistRefresh = _pollCount % 10 == 0
                    || (activeItem != null && !_playlistCache.ContainsKey(activeItem.PlaylistId));

                if (needsPlaylistRefresh)
                {
                    await RefreshPlaylistCacheAsync().ConfigureAwait(false);
                }

                if (activeItem != null)
                {
                    snapshot.PlaylistId = activeItem.PlaylistId ?? "";
                    snapshot.PlaylistItemIndex = activeItem.Index;

                    if (_playlistCache.TryGetValue(activeItem.PlaylistId ?? "", out var info))
                    {
                        snapshot.PlaylistName = info.Title;
                        snapshot.PlaylistItemCount = info.ItemCount;
                    }

                    var artworkKey = $"{activeItem.PlaylistId}:{activeItem.Index}";
                    if (artworkKey != _lastArtworkKey)
                    {
                        _lastArtworkKey = artworkKey;
                        await UpdateArtworkAsync(activeItem, snapshot).ConfigureAwait(false);
                    }
                    else
                    {
                        snapshot.AlbumArtPath = _snapshot.AlbumArtPath;
                        snapshot.HasAlbumArt = _snapshot.HasAlbumArt;
                    }
                }
                else
                {
                    _lastArtworkKey = null;
                }

                snapshot.Connected = true;
                _snapshot = snapshot;
            }
            catch (Exception ex)
            {
                _snapshot = new MediaSnapshot { Connected = false, LastError = ex.Message };
            }
            finally
            {
                _isPolling = false;
            }
        }

        private async Task RefreshPlaylistCacheAsync()
        {
            var playlists = await Client.GetPlaylistsAsync().ConfigureAwait(false);
            if (playlists?.Playlists == null)
            {
                return;
            }

            var map = new Dictionary<string, PlaylistInfo>();
            foreach (var p in playlists.Playlists)
            {
                map[p.Id] = p;
            }

            _playlistCache = map;
        }

        private MediaSnapshot BuildSnapshot(PlayerState state)
        {
            var snapshot = new MediaSnapshot();
            if (state == null)
            {
                return snapshot;
            }

            snapshot.PlaybackState = state.PlaybackState ?? "stopped";
            snapshot.Position = state.ActiveItem?.Position ?? 0;
            snapshot.Duration = state.ActiveItem?.Duration ?? 0;
            snapshot.Volume = state.Volume?.Value ?? 0;
            snapshot.VolumeMin = state.Volume?.Min ?? 0;
            snapshot.VolumeMax = state.Volume?.Max ?? 100;
            snapshot.IsMuted = state.Volume?.IsMuted ?? false;
            snapshot.ServerName = state.Info?.Name ?? "";
            snapshot.ServerVersion = state.Info?.Version ?? "";

            var columns = state.ActiveItem?.Columns;
            string Col(int i) => columns != null && i < columns.Count ? columns[i] ?? "" : "";

            snapshot.Artist = Col(MediaColumns.Artist);
            snapshot.Title = Col(MediaColumns.Title);
            snapshot.Album = Col(MediaColumns.Album);
            snapshot.AlbumArtist = Col(MediaColumns.AlbumArtist);
            snapshot.TrackNumber = Col(MediaColumns.TrackNumber);
            snapshot.Genre = Col(MediaColumns.Genre);
            snapshot.Year = Col(MediaColumns.Date);
            snapshot.Codec = Col(MediaColumns.Codec);
            snapshot.Bitrate = Col(MediaColumns.Bitrate);
            snapshot.SampleRate = Col(MediaColumns.SampleRate);
            snapshot.FilePath = Col(MediaColumns.FilePath);

            return snapshot;
        }

        private async Task UpdateArtworkAsync(ActiveItem item, MediaSnapshot snapshot)
        {
            try
            {
                var bytes = await Client.GetArtworkAsync(item.PlaylistId, item.Index).ConfigureAwait(false);
                if (bytes != null && bytes.Length > 0)
                {
                    // Write to a temp file and swap it in, rather than overwriting cover.jpg in
                    // place, so a concurrent reader (e.g. SimHub's own UI) never sees a partial file.
                    var tempPath = _artworkCachePath + ".tmp";
                    File.WriteAllBytes(tempPath, bytes);
                    if (File.Exists(_artworkCachePath))
                    {
                        File.Replace(tempPath, _artworkCachePath, null);
                    }
                    else
                    {
                        File.Move(tempPath, _artworkCachePath);
                    }

                    snapshot.AlbumArtPath = _artworkCachePath;
                    snapshot.HasAlbumArt = true;
                }
                else
                {
                    snapshot.AlbumArtPath = "";
                    snapshot.HasAlbumArt = false;
                }
            }
            catch
            {
                snapshot.AlbumArtPath = "";
                snapshot.HasAlbumArt = false;
            }
        }

        /// <summary>Point-in-time view of everything the property delegates read. Replaced wholesale on each poll.</summary>
        private class MediaSnapshot
        {
            public bool Connected;
            public string PlaybackState = "stopped";
            public string Artist = "";
            public string Title = "";
            public string Album = "";
            public string AlbumArtist = "";
            public string TrackNumber = "";
            public string Genre = "";
            public string Year = "";
            public string Codec = "";
            public string Bitrate = "";
            public string SampleRate = "";
            public string FilePath = "";
            public double Position;
            public double Duration;
            public double Volume;
            public double VolumeMin;
            public double VolumeMax = 100;
            public bool IsMuted;
            public string PlaylistId = "";
            public string PlaylistName = "";
            public int PlaylistItemIndex = -1;
            public int PlaylistItemCount;
            public string ServerName = "";
            public string ServerVersion = "";
            public string AlbumArtPath = "";
            public bool HasAlbumArt;
            public string LastError = "";

            public MediaSnapshot Clone() => (MediaSnapshot)MemberwiseClone();
        }
    }
}
