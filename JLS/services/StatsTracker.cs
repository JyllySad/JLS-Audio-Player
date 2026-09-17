using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace JLS.Services
{
    public class StatsTracker
    {
        public static StatsTracker Instance { get; } = new StatsTracker();

        private long _currentSessionId = -1;

        private readonly SemaphoreSlim _dbLock = new SemaphoreSlim(1, 1);

        private string? _currentPath;
        private string? _currentTitle;
        private string? _currentArtist;
        private string? _currentAlbum;
        private string? _currentFormat;
        private double _currentTotalSeconds;
        private int _currentYear;

        private Stopwatch _playTimer = new Stopwatch();
        private double _accumulatedSeconds;

        private StatsTracker() { }

        public async Task StartSessionAsync()
        {
            await Task.Run(() =>
            {
                StatsDatabase.Initialize();

                using (var connection = new SqliteConnection(StatsDatabase.GetConnectionString()))
                {
                    connection.Open();
                    var cmd = connection.CreateCommand();
                    cmd.CommandText = "INSERT INTO Sessions (StartTime) VALUES (@start); SELECT last_insert_rowid();";
                    cmd.Parameters.AddWithValue("@start", DateTime.UtcNow);      

                    _currentSessionId = (long)(cmd.ExecuteScalar() ?? -1L);
                }
            });
        }

        public void SetTrack(string? path, string? title, string? artist, string? album, string? format, double totalSeconds, int year)
        {
            _ = CommitCurrentTrackAsync();

            _currentPath = path;
            _currentTitle = title;
            _currentArtist = artist;
            _currentAlbum = album;
            _currentFormat = format;
            _currentTotalSeconds = totalSeconds;
            _currentYear = year;

            _accumulatedSeconds = 0;
            _playTimer.Reset();
        }

        public void Play()
        {
            if (!string.IsNullOrEmpty(_currentPath))
            {
                _playTimer.Start();
            }
        }

        public void Pause()
        {
            _playTimer.Stop();
        }

        public Task CommitCurrentTrackAsync()
        {
            if (string.IsNullOrEmpty(_currentPath)) return Task.CompletedTask;

            Pause();
            _accumulatedSeconds = _playTimer.Elapsed.TotalSeconds;

            if (_accumulatedSeconds < 10.0 || (_currentTotalSeconds > 0 && _accumulatedSeconds < _currentTotalSeconds * 0.1))
            {
                _currentPath = null;
                return Task.CompletedTask;
            }

            bool isSkipped = _accumulatedSeconds < (_currentTotalSeconds * 0.5);

            string? path = _currentPath;
            string? title = string.IsNullOrWhiteSpace(_currentTitle) ? null : _currentTitle.Trim();
            string? artist = string.IsNullOrWhiteSpace(_currentArtist) ? null : _currentArtist.Trim();
            string? album = string.IsNullOrWhiteSpace(_currentAlbum) ? null : _currentAlbum.Trim();
            string? format = _currentFormat;
            double listened = _accumulatedSeconds;
            double total = _currentTotalSeconds;
            long sessionId = _currentSessionId;
            int year = _currentYear;

            _currentPath = null;   

            return Task.Run(async () =>
            {
                await _dbLock.WaitAsync();
                try
                {
                    using (var conn = new SqliteConnection(StatsDatabase.GetConnectionString()))
                    {
                        conn.Open();

                        long trackId = -1;
                        var checkCmd = conn.CreateCommand();
                        checkCmd.CommandText = "SELECT TrackId FROM Tracks WHERE Path = @path";
                        checkCmd.Parameters.AddWithValue("@path", path ?? (object)DBNull.Value);

                        var result = checkCmd.ExecuteScalar();
                        if (result != null)
                        {
                            trackId = (long)result;

                            var updateCmd = conn.CreateCommand();
                            updateCmd.CommandText = @"
        UPDATE Tracks 
        SET Title = @title, 
            Artist = @artist, 
            Album = @album,
            Year = CASE WHEN @year > 0 THEN @year ELSE Year END
        WHERE TrackId = @tid";

                            updateCmd.Parameters.AddWithValue("@title", title ?? (object)DBNull.Value);
                            updateCmd.Parameters.AddWithValue("@artist", artist ?? (object)DBNull.Value);
                            updateCmd.Parameters.AddWithValue("@album", album ?? (object)DBNull.Value);
                            updateCmd.Parameters.AddWithValue("@year", year);
                            updateCmd.Parameters.AddWithValue("@tid", trackId);
                            updateCmd.ExecuteNonQuery();
                        }
                        else
                        {
                            var insertTrackCmd = conn.CreateCommand();
                            insertTrackCmd.CommandText = @"
                        INSERT INTO Tracks (Path, Title, Artist, Album, Format, Year) 
                        VALUES (@path, @title, @artist, @album, @format, @year);
                        SELECT last_insert_rowid();";
                            insertTrackCmd.Parameters.AddWithValue("@path", path ?? (object)DBNull.Value);
                            insertTrackCmd.Parameters.AddWithValue("@title", title ?? (object)DBNull.Value);
                            insertTrackCmd.Parameters.AddWithValue("@artist", artist ?? (object)DBNull.Value);
                            insertTrackCmd.Parameters.AddWithValue("@album", album ?? (object)DBNull.Value);
                            insertTrackCmd.Parameters.AddWithValue("@format", format ?? (object)DBNull.Value);
                            insertTrackCmd.Parameters.AddWithValue("@year", year);

                            trackId = (long)(insertTrackCmd.ExecuteScalar() ?? -1L);
                        }

                        var insertListenCmd = conn.CreateCommand();
                        insertListenCmd.CommandText = @"
                    INSERT INTO Listens (TrackId, SessionId, SecondsListened, TotalSeconds, IsSkipped) 
                    VALUES (@tid, @sid, @listened, @total, @skipped);";
                        insertListenCmd.Parameters.AddWithValue("@tid", trackId);
                        insertListenCmd.Parameters.AddWithValue("@sid", sessionId);
                        insertListenCmd.Parameters.AddWithValue("@listened", listened);
                        insertListenCmd.Parameters.AddWithValue("@total", total);
                        insertListenCmd.Parameters.AddWithValue("@skipped", isSkipped ? 1 : 0);

                        insertListenCmd.ExecuteNonQuery();
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine("Ошибка записи статистики: " + ex.Message);
                }
                finally
                {
                    _dbLock.Release();
                }
            });
        }

        public void EndSession()
        {
            CommitCurrentTrackAsync().GetAwaiter().GetResult();

            if (_currentSessionId != -1)
            {
                _dbLock.Wait();
                try
                {
                    using (var conn = new SqliteConnection(StatsDatabase.GetConnectionString()))
                    {
                        conn.Open();
                        var cmd = conn.CreateCommand();
                        cmd.CommandText = "UPDATE Sessions SET EndTime = @end WHERE SessionId = @id";
                        cmd.Parameters.AddWithValue("@end", DateTime.UtcNow);
                        cmd.Parameters.AddWithValue("@id", _currentSessionId);
                        cmd.ExecuteNonQuery();
                    }
                }
                finally
                {
                    _dbLock.Release();
                }
            }
        }
    }
}