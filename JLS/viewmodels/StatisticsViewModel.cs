using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace JLS.ViewModels
{
    public class TopItem
    {
        public int Rank { get; set; }
        public string Name { get; set; } = string.Empty;
        public string SecondaryText { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
    }

    public partial class StatisticsViewModel : ObservableObject
    {
        [ObservableProperty]
        private ObservableCollection<string> _timeFilters = new()
        {
            "Last 24 Hours", "Today", "Last 7 Days", "This Week",
            "Last 30 Days", "This Month", "This Year", "Last 365 Days", "All Time"
        };

        [ObservableProperty]
        private string _selectedFilter = "All Time";

        [ObservableProperty] private string _timeListening = "0s";
        [ObservableProperty] private string _appUptime = "0s";
        [ObservableProperty] private string _tracksPlayed = "0";
        [ObservableProperty] private string _tracksCompleted = "0";
        [ObservableProperty] private string _skipRate = "0%";
        [ObservableProperty] private string _uniqueTracks = "0";
        [ObservableProperty] private string _avgSessionLength = "0s";
        [ObservableProperty] private string _avgTracksPerSession = "0";
        [ObservableProperty] private string _formatProfile = "No data";
        [ObservableProperty] private string _favoriteDay = "---";
        [ObservableProperty] private string _favoriteMonth = "---";
        [ObservableProperty] private string _streaks = "Current: 0 | Best: 0";
        [ObservableProperty] private string _avgListenPercentage = "0%";
        [ObservableProperty] private string _avgTrackYear = "---";
        [ObservableProperty] private string _favoriteTimeOfDay = "---";

        [ObservableProperty] private ObservableCollection<TopItem> _topArtists = new();
        [ObservableProperty] private ObservableCollection<TopItem> _topAlbums = new();
        [ObservableProperty] private ObservableCollection<TopItem> _topTracks = new();

        [ObservableProperty]
        private ObservableCollection<string> _formatBadges = new();

        partial void OnFormatProfileChanged(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                FormatBadges = new ObservableCollection<string>();
                return;
            }

            var parts = value.Split(" | ", StringSplitOptions.RemoveEmptyEntries);
            var tempBadges = new List<string>();    

            foreach (var part in parts)
            {
                tempBadges.Add(part.Trim());
            }

            FormatBadges = new ObservableCollection<string>(tempBadges);
        }

        public StatisticsViewModel()
        {
            _ = LoadStatisticsAsync();
        }

        partial void OnSelectedFilterChanged(string value)
        {
            _ = LoadStatisticsAsync();
        }

        public async Task LoadStatisticsAsync()
        {
            await Task.Run(() =>
            {
                LoadStatistics();
            });
        }

        public void LoadStatistics()
        {
            try
            {
                DateTime? startDate = GetStartDateForFilter();

                string connectionString = Services.StatsDatabase.GetConnectionString();
                using (var conn = new SqliteConnection(connectionString))
                {
                    conn.Open();

                    LoadBasicMetrics(conn, startDate);

                    LoadSessionMetrics(conn, startDate);

                    LoadFormatProfile(conn, startDate);
                    LoadFavoriteDayAndMonth(conn, startDate);
                    LoadTops(conn, startDate);

                    LoadNewMetrics(conn, startDate);

                    CalculateStreaks(conn);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Ошибка загрузки статистики: " + ex.Message);
            }
        }

        [RelayCommand]
        private void ResetStatistics()
        {
            var dialog = new JLS.Views.ConfirmDialog(
                "Reset Statistics",
                $"Are you sure you want to reset statistics for:\n{SelectedFilter}?\n\nThis action cannot be undone.",
                "Reset");

            dialog.Owner = App.Current.MainWindow;        

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    DateTime? startDate = GetStartDateForFilter();

                    string whereSess = startDate.HasValue ? "WHERE StartTime >= @start AND EndTime IS NOT NULL" : "WHERE EndTime IS NOT NULL";

                    using (var conn = new SqliteConnection(Services.StatsDatabase.GetConnectionString()))
                    {
                        conn.Open();

                        using (var transaction = conn.BeginTransaction())
                        {
                            try
                            {
                                using var cmdListensSess = conn.CreateCommand();
                                cmdListensSess.Transaction = transaction;
                                cmdListensSess.CommandText = $"DELETE FROM Listens WHERE SessionId IN (SELECT SessionId FROM Sessions {whereSess})";
                                if (startDate.HasValue) cmdListensSess.Parameters.AddWithValue("@start", startDate.Value);
                                cmdListensSess.ExecuteNonQuery();

                                string whereListens = startDate.HasValue ? "WHERE Timestamp >= @start" : "";
                                using var cmdListens = conn.CreateCommand();
                                cmdListens.Transaction = transaction;
                                cmdListens.CommandText = $"DELETE FROM Listens {whereListens}";
                                if (startDate.HasValue) cmdListens.Parameters.AddWithValue("@start", startDate.Value);
                                cmdListens.ExecuteNonQuery();

                                using var cmdSess = conn.CreateCommand();
                                cmdSess.Transaction = transaction;
                                cmdSess.CommandText = $"DELETE FROM Sessions {whereSess}";
                                if (startDate.HasValue) cmdSess.Parameters.AddWithValue("@start", startDate.Value);
                                cmdSess.ExecuteNonQuery();

                                transaction.Commit();
                            }
                            catch
                            {
                                transaction.Rollback();
                                throw;
                            }
                        }
                    }

                    LoadStatistics();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine("Ошибка при очистке БД: " + ex.Message);
                }
            }
        }

        private DateTime? GetStartDateForFilter()
        {
            DateTime now = DateTime.Now;        
            DateTime? localStart = SelectedFilter switch
            {
                "Last 24 Hours" => now.AddHours(-24),
                "Today" => now.Date,      
                "Last 7 Days" => now.AddDays(-7),
                "This Week" => now.Date.AddDays(-(int)now.DayOfWeek + (now.DayOfWeek == DayOfWeek.Sunday ? -6 : 1)),
                "Last 30 Days" => now.AddDays(-30),
                "This Month" => new DateTime(now.Year, now.Month, 1),
                "This Year" => new DateTime(now.Year, 1, 1),
                "Last 365 Days" => now.AddDays(-365),
                _ => null   
            };

            return localStart?.ToUniversalTime();
        }

        private void LoadBasicMetrics(SqliteConnection conn, DateTime? start)
        {
            string where = start.HasValue ? "WHERE Timestamp >= @start" : "";

            using var cmd = conn.CreateCommand();
            cmd.CommandText = $@"
    SELECT 
        COUNT(ListenId), 
        SUM(CASE WHEN IsSkipped = 0 THEN 1 ELSE 0 END), 
        COUNT(DISTINCT TrackId), 
        SUM(SecondsListened),
        AVG(CASE WHEN TotalSeconds > 0 THEN MIN(CAST(SecondsListened AS REAL) / TotalSeconds, 1.0) ELSE 0 END)
    FROM Listens {where}";

            if (start.HasValue) cmd.Parameters.AddWithValue("@start", start.Value);

            using (var reader = cmd.ExecuteReader())
            {
                if (reader.Read())
                {
                    double totalPlays = reader.IsDBNull(0) ? 0 : reader.GetInt64(0);
                    double completed = reader.IsDBNull(1) ? 0 : reader.GetInt64(1);
                    double unique = reader.IsDBNull(2) ? 0 : reader.GetInt64(2);
                    double totalSecs = reader.IsDBNull(3) ? 0 : reader.GetDouble(3);
                    double avgPercent = reader.IsDBNull(4) ? 0 : reader.GetDouble(4);
                    AvgListenPercentage = Math.Round(avgPercent * 100) + "%";

                    TracksPlayed = totalPlays.ToString();
                    TracksCompleted = completed.ToString();
                    UniqueTracks = unique.ToString();
                    TimeListening = FormatTime(totalSecs);

                    if (totalPlays > 0)
                    {
                        double skips = totalPlays - completed;
                        SkipRate = Math.Round((skips / totalPlays) * 100, 1) + "%";
                    }
                    else SkipRate = "0%";
                }
            }
        }

        private void LoadSessionMetrics(SqliteConnection conn, DateTime? start)
        {
            string where = start.HasValue ? "WHERE StartTime >= @start AND EndTime IS NOT NULL" : "WHERE EndTime IS NOT NULL";
            string whereListens = start.HasValue ? "WHERE Timestamp >= @start AND SessionId IS NOT NULL" : "WHERE SessionId IS NOT NULL";

            using var cmdUptime = conn.CreateCommand();
            cmdUptime.CommandText = $"SELECT SUM(CAST(strftime('%s', EndTime) - strftime('%s', StartTime) AS REAL)) FROM Sessions {where}";
            if (start.HasValue) cmdUptime.Parameters.AddWithValue("@start", start.Value);

            var uptimeObj = cmdUptime.ExecuteScalar();
            double totalUptime = (uptimeObj != DBNull.Value && uptimeObj != null) ? Convert.ToDouble(uptimeObj) : 0;
            AppUptime = FormatTime(totalUptime);

            using var cmdAvgLength = conn.CreateCommand();
            cmdAvgLength.CommandText = $@"
        SELECT AVG(CAST(strftime('%s', EndTime) - strftime('%s', StartTime) AS REAL))
        FROM Sessions
        {where} AND SessionId IN (
            SELECT SessionId 
            FROM Listens 
            {whereListens}
            GROUP BY SessionId 
            HAVING COUNT(ListenId) >= 2
        )";
            if (start.HasValue) cmdAvgLength.Parameters.AddWithValue("@start", start.Value);

            var avgLenObj = cmdAvgLength.ExecuteScalar();
            if (avgLenObj != DBNull.Value && avgLenObj != null)
            {
                AvgSessionLength = FormatTime(Convert.ToDouble(avgLenObj));
            }
            else
            {
                AvgSessionLength = "0s";
            }

            using var cmdAvgTracks = conn.CreateCommand();
            cmdAvgTracks.CommandText = $@"
        SELECT AVG(TrackCount) 
        FROM (
            SELECT COUNT(ListenId) as TrackCount 
            FROM Listens 
            {whereListens}
            GROUP BY SessionId 
            HAVING COUNT(ListenId) >= 2
        )";
            if (start.HasValue) cmdAvgTracks.Parameters.AddWithValue("@start", start.Value);

            var avgResult = cmdAvgTracks.ExecuteScalar();
            if (avgResult != DBNull.Value && avgResult != null)
            {
                AvgTracksPerSession = Math.Round(Convert.ToDouble(avgResult), 1).ToString();
            }
            else
            {
                AvgTracksPerSession = "0";
            }
        }

        private void LoadFormatProfile(SqliteConnection conn, DateTime? start)
        {
            string where = start.HasValue ? "WHERE l.Timestamp >= @start" : "";

            using var cmd = conn.CreateCommand();
            cmd.CommandText = $@"
                SELECT t.Format, SUM(l.SecondsListened) as Time
                FROM Listens l
                JOIN Tracks t ON l.TrackId = t.TrackId
                {where}
                GROUP BY t.Format
                ORDER BY Time DESC";

            if (start.HasValue) cmd.Parameters.AddWithValue("@start", start.Value);

            double totalFormatTime = 0;
            var formats = new Dictionary<string, double>();

            using (var reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    string fmt = reader.IsDBNull(0) ? "UNKNOWN" : reader.GetString(0);
                    double time = reader.IsDBNull(1) ? 0 : reader.GetDouble(1);
                    formats[fmt] = time;
                    totalFormatTime += time;
                }
            }

            if (totalFormatTime > 0)
            {
                var profile = formats.Select(f => $"{f.Key}: {Math.Round((f.Value / totalFormatTime) * 100)}%");
                FormatProfile = string.Join(" | ", profile);
            }
            else FormatProfile = "No data";
        }

        private void LoadNewMetrics(SqliteConnection conn, DateTime? start)
        {
            string whereYear = start.HasValue ? "WHERE l.Timestamp >= @start AND t.Year > 0" : "WHERE t.Year > 0";
            string whereTime = start.HasValue ? "WHERE Timestamp >= @start" : "";

            try
            {
                using var cmdYear = conn.CreateCommand();
                cmdYear.CommandText = $"SELECT AVG(t.Year) FROM Listens l JOIN Tracks t ON l.TrackId = t.TrackId {whereYear}";
                if (start.HasValue) cmdYear.Parameters.AddWithValue("@start", start.Value);

                var resYear = cmdYear.ExecuteScalar();
                if (resYear != DBNull.Value && resYear != null)
                    AvgTrackYear = Math.Round(Convert.ToDouble(resYear)).ToString();
                else
                    AvgTrackYear = "---";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Ошибка расчета среднего года: " + ex.Message);
                AvgTrackYear = "---";
            }

            try
            {
                using var cmdTime = conn.CreateCommand();
                cmdTime.CommandText = $"SELECT Timestamp, SecondsListened FROM Listens {whereTime}";
                if (start.HasValue) cmdTime.Parameters.AddWithValue("@start", start.Value);

                double morning = 0, day = 0, evening = 0, night = 0;

                using (var reader = cmdTime.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        if (reader.IsDBNull(0) || reader.IsDBNull(1)) continue;

                        DateTime dt;
                        try { dt = reader.GetDateTime(0); }
                        catch
                        {
                            if (DateTime.TryParse(reader.GetString(0), out var parsedDt)) dt = parsedDt;
                            else continue;
                        }

                        if (dt.Kind == DateTimeKind.Utc || dt.Kind == DateTimeKind.Unspecified)
                        {
                            dt = dt.ToLocalTime();
                        }

                        double s = Convert.ToDouble(reader.GetValue(1));
                        int h = dt.Hour;

                        if (h >= 6 && h < 12) morning += s;
                        else if (h >= 12 && h < 18) day += s;
                        else if (h >= 18 && h <= 23) evening += s;
                        else night += s;
                    }
                }

                double maxTime = Math.Max(Math.Max(morning, day), Math.Max(evening, night));

                if (maxTime == 0) FavoriteTimeOfDay = "---";
                else if (maxTime == morning) FavoriteTimeOfDay = "Morning";
                else if (maxTime == day) FavoriteTimeOfDay = "Day";
                else if (maxTime == evening) FavoriteTimeOfDay = "Evening";
                else FavoriteTimeOfDay = "Night";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Ошибка расчета времени суток: " + ex.Message);
                FavoriteTimeOfDay = "---";
            }
        }

        private void LoadFavoriteDayAndMonth(SqliteConnection conn, DateTime? start)
        {
            string[] days = { "Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday" };
            string[] months = { "", "January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December" };

            string where = start.HasValue ? "WHERE Timestamp >= @start" : "";

            using var cmdDay = conn.CreateCommand();
            cmdDay.CommandText = $"SELECT strftime('%w', Timestamp, 'localtime') as D, SUM(SecondsListened) as T FROM Listens {where} GROUP BY D ORDER BY T DESC LIMIT 1";
            if (start.HasValue) cmdDay.Parameters.AddWithValue("@start", start.Value);

            var resDay = cmdDay.ExecuteScalar();
            if (resDay != null && int.TryParse(resDay.ToString(), out int dayIdx))
                FavoriteDay = days[dayIdx];
            else FavoriteDay = "---";

            using var cmdMonth = conn.CreateCommand();
            cmdMonth.CommandText = $"SELECT strftime('%m', Timestamp, 'localtime') as M, SUM(SecondsListened) as T FROM Listens {where} GROUP BY M ORDER BY T DESC LIMIT 1";
            if (start.HasValue) cmdMonth.Parameters.AddWithValue("@start", start.Value);

            var resMonth = cmdMonth.ExecuteScalar();
            if (resMonth != null && int.TryParse(resMonth.ToString(), out int monthIdx))
                FavoriteMonth = months[monthIdx];
            else FavoriteMonth = "---";
        }

        private void LoadTops(SqliteConnection conn, DateTime? start)
        {
            string where = start.HasValue ? "WHERE l.Timestamp >= @start AND " : "WHERE ";

            var tempArtists = new List<TopItem>();
            using (var cmdArt = conn.CreateCommand())
            {
                cmdArt.CommandText = $"SELECT t.Artist, SUM(l.SecondsListened) as T FROM Listens l JOIN Tracks t ON l.TrackId = t.TrackId {where} t.Artist IS NOT NULL AND TRIM(t.Artist) != '' AND t.Artist != 'Unknown Artist' GROUP BY t.Artist COLLATE NOCASE ORDER BY T DESC LIMIT 10";
                if (start.HasValue) cmdArt.Parameters.AddWithValue("@start", start.Value);
                using (var reader = cmdArt.ExecuteReader())
                {
                    int rank = 1;
                    while (reader.Read())
                    {
                        string artistName = reader.GetString(0);
                        tempArtists.Add(new TopItem { Rank = rank, Name = artistName, Value = FormatTime(reader.GetDouble(1)) });
                        rank++;
                    }
                }
            }
            TopArtists = new ObservableCollection<TopItem>(tempArtists);

            var tempAlbums = new List<TopItem>();
            using (var cmdAlb = conn.CreateCommand())
            {
                cmdAlb.CommandText = $"SELECT t.Album, t.Artist, SUM(l.SecondsListened) as T FROM Listens l JOIN Tracks t ON l.TrackId = t.TrackId {where} t.Album IS NOT NULL AND TRIM(t.Album) != '' AND t.Album != 'Unknown Album' GROUP BY t.Album COLLATE NOCASE, t.Artist COLLATE NOCASE ORDER BY T DESC LIMIT 10";
                if (start.HasValue) cmdAlb.Parameters.AddWithValue("@start", start.Value);

                using (var reader = cmdAlb.ExecuteReader())
                {
                    int rank = 1;
                    while (reader.Read())
                    {
                        string dbAlbum = reader.IsDBNull(0) ? "" : reader.GetString(0);
                        string dbArtist = reader.IsDBNull(1) ? "" : reader.GetString(1);

                        string albumName = string.IsNullOrWhiteSpace(dbAlbum) ? "Unknown Album" : dbAlbum;
                        string artistName = string.IsNullOrWhiteSpace(dbArtist) ? "Unknown Artist" : dbArtist;

                        tempAlbums.Add(new TopItem
                        {
                            Rank = rank,
                            Name = albumName,
                            SecondaryText = $" by {artistName}",
                            Value = FormatTime(reader.GetDouble(2))
                        });
                        rank++;
                    }
                }
            }
            TopAlbums = new ObservableCollection<TopItem>(tempAlbums);

            var tempTracks = new List<TopItem>();
            using (var cmdTrk = conn.CreateCommand())
            {
                cmdTrk.CommandText = $"SELECT t.Title, t.Artist, SUM(l.SecondsListened) as T FROM Listens l JOIN Tracks t ON l.TrackId = t.TrackId {where} t.Title IS NOT NULL AND TRIM(t.Title) != '' GROUP BY t.TrackId ORDER BY T DESC LIMIT 10";
                if (start.HasValue) cmdTrk.Parameters.AddWithValue("@start", start.Value);
                using (var reader = cmdTrk.ExecuteReader())
                {
                    int rank = 1;
                    while (reader.Read())
                    {
                        string dbTitle = reader.IsDBNull(0) ? "" : reader.GetString(0);
                        string dbArtist = reader.IsDBNull(1) ? "" : reader.GetString(1);

                        string trackTitle = string.IsNullOrWhiteSpace(dbTitle) ? "Unknown Title" : dbTitle;
                        string artistName = string.IsNullOrWhiteSpace(dbArtist) ? "Unknown Artist" : dbArtist;

                        tempTracks.Add(new TopItem
                        {
                            Rank = rank,
                            Name = trackTitle,
                            SecondaryText = $" by {artistName}",
                            Value = FormatTime(reader.GetDouble(2))
                        });
                        rank++;
                    }
                }
            }
            TopTracks = new ObservableCollection<TopItem>(tempTracks);
        }

        private void CalculateStreaks(SqliteConnection conn)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT DISTINCT date(StartTime, 'localtime') FROM Sessions ORDER BY date(StartTime, 'localtime')";

            var dates = new List<DateTime>();
            using (var reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    if (DateTime.TryParse(reader.GetString(0), out DateTime dt))
                        dates.Add(dt.Date);
                }
            }

            var datesSet = new HashSet<DateTime>(dates);

            int best = 0;
            int temp = 0;
            DateTime lastDate = DateTime.MinValue;

            foreach (var d in dates)
            {
                if (temp == 0) { temp = 1; lastDate = d; }
                else if (lastDate.AddDays(1) == d) { temp++; lastDate = d; }
                else if (lastDate == d) {       }
                else { temp = 1; lastDate = d; }

                if (temp > best) best = temp;
            }

            int current = 0;
            DateTime today = DateTime.Now.Date;

            if (datesSet.Contains(today))
            {
                current = 1;
                DateTime check = today.AddDays(-1);
                while (datesSet.Contains(check)) { current++; check = check.AddDays(-1); }
            }
            else if (datesSet.Contains(today.AddDays(-1)))
            {
                current = 1;
                DateTime check = today.AddDays(-2);
                while (datesSet.Contains(check)) { current++; check = check.AddDays(-1); }
            }

            Streaks = $"Current: {current} | Best: {best}";
        }

        private string FormatTime(double totalSeconds)
        {
            if (totalSeconds <= 0) return "0s";
            TimeSpan ts = TimeSpan.FromSeconds(totalSeconds);

            if (ts.TotalHours >= 1)
                return $"{(int)ts.TotalHours}h {ts.Minutes}m";

            if (ts.TotalMinutes >= 1)
                return $"{ts.Minutes}m {ts.Seconds}s";

            return $"{ts.Seconds}s";
        }
    }
}