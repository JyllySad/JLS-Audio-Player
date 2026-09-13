using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace JLS.ViewModels
{
    public partial class PlayHistoryViewModel : ObservableObject
    {
        [ObservableProperty]
        private bool _isOpen = false;

        [ObservableProperty]
        private ObservableCollection<PlayHistoryItem> _tracks = new();

        [ObservableProperty]
        private bool _isLoading = false;

        public void Open(List<HistoryRecord> historyRecords)
        {
            IsOpen = true;
            LoadHistoryAsync(historyRecords);
        }

        [RelayCommand]
        private void CloseHistory()
        {
            IsOpen = false;
        }

        private async void LoadHistoryAsync(List<HistoryRecord> records)
        {
            IsLoading = true;
            Tracks.Clear();

            if (records == null || records.Count == 0)
            {
                IsLoading = false;
                return;
            }

            var rawParsedList = await Task.Run(() =>
            {
                var list = new List<TempHistoryTrack>();
                foreach (var record in records)
                {
                    if (!File.Exists(record.Path)) continue;

                    try
                    {
                        using (var file = TagLib.File.Create(JLS.Services.PathHelper.GetSafePath(record.Path)))
                        {
                            var item = new TempHistoryTrack
                            {
                                FullPath = record.Path,
                                Title = !string.IsNullOrEmpty(file.Tag.Title) ? file.Tag.Title : Path.GetFileNameWithoutExtension(record.Path),
                                Artist = !string.IsNullOrEmpty(file.Tag.FirstPerformer) ? file.Tag.FirstPerformer : "Unknown Artist",
                                Album = !string.IsNullOrEmpty(file.Tag.Album) ? file.Tag.Album : "Unknown Album",
                                Year = file.Tag.Year > 0 ? file.Tag.Year.ToString() : "---",
                                TrackNumber = file.Tag.Track > 0 ? file.Tag.Track.ToString() : "",
                                ListenDate = record.PlayedAt.ToString("dd.MM.yyyy HH:mm")
                            };

                            if (file.Tag.Pictures.Length > 0)
                            {
                                item.RawImageBytes = file.Tag.Pictures[0].Data.Data;
                            }
                            else
                            {
                                string? directory = Path.GetDirectoryName(record.Path);
                                if (!string.IsNullOrEmpty(directory))
                                {
                                    string[] possibleNames = { "cover.jpg", "cover.png", "folder.jpg", "folder.png", "front.jpg", "front.png" };
                                    foreach (var name in possibleNames)
                                    {
                                        string coverPath = Path.Combine(directory, name);
                                        if (File.Exists(coverPath))
                                        {
                                            item.RawImageBytes = File.ReadAllBytes(coverPath);
                                            break;
                                        }
                                    }
                                }
                            }
                            list.Add(item);
                        }
                    }
                    catch {      }
                }
                return list;
            });

            var finalItems = await Task.Run(() =>
            {
                var clusters = new List<TempCluster>();
                TempCluster? currentCluster = null;

                foreach (var track in rawParsedList)
                {
                    bool isSameAlbum = currentCluster != null &&
                                       !string.Equals(track.Album, "Unknown Album", StringComparison.OrdinalIgnoreCase) &&
                                       string.Equals(currentCluster.AlbumName, track.Album, StringComparison.OrdinalIgnoreCase) &&
                                       string.Equals(currentCluster.ArtistName, track.Artist, StringComparison.OrdinalIgnoreCase);

                    if (isSameAlbum && currentCluster != null)
                    {
                        currentCluster.Tracks.Add(track);
                    }
                    else
                    {
                        currentCluster = new TempCluster
                        {
                            AlbumName = track.Album,
                            ArtistName = track.Artist,
                            Tracks = new List<TempHistoryTrack> { track }
                        };
                        clusters.Add(currentCluster);
                    }
                }

                var seenInAlbumGroup = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var seenAsSingle = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var processedList = new List<PlayHistoryItem>();

                for (int i = 0; i < clusters.Count; i++)
                {
                    var cluster = clusters[i];

                    if (cluster.Tracks.Count >= 2 && !string.Equals(cluster.AlbumName, "Unknown Album", StringComparison.OrdinalIgnoreCase))
                    {
                        var validTracks = cluster.Tracks.Where(t => !seenInAlbumGroup.Contains(t.FullPath)).ToList();

                        if (validTracks.Count >= 2)
                        {
                            var groupItem = new PlayHistoryItem
                            {
                                IsAlbumGroup = true,
                                AlbumTitle = cluster.AlbumName,
                                AlbumArtist = cluster.ArtistName,
                                RawImageBytes = validTracks.FirstOrDefault(t => t.RawImageBytes != null)?.RawImageBytes
                            };

                            foreach (var t in validTracks)
                            {
                                seenInAlbumGroup.Add(t.FullPath);
                                groupItem.AlbumTracks.Add(new PlayHistorySubTrackItem
                                {
                                    FullPath = t.FullPath,
                                    Title = t.Title,
                                    Artist = t.Artist,
                                    TrackNumber = t.TrackNumber,
                                    ListenDate = t.ListenDate
                                });
                            }
                            processedList.Add(groupItem);
                        }
                        else if (validTracks.Count == 1)
                        {
                            var singleTrack = validTracks[0];
                            if (!seenAsSingle.Contains(singleTrack.FullPath))
                            {
                                seenAsSingle.Add(singleTrack.FullPath);
                                processedList.Add(CreateSingleItem(singleTrack));
                            }
                        }
                    }
                    else
                    {
                        foreach (var track in cluster.Tracks)
                        {
                            if (seenAsSingle.Contains(track.FullPath)) continue;

                            bool adjacentToSameAlbumGroup = false;

                            if (processedList.Count > 0)
                            {
                                var lastProcessed = processedList[^1];
                                if (lastProcessed.IsAlbumGroup && lastProcessed.AlbumTracks.Any(st => st.FullPath.Equals(track.FullPath, StringComparison.OrdinalIgnoreCase)))
                                {
                                    adjacentToSameAlbumGroup = true;
                                }
                            }

                            if (!adjacentToSameAlbumGroup && i + 1 < clusters.Count)
                            {
                                var nextCluster = clusters[i + 1];
                                if (nextCluster.Tracks.Count >= 2 &&
                                    nextCluster.Tracks.Any(t => t.FullPath.Equals(track.FullPath, StringComparison.OrdinalIgnoreCase)))
                                {
                                    adjacentToSameAlbumGroup = true;
                                }
                            }

                            if (!adjacentToSameAlbumGroup)
                            {
                                seenAsSingle.Add(track.FullPath);
                                processedList.Add(CreateSingleItem(track));
                            }
                        }
                    }
                }

                return processedList;
            });

            void DecodeCover(PlayHistoryItem item)
            {
                if (item.RawImageBytes != null && item.RawImageBytes.Length > 0)
                {
                    try
                    {
                        var bmp = new BitmapImage();
                        using (var ms = new MemoryStream(item.RawImageBytes))
                        {
                            bmp.BeginInit();
                            bmp.CacheOption = BitmapCacheOption.OnLoad;
                            bmp.DecodePixelWidth = 100;
                            bmp.StreamSource = ms;
                            bmp.EndInit();
                        }
                        bmp.Freeze();
                        item.Cover = bmp;
                    }
                    catch { }
                    finally
                    {
                        item.RawImageBytes = null;
                    }
                }
            }

            Tracks.Clear();
            int initialLoad = Math.Min(12, finalItems.Count);

            await Task.Run(() =>
            {
                for (int i = 0; i < initialLoad; i++)
                {
                    DecodeCover(finalItems[i]);
                }
            });

            for (int i = 0; i < initialLoad; i++)
            {
                Tracks.Add(finalItems[i]);
            }

            IsLoading = false;

            await Task.Delay(250);

            for (int i = initialLoad; i < finalItems.Count; i += 5)
            {
                int chunkEnd = Math.Min(i + 5, finalItems.Count);

                await Task.Run(() =>
                {
                    for (int j = i; j < chunkEnd; j++)
                    {
                        DecodeCover(finalItems[j]);
                    }
                });

                for (int j = i; j < chunkEnd; j++)
                {
                    Tracks.Add(finalItems[j]);
                }

                await Task.Delay(10);
            }
        }

        private static PlayHistoryItem CreateSingleItem(TempHistoryTrack t)
        {
            return new PlayHistoryItem
            {
                IsAlbumGroup = false,
                FullPath = t.FullPath,
                Title = t.Title,
                Artist = t.Artist,
                Album = t.Album,
                Year = t.Year,
                TrackNumber = t.TrackNumber,
                ListenDate = t.ListenDate,
                RawImageBytes = t.RawImageBytes
            };
        }

        private class TempHistoryTrack
        {
            public string FullPath { get; set; } = string.Empty;
            public string Title { get; set; } = string.Empty;
            public string Artist { get; set; } = string.Empty;
            public string Album { get; set; } = string.Empty;
            public string Year { get; set; } = string.Empty;
            public string TrackNumber { get; set; } = string.Empty;
            public string ListenDate { get; set; } = string.Empty;
            public byte[]? RawImageBytes { get; set; }
        }

        private class TempCluster
        {
            public string AlbumName { get; set; } = string.Empty;
            public string ArtistName { get; set; } = string.Empty;
            public List<TempHistoryTrack> Tracks { get; set; } = new();
        }
    }

    public partial class PlayHistoryItem : ObservableObject
    {
        public bool IsAlbumGroup { get; set; }

        public string FullPath { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Artist { get; set; } = string.Empty;
        public string Album { get; set; } = string.Empty;
        public string Year { get; set; } = string.Empty;
        public string TrackNumber { get; set; } = string.Empty;
        public string ListenDate { get; set; } = string.Empty;

        public byte[]? RawImageBytes { get; set; }

        [ObservableProperty]
        private BitmapImage? _cover;

        public string AlbumTitle { get; set; } = string.Empty;
        public string AlbumArtist { get; set; } = string.Empty;
        public ObservableCollection<PlayHistorySubTrackItem> AlbumTracks { get; set; } = new();
    }

    public partial class PlayHistorySubTrackItem : ObservableObject
    {
        public string FullPath { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Artist { get; set; } = string.Empty;
        public string TrackNumber { get; set; } = string.Empty;
        public string ListenDate { get; set; } = string.Empty;
    }

    public class HistoryRecord
    {
        public string Path { get; set; } = string.Empty;
        public DateTime PlayedAt { get; set; }
    }

}