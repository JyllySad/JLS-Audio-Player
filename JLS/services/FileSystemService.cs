using JLS.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TagLib;
using TagLib.Ape;

namespace JLS.Services
{
    public static class FileSystemService
    {
        private static readonly HashSet<string> AudioExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".mp3", ".flac", ".wav", ".m4a", ".ogg", ".wma", ".aac",
            ".alac", ".ape", ".aif", ".aiff"
        };

        public static List<FileSystemItem> GetDirectoryContents(string path)
        {
            var items = new List<FileSystemItem>();

            try
            {
                foreach (var dir in Directory.EnumerateDirectories(path))
                {
                    var info = new DirectoryInfo(dir);
                    if ((info.Attributes & FileAttributes.Hidden) == 0)
                    {
                        items.Add(new FileSystemItem
                        {
                            Name = info.Name,
                            FullPath = info.FullName,
                            IsDirectory = true,
                            Modified = info.LastWriteTime
                        });
                    }
                }
            }
            catch {         }

            try
            {
                foreach (var file in Directory.EnumerateFiles(path))
                {
                    var ext = Path.GetExtension(file);
                    if (AudioExtensions.Contains(ext))
                    {
                        var info = new FileInfo(file);
                        var item = new FileSystemItem
                        {
                            Name = info.Name,
                            FullPath = info.FullName,
                            IsDirectory = false,
                            Size = info.Length,
                            Modified = info.LastWriteTime
                        };

                        try
                        {
                            using (var tagFile = TagLib.File.Create(PathHelper.GetSafePath(file)))
                            {
                                item.Duration = tagFile.Properties.Duration.ToString(@"mm\:ss");
                                item.Artist = tagFile.Tag.FirstPerformer;
                                item.Album = tagFile.Tag.Album;
                                item.Genre = tagFile.Tag.FirstGenre;
                                item.TrackNumber = (int)tagFile.Tag.Track;
                                item.Title = tagFile.Tag.Title;
                                item.Year = tagFile.Tag.Year > 0 ? tagFile.Tag.Year.ToString() : "";
                                item.Bitrate = tagFile.Properties.AudioBitrate;
                                item.SampleRate = tagFile.Properties.AudioSampleRate;
                                item.BitsPerSample = tagFile.Properties.BitsPerSample;
                            }
                        }
                        catch
                        {
                        }

                        items.Add(item);
                    }
                }
            }
            catch {         }

            return items.OrderBy(i => i.IsDirectory ? 0 : 1).ThenBy(i => i.Name).ToList();
        }

        public static bool IsAudioFile(string path)
        {
            return AudioExtensions.Contains(Path.GetExtension(path));
        }
    }
}