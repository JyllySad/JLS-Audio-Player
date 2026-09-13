using System;
using System.IO;

namespace JLS.Services
{
    public static class AppPaths
    {
        public static string BaseFolder
        {
            get
            {
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string path = Path.Combine(appData, "JLS_Player");
                
                if (!Directory.Exists(path))
                {
                    Directory.CreateDirectory(path);
                }
                
                return path;
            }
        }
        public static string SavedAlbumsJson => Path.Combine(BaseFolder, "saved_albums.json");

        public static string AppSettingsJson => Path.Combine(BaseFolder, "config.json");

        public static string CoversFolder
        {
            get
            {
                string path = Path.Combine(BaseFolder, "Playlists covers");
                if (!Directory.Exists(path))
                {
                    Directory.CreateDirectory(path);
                }
                return path;
            }
        }

    }
}