using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace JLS.Services
{
    public static class WaveformCacheManager
    {
        private const int MAX_CACHED_FILES = 1000;
        
        private static readonly string CacheDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "JLS_Player", 
            "WaveformCache");

        static WaveformCacheManager()
        {
            if (!Directory.Exists(CacheDirectory))
            {
                Directory.CreateDirectory(CacheDirectory);
            }
        }

        private static string GetCacheFilePath(string audioFilePath)
        {
            using (var md5 = MD5.Create())
            {
                byte[] hash = md5.ComputeHash(Encoding.UTF8.GetBytes(audioFilePath));
                string hex = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
                return Path.Combine(CacheDirectory, $"{hex}.bin");
            }
        }

        public static async Task SaveWaveformAsync(string audioFilePath, float[] left, float[] right)
        {
            await Task.Run(() =>
            {
                string cachePath = GetCacheFilePath(audioFilePath);

                using (var fs = new FileStream(cachePath, FileMode.Create, FileAccess.Write, FileShare.None))
                using (var writer = new BinaryWriter(fs))
                {
                    writer.Write(left.Length);
                    for (int i = 0; i < left.Length; i++) writer.Write(left[i]);
                    
                    writer.Write(right.Length);
                    for (int i = 0; i < right.Length; i++) writer.Write(right[i]);
                }

                CleanupOldCache();
            });
        }

        public static async Task<(float[] Left, float[] Right)?> LoadWaveformAsync(string audioFilePath)
        {
            return await Task.Run(() =>
            {
                string cachePath = GetCacheFilePath(audioFilePath);
                if (!File.Exists(cachePath)) return ((float[] Left, float[] Right)?)null;

                try
                {
                    using (var fs = new FileStream(cachePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                    using (var reader = new BinaryReader(fs))
                    {
                        int leftLen = reader.ReadInt32();
                        float[] left = new float[leftLen];
                        for (int i = 0; i < leftLen; i++) left[i] = reader.ReadSingle();

                        int rightLen = reader.ReadInt32();
                        float[] right = new float[rightLen];
                        for (int i = 0; i < rightLen; i++) right[i] = reader.ReadSingle();

                        File.SetLastAccessTime(cachePath, DateTime.Now);

                        return (left, right);
                    }
                }
                catch
                {
                    File.Delete(cachePath);
                    return null;
                }
            });
        }

        private static void CleanupOldCache()
        {
            var dirInfo = new DirectoryInfo(CacheDirectory);
            var files = dirInfo.GetFiles("*.bin");

            if (files.Length > MAX_CACHED_FILES)
            {
                var filesToDelete = files.OrderBy(f => f.LastAccessTime)
                                         .Take(files.Length - MAX_CACHED_FILES);

                foreach (var file in filesToDelete)
                {
                    try { file.Delete(); } catch {       }
                }
            }
        }
    }
}