using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace SimHub.Plugin.Beefweb
{
    /// <summary>
    /// Thin async wrapper around the Beefweb HTTP API exposed by the
    /// foo_beefweb Foobar2000 component. See https://github.com/hyperblast/beefweb
    /// for the upstream project. Every method here is a direct call to one
    /// REST endpoint, no caching or state tracking; that lives in BeefwebPlugin.
    /// </summary>
    public class BeefwebClient : IDisposable
    {
        private HttpClient _http;
        private string _baseUrl;

        public bool IsConfigured => _http != null;

        public void Configure(BeefwebPluginSettings settings)
        {
            if (string.IsNullOrWhiteSpace(settings.Host))
            {
                throw new ArgumentException("Host must not be empty.");
            }

            if (settings.Port <= 0 || settings.Port > 65535)
            {
                throw new ArgumentException("Port must be between 1 and 65535.");
            }

            var baseUrl = $"http://{settings.Host}:{settings.Port}/api";
            Uri baseUri;
            try
            {
                baseUri = new Uri(baseUrl + "/");
            }
            catch (UriFormatException ex)
            {
                throw new ArgumentException($"Invalid host/port: {ex.Message}", ex);
            }

            // Build and validate the replacement client fully before touching any field, so that
            // if something above throws, the previously configured (working) client is untouched.
            var newHttp = new HttpClient
            {
                BaseAddress = baseUri,
                Timeout = TimeSpan.FromMilliseconds(Math.Max(500, settings.RequestTimeoutMs))
            };

            if (settings.UseAuthentication && !string.IsNullOrEmpty(settings.Username))
            {
                var raw = Encoding.UTF8.GetBytes($"{settings.Username}:{settings.Password}");
                newHttp.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Basic", Convert.ToBase64String(raw));
            }

            var oldHttp = _http;
            _http = newHttp;
            _baseUrl = baseUrl;

            if (oldHttp != null)
            {
                // Don't dispose the old client immediately: a poll request already in flight may
                // still be using it, and disposing mid-request throws ObjectDisposedException on
                // that request. Give it the old timeout window to finish naturally first.
                var oldTimeout = oldHttp.Timeout;
                _ = Task.Delay(oldTimeout).ContinueWith(_ => oldHttp.Dispose());
            }
        }

        public async Task<PlayerState> GetPlayerStateAsync()
        {
            var url = $"player?columns={Uri.EscapeDataString(MediaColumns.QueryValue)}";
            var json = await _http.GetStringAsync(url).ConfigureAwait(false);
            var envelope = JsonConvert.DeserializeObject<PlayerStateEnvelope>(json);
            return envelope?.Player;
        }

        public async Task<PlaylistsResponse> GetPlaylistsAsync()
        {
            var json = await _http.GetStringAsync("playlists").ConfigureAwait(false);
            return JsonConvert.DeserializeObject<PlaylistsResponse>(json);
        }

        public async Task<byte[]> GetArtworkAsync(string playlistId, int index)
        {
            var url = $"artwork/{Uri.EscapeDataString(playlistId)}/{index}";
            using (var response = await _http.GetAsync(url).ConfigureAwait(false))
            {
                if (!response.IsSuccessStatusCode)
                {
                    return null;
                }

                return await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
            }
        }

        // --- Playback transport controls ---

        public Task PlayAsync() => PostAsync("player/play");

        public Task PlayItemAsync(string playlistId, int index) =>
            PostAsync($"player/play/{Uri.EscapeDataString(playlistId)}/{index}");

        public Task PauseAsync() => PostAsync("player/pause");

        public Task PauseToggleAsync() => PostAsync("player/pause/toggle");

        public Task StopAsync() => PostAsync("player/stop");

        public Task NextTrackAsync() => PostAsync("player/next");

        public Task PreviousTrackAsync() => PostAsync("player/previous");

        // --- Player state (volume, mute, seek position) ---

        public Task SeekAsync(double positionSeconds) =>
            SetPlayerStateAsync(new { position = Math.Max(0, positionSeconds) });

        public Task SetVolumeAsync(double value) =>
            SetPlayerStateAsync(new { volume = value });

        public Task SetMuteAsync(bool isMuted) =>
            SetPlayerStateAsync(new { isMuted });

        // --- Low level helpers ---

        private Task SetPlayerStateAsync(object body)
        {
            var json = JsonConvert.SerializeObject(body);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            return _http.PutAsync("player", content);
        }

        private async Task PostAsync(string relativeUrl)
        {
            var content = new StringContent(string.Empty);
            var response = await _http.PostAsync(relativeUrl, content).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
        }

        public void Dispose()
        {
            _http?.Dispose();
            _http = null;
        }
    }
}
