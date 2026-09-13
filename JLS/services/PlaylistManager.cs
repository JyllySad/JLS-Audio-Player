using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using JLS.Models;

namespace JLS.Services
{
    public class PlaylistManager
    {
        private readonly string _filePath;

        public ObservableCollection<Playlist> Playlists { get; private set; } = new();

        public PlaylistManager()
        {
            _filePath = Path.Combine(AppPaths.BaseFolder, "playlists.json");
            LoadPlaylists();
        }

        public Playlist CreatePlaylist(string name)
        {
            var playlist = new Playlist { Name = name };
            Playlists.Add(playlist);
            SavePlaylists();
            return playlist;
        }

        public void DeletePlaylist(Playlist playlist)
        {
            if (Playlists.Contains(playlist))
            {
                Playlists.Remove(playlist);
                SavePlaylists();
            }
        }

        public void AddTrackToPlaylist(Playlist playlist, FileSystemItem track)
        {
            if (playlist.Items.Any(t => t.FullPath == track.FullPath)) return;

            playlist.Items.Add(track);
            playlist.TrackPaths.Add(track.FullPath);
            SavePlaylists();
        }

        public void RemoveTrackFromPlaylist(Playlist playlist, FileSystemItem track)
        {
            var itemToRemove = playlist.Items.FirstOrDefault(t => t.FullPath == track.FullPath);
            if (itemToRemove != null)
            {
                playlist.Items.Remove(itemToRemove);
                playlist.TrackPaths.Remove(itemToRemove.FullPath);
                SavePlaylists();
            }
        }

        public void SavePlaylists()
        {
            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };

                foreach (var p in Playlists)
                {
                    p.TrackPaths = p.Items.Select(i => i.FullPath).ToList();

                    p.SavedTracks = p.Items.Select(i => new PlaylistSavedTrack
                    {
                        FullPath = i.FullPath,
                        Title = i.Title,
                        Artist = i.Artist,
                        Album = i.Album,
                        Year = i.Year,
                        TrackNumber = i.TrackNumber
                    }).ToList();
                }

                var json = JsonSerializer.Serialize(Playlists, options);
                File.WriteAllText(_filePath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to save playlists: {ex.Message}");
            }
        }

        private void LoadPlaylists()
        {
            if (!File.Exists(_filePath)) return;

            try
            {
                var json = File.ReadAllText(_filePath);
                var loaded = JsonSerializer.Deserialize<ObservableCollection<Playlist>>(json);

                if (loaded != null)
                {
                    Playlists = loaded;

                    foreach (var playlist in Playlists)
                    {
                        playlist.Items = new ObservableCollection<FileSystemItem>();

                        if (playlist.SavedTracks != null && playlist.SavedTracks.Count > 0)
                        {
                            foreach (var savedTrack in playlist.SavedTracks)
                            {
                                if (File.Exists(savedTrack.FullPath))
                                {
                                    var fileInfo = new FileInfo(savedTrack.FullPath);
                                    var item = new FileSystemItem
                                    {
                                        FullPath = savedTrack.FullPath,
                                        Name = fileInfo.Name,
                                        IsDirectory = false,
                                        Size = fileInfo.Length,
                                        Modified = fileInfo.LastWriteTime
                                    };

                                    try
                                    {
                                        using (var tagFile = TagLib.File.Create(new JLS.Services.SafeFileAbstraction(savedTrack.FullPath)))
                                        {
                                            item.Duration = tagFile.Properties.Duration.ToString(@"mm\:ss");
                                            item.Bitrate = tagFile.Properties.AudioBitrate;
                                            item.SampleRate = tagFile.Properties.AudioSampleRate;
                                            item.BitsPerSample = tagFile.Properties.BitsPerSample;

                                            item.Title = tagFile.Tag.Title;
                                            item.Artist = tagFile.Tag.FirstPerformer;
                                            item.Album = tagFile.Tag.Album;
                                            item.Year = tagFile.Tag.Year > 0 ? tagFile.Tag.Year.ToString() : "";
                                            item.TrackNumber = (int)tagFile.Tag.Track;
                                        }
                                    }
                                    catch { }

                                    if (savedTrack.Title != null) item.Title = savedTrack.Title;
                                    if (savedTrack.Artist != null) item.Artist = savedTrack.Artist;
                                    if (savedTrack.Album != null) item.Album = savedTrack.Album;
                                    if (savedTrack.Year != null) item.Year = savedTrack.Year;
                                    if (savedTrack.TrackNumber.HasValue) item.TrackNumber = savedTrack.TrackNumber.Value;

                                    playlist.Items.Add(item);
                                }
                            }
                        }
                        else
                        {
                            foreach (var path in playlist.TrackPaths)
                            {
                                if (File.Exists(path))
                                {
                                    var fileInfo = new FileInfo(path);
                                    var item = new FileSystemItem
                                    {
                                        FullPath = path,
                                        Name = fileInfo.Name,
                                        IsDirectory = false,
                                        Size = fileInfo.Length,
                                        Modified = fileInfo.LastWriteTime
                                    };

                                    try
                                    {
                                        using (var tagFile = TagLib.File.Create(new JLS.Services.SafeFileAbstraction(path)))
                                        {
                                            item.Duration = tagFile.Properties.Duration.ToString(@"mm\:ss");
                                            item.Artist = tagFile.Tag.FirstPerformer;
                                            item.Album = tagFile.Tag.Album;
                                            item.TrackNumber = (int)tagFile.Tag.Track;
                                            item.Title = tagFile.Tag.Title;
                                            item.Genre = tagFile.Tag.FirstGenre;
                                            item.Year = tagFile.Tag.Year > 0 ? tagFile.Tag.Year.ToString() : "";
                                            item.Bitrate = tagFile.Properties.AudioBitrate;
                                            item.SampleRate = tagFile.Properties.AudioSampleRate;
                                            item.BitsPerSample = tagFile.Properties.BitsPerSample;
                                        }
                                    }
                                    catch { }

                                    playlist.Items.Add(item);
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load playlists: {ex.Message}");
            }
        }
    }
}