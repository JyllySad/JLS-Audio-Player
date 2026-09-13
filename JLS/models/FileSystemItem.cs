using CommunityToolkit.Mvvm.ComponentModel;
using JLS.Services;
using System;
using System.IO;
using System.Xml.Linq;

namespace JLS.Models
{
    public partial class FileSystemItem : ObservableObject
    {
        [ObservableProperty] private bool _isPlaying;
        [ObservableProperty] private string _name = string.Empty;
        [ObservableProperty] private string _fullPath = string.Empty;
        [ObservableProperty] private bool _isDirectory;
        [ObservableProperty] private string? _duration;
        [ObservableProperty] private string? _artist;
        [ObservableProperty] private string? _year;
        [ObservableProperty] private string? _album;
        [ObservableProperty] private string? _genre;
        [ObservableProperty] private string? _title;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(DisplayBitsPerSample))]
        private int? _bitsPerSample;

        [ObservableProperty]
        private bool _isCustomNamed;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(DisplaySize))]
        private long _size;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(DisplayModified))]
        private DateTime _modified;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(DisplayTrackNumber))]
        private int? _trackNumber;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(DisplayBitrate))]
        private int? _bitrate;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(DisplaySampleRate))]
        private int? _sampleRate;

        public string Extension => IsDirectory ? "" : Path.GetExtension(Name).ToLower();
        public string DisplayTrackNumber => TrackNumber?.ToString() ?? "";
        public string DisplaySize => IsDirectory ? "" : FormatSize(Size);
        public string DisplayModified => Modified.ToString("dd.MM.yyyy HH:mm");
        public string DisplayBitrate => Bitrate.HasValue ? $"{Bitrate} kbps" : "";
        public string DisplaySampleRate => SampleRate.HasValue ? $"{SampleRate / 1000.0:F1} kHz" : "";
        public string DisplayBitsPerSample => (BitsPerSample.HasValue && BitsPerSample.Value > 0) ? $"{BitsPerSample} Bit" : "";

        private static string FormatSize(long bytes)
        {
            string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
            int idx = 0;
            double size = bytes;
            while (size >= 1024 && idx < suffixes.Length - 1)
            {
                size /= 1024;
                idx++;
            }
            return $"{size:0.#} {suffixes[idx]}";
        }

        public void ReloadMetadata()
        {
            if (IsDirectory || !System.IO.File.Exists(FullPath)) return;

            try
            {
                using (var tagFile = TagLib.File.Create(PathHelper.GetSafePath(FullPath)))
                {
                    if (!IsCustomNamed)
                    {
                        Title = tagFile.Tag.Title;
                        Artist = tagFile.Tag.FirstPerformer;
                        Album = tagFile.Tag.Album;
                        Year = tagFile.Tag.Year > 0 ? tagFile.Tag.Year.ToString() : "";
                        Genre = tagFile.Tag.FirstGenre;
                        TrackNumber = (int)tagFile.Tag.Track;
                    }

                    Bitrate = tagFile.Properties.AudioBitrate;
                    SampleRate = tagFile.Properties.AudioSampleRate;
                    BitsPerSample = tagFile.Properties.BitsPerSample;
                    Duration = tagFile.Properties.Duration.ToString(@"mm\:ss");

                    var fileInfo = new System.IO.FileInfo(FullPath);
                    Size = fileInfo.Length;
                    Modified = fileInfo.LastWriteTime;
                }
            }
            catch {  }
        }
    }
}