using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace JLS.Services
{
    public static class LyricsService
    {
        private static readonly string PlainFilePath = Path.Combine(AppPaths.BaseFolder, "lyrics_db.json");
        private static readonly string SyncedFilePath = Path.Combine(AppPaths.BaseFolder, "lyrics_synced_db.json");
        private static readonly string SettingsFilePath = Path.Combine(AppPaths.BaseFolder, "lyrics_settings.json");

        private static Dictionary<string, string> _plainLyricsDb = new();
        private static Dictionary<string, string> _syncedLyricsDb = new();
        private static Dictionary<string, bool> _modePreferenceDb = new();      

        static LyricsService() { LoadFromDisk(); }

        public static string GetPlainLyrics(string path) => _plainLyricsDb.GetValueOrDefault(path, "");
        public static void SavePlainLyrics(string path, string text) { _plainLyricsDb[path] = text; SaveToDisk(); }

        public static string GetSyncedLyrics(string path) => _syncedLyricsDb.GetValueOrDefault(path, "");
        public static void SaveSyncedLyrics(string path, string text) { _syncedLyricsDb[path] = text; SaveToDisk(); }

        public static bool GetSyncedModePreference(string path)
        {
            if (!_modePreferenceDb.ContainsKey(path))
                return _syncedLyricsDb.ContainsKey(path) && !string.IsNullOrEmpty(_syncedLyricsDb[path]);

            return _modePreferenceDb[path];
        }

        public static void SaveSyncedModePreference(string path, bool isSynced)
        {
            _modePreferenceDb[path] = isSynced;
            SaveToDisk();
        }

        private static void LoadFromDisk()
        {
            try
            {
                if (File.Exists(PlainFilePath)) _plainLyricsDb = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(PlainFilePath)) ?? new();
                if (File.Exists(SyncedFilePath)) _syncedLyricsDb = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(SyncedFilePath)) ?? new();
                if (File.Exists(SettingsFilePath)) _modePreferenceDb = JsonSerializer.Deserialize<Dictionary<string, bool>>(File.ReadAllText(SettingsFilePath)) ?? new();
            }
            catch { }
        }

        private static void SaveToDisk()
        {
            try
            {
                var opt = new JsonSerializerOptions { WriteIndented = true };
                File.WriteAllText(PlainFilePath, JsonSerializer.Serialize(_plainLyricsDb, opt));
                File.WriteAllText(SyncedFilePath, JsonSerializer.Serialize(_syncedLyricsDb, opt));
                File.WriteAllText(SettingsFilePath, JsonSerializer.Serialize(_modePreferenceDb, opt));
            }
            catch { }
        }
    }
}