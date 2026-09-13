using System;
using System.IO;
using Microsoft.Data.Sqlite;

namespace JLS.Services
{
    public static class StatsDatabase
    {
        private static string GetDbPath()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string folderPath = Path.Combine(appData, "JLS_Player");

            if (!Directory.Exists(folderPath))
            {
                Directory.CreateDirectory(folderPath);
            }

            return Path.Combine(folderPath, "stats.db");
        }

        public static string GetConnectionString()
        {
            return $"Data Source={GetDbPath()}";
        }

        public static void Initialize()
        {
            using (var connection = new SqliteConnection(GetConnectionString()))
            {
                connection.Open();

                string createSessionsTable = @"
                    CREATE TABLE IF NOT EXISTS Sessions (
                        SessionId INTEGER PRIMARY KEY AUTOINCREMENT,
                        StartTime DATETIME NOT NULL,
                        EndTime DATETIME
                    );";

                string createTracksTable = @"
                    CREATE TABLE IF NOT EXISTS Tracks (
                        TrackId INTEGER PRIMARY KEY AUTOINCREMENT,
                        Path TEXT UNIQUE NOT NULL,
                        Title TEXT,
                        Artist TEXT,
                        Album TEXT,
                        Format TEXT,
                        Year INTEGER DEFAULT 0
                    );";

                string createListensTable = @"
                    CREATE TABLE IF NOT EXISTS Listens (
                        ListenId INTEGER PRIMARY KEY AUTOINCREMENT,
                        TrackId INTEGER,
                        SessionId INTEGER,
                        Timestamp DATETIME DEFAULT CURRENT_TIMESTAMP,
                        SecondsListened REAL DEFAULT 0,
                        TotalSeconds REAL DEFAULT 0,
                        IsSkipped INTEGER DEFAULT 0,
                        FOREIGN KEY(TrackId) REFERENCES Tracks(TrackId),
                        FOREIGN KEY(SessionId) REFERENCES Sessions(SessionId)
                    );";

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "PRAGMA journal_mode = WAL;";
                    command.ExecuteNonQuery();

                    command.CommandText = "PRAGMA foreign_keys = ON;";
                    command.ExecuteNonQuery();

                    command.CommandText = createSessionsTable;
                    command.ExecuteNonQuery();

                    command.CommandText = createTracksTable;
                    command.ExecuteNonQuery();

                    command.CommandText = createListensTable;
                    command.ExecuteNonQuery();

                    command.CommandText = "PRAGMA table_info(Tracks);";
                    bool hasYear = false;
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            if (reader.GetString(1) == "Year")
                            {
                                hasYear = true;
                                break;
                            }
                        }
                    }

                    if (!hasYear)
                    {
                        command.CommandText = "ALTER TABLE Tracks ADD COLUMN Year INTEGER DEFAULT 0;";
                        command.ExecuteNonQuery();
                    }
                }
            }
        }
    }
}