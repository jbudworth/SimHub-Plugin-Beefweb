using System;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;

namespace SimHub.Plugin.Beefweb
{
    /// <summary>
    /// Plugin settings persisted by SimHub through PluginManager.ReadCommonSettings /
    /// SaveCommonSettings. Kept as a plain data class so it serializes cleanly.
    /// </summary>
    public class BeefwebPluginSettings
    {
        public string Host { get; set; } = "127.0.0.1";

        public int Port { get; set; } = 8880;

        public bool UseAuthentication { get; set; } = false;

        public string Username { get; set; } = "";

        /// <summary>DPAPI-protected, base64-encoded form of the password; this is what actually gets persisted.</summary>
        public string EncryptedPassword { get; set; } = "";

        /// <summary>
        /// Plaintext password. Never persisted directly (see JsonIgnore) - reading/writing this
        /// transparently unprotects/protects EncryptedPassword via Windows DPAPI, scoped to the
        /// current Windows user, so the settings file on disk never contains it in the clear.
        /// </summary>
        [JsonIgnore]
        public string Password
        {
            get => Unprotect(EncryptedPassword);
            set => EncryptedPassword = Protect(value);
        }

        /// <summary>How often, in milliseconds, to poll Beefweb for player state.</summary>
        public int PollingIntervalMs { get; set; } = 500;

        /// <summary>Seconds to jump for the SeekForward / SeekBackward controls.</summary>
        public int SeekStepSeconds { get; set; } = 10;

        /// <summary>Percent to change volume by for the VolumeUp / VolumeDown controls.</summary>
        public double VolumeStepPercent { get; set; } = 5;

        /// <summary>HTTP request timeout, in milliseconds.</summary>
        public int RequestTimeoutMs { get; set; } = 3000;

        // DPAPI has no separate "key"; this entropy just scopes the protected blob to this
        // plugin so it can't be casually unprotected by unrelated code using the same API.
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("SimHub.Plugin.Beefweb");

        private static string Protect(string plainText)
        {
            if (string.IsNullOrEmpty(plainText))
            {
                return "";
            }

            var bytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(plainText), Entropy, DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(bytes);
        }

        private static string Unprotect(string encrypted)
        {
            if (string.IsNullOrEmpty(encrypted))
            {
                return "";
            }

            try
            {
                var bytes = ProtectedData.Unprotect(Convert.FromBase64String(encrypted), Entropy, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(bytes);
            }
            catch (Exception)
            {
                // Not decryptable (e.g. settings copied to a different user profile/machine); treat as unset.
                return "";
            }
        }
    }
}
