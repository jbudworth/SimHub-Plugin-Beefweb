using System.Collections.Generic;
using Newtonsoft.Json;

namespace SimHub.Plugin.Beefweb
{
    // These classes mirror the JSON shapes returned by the Beefweb plugin's
    // REST API (https://github.com/hyperblast/beefweb). Field names use the
    // same camelCase as the server; Newtonsoft.Json matches property names
    // case insensitively, and the explicit JsonProperty attributes below are
    // kept for clarity and as a safeguard against that default ever changing.
    //
    // If a future Beefweb release changes a field name or shape, check the
    // server's own response (GET http://host:port/api/player) and adjust the
    // matching class here; the rest of the plugin only depends on these
    // classes, not on the raw JSON.

    public class PlayerStateEnvelope
    {
        [JsonProperty("player")]
        public PlayerState Player { get; set; }
    }

    public class PlayerState
    {
        [JsonProperty("info")]
        public PlayerInfo Info { get; set; }

        [JsonProperty("playbackState")]
        public string PlaybackState { get; set; } // "playing" | "paused" | "stopped"

        [JsonProperty("playbackMode")]
        public int? PlaybackMode { get; set; }

        [JsonProperty("volume")]
        public VolumeInfo Volume { get; set; }

        [JsonProperty("activeItem")]
        public ActiveItem ActiveItem { get; set; }
    }

    public class PlayerInfo
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("title")]
        public string Title { get; set; }

        [JsonProperty("version")]
        public string Version { get; set; }

        [JsonProperty("pluginVersion")]
        public string PluginVersion { get; set; }
    }

    public class VolumeInfo
    {
        [JsonProperty("type")]
        public string Type { get; set; } // "linear" | "db"

        [JsonProperty("min")]
        public double Min { get; set; }

        [JsonProperty("max")]
        public double Max { get; set; }

        [JsonProperty("value")]
        public double Value { get; set; }

        [JsonProperty("isMuted")]
        public bool IsMuted { get; set; }
    }

    public class ActiveItem
    {
        [JsonProperty("playlistId")]
        public string PlaylistId { get; set; }

        [JsonProperty("playlistIndex")]
        public int PlaylistIndex { get; set; }

        [JsonProperty("index")]
        public int Index { get; set; }

        [JsonProperty("position")]
        public double? Position { get; set; }

        [JsonProperty("duration")]
        public double? Duration { get; set; }

        [JsonProperty("columns")]
        public List<string> Columns { get; set; }
    }

    public class PlaylistsResponse
    {
        [JsonProperty("playlists")]
        public List<PlaylistInfo> Playlists { get; set; }
    }

    public class PlaylistInfo
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("title")]
        public string Title { get; set; }

        [JsonProperty("isCurrent")]
        public bool IsCurrent { get; set; }

        [JsonProperty("itemCount")]
        public int ItemCount { get; set; }
    }

    public class PlaylistItemsResponse
    {
        [JsonProperty("offset")]
        public int Offset { get; set; }

        [JsonProperty("totalCount")]
        public int TotalCount { get; set; }

        [JsonProperty("items")]
        public List<PlaylistItemRow> Items { get; set; }
    }

    public class PlaylistItemRow
    {
        [JsonProperty("columns")]
        public List<string> Columns { get; set; }
    }

    /// <summary>
    /// Column layout requested from Beefweb for both the "now playing" item and
    /// playlist item listings. Index positions here must line up with how the
    /// results are read in BeefwebPlugin, since Beefweb returns the columns
    /// array positionally, not as a dictionary.
    /// </summary>
    public static class MediaColumns
    {
        public const int Artist = 0;
        public const int Title = 1;
        public const int Album = 2;
        public const int AlbumArtist = 3;
        public const int TrackNumber = 4;
        public const int Genre = 5;
        public const int Date = 6;
        public const int Codec = 7;
        public const int Bitrate = 8;
        public const int SampleRate = 9;
        public const int FilePath = 10;

        public static readonly string[] Expressions =
        {
            "%artist%",
            "%title%",
            "%album%",
            "%album artist%",
            "%tracknumber%",
            "%genre%",
            "%date%",
            "%codec%",
            "%bitrate%",
            "%samplerate%",
            "%path%"
        };

        public static string QueryValue => string.Join(",", Expressions);
    }
}
