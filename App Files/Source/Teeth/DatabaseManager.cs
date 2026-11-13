using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.IO;

namespace Teeth
{
    /// <summary>
    /// Phase 2: all SQLite access lives here. Nothing outside this class should
    /// ever open a SQLiteConnection directly - MainDashboard / ScheduleSettings
    /// (Phase 3+) will only ever talk to DatabaseManager.
    ///
    /// Replaces: schedule.json (ShiftConfig + WorkBlocks tables)
    ///           counts.json   (LiveCounts table)
    /// </summary>
    public class DatabaseManager
    {
        /// <summary>Full path to Teeth.sqlite, kept in the "App Files" folder
        /// next to the .exe (see AppBootstrap).</summary>
        public static readonly string DbPath = AppBootstrap.DbFile;

        private static readonly string ConnectionString =
            $"Data Source={DbPath};Version=3;Journal Mode=WAL;Synchronous=Normal;";

        private SQLiteConnection OpenConnection()
        {
            var conn = new SQLiteConnection(ConnectionString);
            conn.Open();
            return conn;
        }

        // ------------------------------------------------------------------
        //  INITIALIZATION
        // ------------------------------------------------------------------

        /// <summary>
        /// Creates Teeth.sqlite (if missing) and all three tables (if missing).
        /// Safe to call every time the app starts - CREATE TABLE IF NOT EXISTS
        /// means it never wipes existing data. Also seeds ShiftConfig /
        /// LiveCounts with a single default row the very first time.
        /// </summary>
        public void InitializeDatabase()
        {
            bool isNewFile = !File.Exists(DbPath);

            if (isNewFile)
            {
                SQLiteConnection.CreateFile(DbPath);
            }

            using (var conn = OpenConnection())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    CREATE TABLE IF NOT EXISTS ShiftConfig (
                        Id              INTEGER PRIMARY KEY CHECK (Id = 1),
                        ActiveLine      TEXT    NOT NULL DEFAULT 'Line A',
                        ActiveProfile   TEXT    NOT NULL DEFAULT 'Day Schedule',
                        GlobalPlanIn    INTEGER NOT NULL DEFAULT 0,
                        GlobalPlanOut   INTEGER NOT NULL DEFAULT 0
                    );

                    CREATE TABLE IF NOT EXISTS WorkBlocks (
                        Id          INTEGER PRIMARY KEY AUTOINCREMENT,
                        Profile     TEXT    NOT NULL,
                        BlockOrder  INTEGER NOT NULL,
                        Name        TEXT    NOT NULL,
                        StartTime   TEXT    NOT NULL,
                        EndTime     TEXT    NOT NULL,
                        IsBreak     INTEGER NOT NULL DEFAULT 0,
                        TargetIn    INTEGER NOT NULL DEFAULT 0,
                        TargetOut   INTEGER NOT NULL DEFAULT 0
                    );

                    CREATE INDEX IF NOT EXISTS IX_WorkBlocks_Profile
                        ON WorkBlocks (Profile, BlockOrder);

                    CREATE TABLE IF NOT EXISTS LiveCounts (
                        Id              INTEGER PRIMARY KEY CHECK (Id = 1),
                        CountIn         INTEGER NOT NULL DEFAULT 0,
                        CountOut        INTEGER NOT NULL DEFAULT 0,
                        FrozenSeconds   REAL    NOT NULL DEFAULT 0.0,
                        ShiftKey        TEXT    NULL,
                        LastLoggedIn    INTEGER NULL,
                        LastLoggedOut   INTEGER NULL
                    );
                ";
                cmd.ExecuteNonQuery();

                // Upgrade path: databases made before these columns existed get
                // them added in place. No data is touched or wiped.
                EnsureLiveCountsColumn(cmd, "ShiftKey", "TEXT");
                EnsureLiveCountsColumn(cmd, "LastLoggedIn", "INTEGER");
                EnsureLiveCountsColumn(cmd, "LastLoggedOut", "INTEGER");

                // Seed the two single-row tables so later UPDATE statements
                // always have a row to hit (INSERT OR IGNORE = no-op if the
                // row already exists from a previous run).
                cmd.CommandText = @"
                    INSERT OR IGNORE INTO ShiftConfig (Id, ActiveLine, ActiveProfile, GlobalPlanIn, GlobalPlanOut)
                    VALUES (1, 'Line A', 'Day Schedule', 0, 0);

                    INSERT OR IGNORE INTO LiveCounts (Id, CountIn, CountOut, FrozenSeconds)
                    VALUES (1, 0, 0, 0.0);
                ";
                cmd.ExecuteNonQuery();
            }

            SeedDefaultWorkBlocksIfEmpty();
        }

        private static void EnsureLiveCountsColumn(SQLiteCommand cmd, string column, string sqlType)
        {
            bool exists = false;
            cmd.CommandText = "PRAGMA table_info(LiveCounts);";
            using (var reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
                    {
                        exists = true;
                    }
                }
            }

            if (!exists)
            {
                cmd.CommandText = $"ALTER TABLE LiveCounts ADD COLUMN {column} {sqlType} NULL;";
                cmd.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// Phase 5 (the Settings window) is what's really meant to populate
        /// WorkBlocks. Until it exists, Phase 4's target math would have
        /// nothing to calculate against, so on a brand-new database we seed
        /// one sample timeline per profile using the same numbers that were
        /// in the original schedule.json. Only runs if WorkBlocks is empty,
        /// so it never overwrites anything you edit later.
        /// </summary>
        private void SeedDefaultWorkBlocksIfEmpty()
        {
            using (var conn = OpenConnection())
            using (var checkCmd = conn.CreateCommand())
            {
                checkCmd.CommandText = "SELECT COUNT(*) FROM WorkBlocks;";
                long existing = (long)checkCmd.ExecuteScalar();
                if (existing > 0)
                {
                    return;
                }
            }

            // NOTE: breaks are NOT separate rows anymore. A gap between one
            // block's End and the next block's Start (e.g. Work 2 ends
            // 10:15, Work 3 starts 10:25) IS the break - TargetCalculator
            // treats that unclaimed 10 minutes as idle time automatically.
            // Extended from 8 to 10 work blocks so the Day/Night timeline
            // now runs the full 12 hours (08:00-20:00 / 20:00-08:00)
            // instead of stopping at 17:20 partway through the shift.
            var dayBlocks = new List<WorkBlockModel>
            {
                Block(0, "Work 1",  "08:00", "09:00", false, 233, 230),
                Block(1, "Work 2",  "09:00", "10:15", false, 291, 288),
                Block(2, "Work 3",  "10:25", "11:30", false, 252, 249),
                Block(3, "Work 4",  "12:10", "13:00", false, 194, 192),
                Block(4, "Work 5",  "13:00", "14:00", false, 233, 230),
                Block(5, "Work 6",  "14:00", "15:00", false, 233, 230),
                Block(6, "Work 7",  "15:10", "16:00", false, 194, 192),
                Block(7, "Work 8",  "16:00", "17:20", false, 233, 230),
                Block(8, "Work 9",  "17:30", "18:30", false, 233, 230),
                Block(9, "Work 10", "18:30", "20:00", false, 348, 345),
            };

            var nightBlocks = new List<WorkBlockModel>
            {
                Block(0, "Work 1",  "20:00", "21:00", false, 233, 230),
                Block(1, "Work 2",  "21:00", "22:15", false, 291, 288),
                Block(2, "Work 3",  "22:25", "23:30", false, 252, 249),
                Block(3, "Work 4",  "00:10", "01:00", false, 194, 192),
                Block(4, "Work 5",  "01:00", "02:00", false, 233, 230),
                Block(5, "Work 6",  "02:00", "03:00", false, 233, 230),
                Block(6, "Work 7",  "03:10", "04:00", false, 194, 192),
                Block(7, "Work 8",  "04:00", "05:20", false, 233, 230),
                Block(8, "Work 9",  "05:30", "06:30", false, 233, 230),
                Block(9, "Work 10", "06:30", "08:00", false, 348, 345),
            };

            SaveWorkBlocks("Day Schedule", dayBlocks);
            SaveWorkBlocks("Night Schedule", nightBlocks);
        }

        private static WorkBlockModel Block(int order, string name, string start, string end,
            bool isBreak, int targetIn, int targetOut)
        {
            return new WorkBlockModel
            {
                BlockOrder = order,
                Name = name,
                StartTime = start,
                EndTime = end,
                IsBreak = isBreak,
                TargetIn = targetIn,
                TargetOut = targetOut
            };
        }

        // ------------------------------------------------------------------
        //  SHIFT CONFIG  (replaces top-level keys of schedule.json)
        // ------------------------------------------------------------------

        public ShiftConfigModel LoadConfig()
        {
            using (var conn = OpenConnection())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    SELECT ActiveLine, ActiveProfile, GlobalPlanIn, GlobalPlanOut
                    FROM ShiftConfig WHERE Id = 1;";

                using (var reader = cmd.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        return new ShiftConfigModel
                        {
                            ActiveLine = reader.GetString(0),
                            ActiveProfile = reader.GetString(1),
                            GlobalPlanIn = reader.GetInt32(2),
                            GlobalPlanOut = reader.GetInt32(3)
                        };
                    }
                }
            }

            // Table existed but the seed row was somehow missing - fall back
            // to safe defaults rather than throwing.
            return new ShiftConfigModel();
        }

        public void SaveConfig(ShiftConfigModel config)
        {
            using (var conn = OpenConnection())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    UPDATE ShiftConfig
                    SET ActiveLine = @line,
                        ActiveProfile = @profile,
                        GlobalPlanIn = @planIn,
                        GlobalPlanOut = @planOut
                    WHERE Id = 1;";

                cmd.Parameters.AddWithValue("@line", config.ActiveLine);
                cmd.Parameters.AddWithValue("@profile", config.ActiveProfile);
                cmd.Parameters.AddWithValue("@planIn", config.GlobalPlanIn);
                cmd.Parameters.AddWithValue("@planOut", config.GlobalPlanOut);
                cmd.ExecuteNonQuery();
            }
        }

        // ------------------------------------------------------------------
        //  WORK BLOCKS  (replaces the "presets" array of schedule.json)
        // ------------------------------------------------------------------

        /// <summary>Returns the ordered timeline of blocks for one profile
        /// ("Day Schedule" or "Night Schedule").</summary>
        public List<WorkBlockModel> LoadWorkBlocks(string profile)
        {
            var results = new List<WorkBlockModel>();

            using (var conn = OpenConnection())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    SELECT Id, Profile, BlockOrder, Name, StartTime, EndTime, IsBreak, TargetIn, TargetOut
                    FROM WorkBlocks
                    WHERE Profile = @profile
                    ORDER BY BlockOrder ASC;";
                cmd.Parameters.AddWithValue("@profile", profile);

                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        results.Add(new WorkBlockModel
                        {
                            Id = reader.GetInt32(0),
                            Profile = reader.GetString(1),
                            BlockOrder = reader.GetInt32(2),
                            Name = reader.GetString(3),
                            StartTime = reader.GetString(4),
                            EndTime = reader.GetString(5),
                            IsBreak = reader.GetInt32(6) != 0,
                            TargetIn = reader.GetInt32(7),
                            TargetOut = reader.GetInt32(8)
                        });
                    }
                }
            }

            return results;
        }

        /// <summary>
        /// Replaces the entire timeline for one profile in a single
        /// transaction (delete-then-reinsert), matching the way the
        /// Settings window (Phase 5) will save all 11 rows at once.
        /// </summary>
        public void SaveWorkBlocks(string profile, List<WorkBlockModel> blocks)
        {
            using (var conn = OpenConnection())
            using (var transaction = conn.BeginTransaction())
            {
                using (var del = conn.CreateCommand())
                {
                    del.Transaction = transaction;
                    del.CommandText = "DELETE FROM WorkBlocks WHERE Profile = @profile;";
                    del.Parameters.AddWithValue("@profile", profile);
                    del.ExecuteNonQuery();
                }

                using (var ins = conn.CreateCommand())
                {
                    ins.Transaction = transaction;
                    ins.CommandText = @"
                        INSERT INTO WorkBlocks
                            (Profile, BlockOrder, Name, StartTime, EndTime, IsBreak, TargetIn, TargetOut)
                        VALUES
                            (@profile, @order, @name, @start, @end, @isBreak, @targetIn, @targetOut);";

                    var pProfile = ins.Parameters.Add("@profile", DbType.String);
                    var pOrder = ins.Parameters.Add("@order", DbType.Int32);
                    var pName = ins.Parameters.Add("@name", DbType.String);
                    var pStart = ins.Parameters.Add("@start", DbType.String);
                    var pEnd = ins.Parameters.Add("@end", DbType.String);
                    var pIsBreak = ins.Parameters.Add("@isBreak", DbType.Int32);
                    var pTargetIn = ins.Parameters.Add("@targetIn", DbType.Int32);
                    var pTargetOut = ins.Parameters.Add("@targetOut", DbType.Int32);

                    foreach (var block in blocks)
                    {
                        pProfile.Value = profile;
                        pOrder.Value = block.BlockOrder;
                        pName.Value = block.Name;
                        pStart.Value = block.StartTime;
                        pEnd.Value = block.EndTime;
                        pIsBreak.Value = block.IsBreak ? 1 : 0;
                        pTargetIn.Value = block.TargetIn;
                        pTargetOut.Value = block.TargetOut;
                        ins.ExecuteNonQuery();
                    }
                }

                transaction.Commit();
            }
        }

        // ------------------------------------------------------------------
        //  LIVE COUNTS  (replaces counts.json)
        // ------------------------------------------------------------------

        public LiveCountsModel LoadLiveCounts()
        {
            using (var conn = OpenConnection())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT CountIn, CountOut, FrozenSeconds, ShiftKey, LastLoggedIn, LastLoggedOut FROM LiveCounts WHERE Id = 1;";

                using (var reader = cmd.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        return new LiveCountsModel
                        {
                            CountIn = reader.GetInt32(0),
                            CountOut = reader.GetInt32(1),
                            FrozenSeconds = reader.GetDouble(2),
                            ShiftKey = reader.IsDBNull(3) ? null : reader.GetString(3),
                            LastLoggedIn = reader.IsDBNull(4) ? (int?)null : reader.GetInt32(4),
                            LastLoggedOut = reader.IsDBNull(5) ? (int?)null : reader.GetInt32(5)
                        };
                    }
                }
            }

            return new LiveCountsModel();
        }

        /// <summary>
        /// Mirrors the Python autosave() pattern: caller is expected to only
        /// invoke this when values actually changed, so we don't hammer the
        /// disk every tick. This method itself is a plain, atomic UPDATE.
        /// </summary>
        public void SaveLiveCounts(LiveCountsModel counts)
        {
            using (var conn = OpenConnection())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    UPDATE LiveCounts
                    SET CountIn = @in,
                        CountOut = @out,
                        FrozenSeconds = @frozen,
                        ShiftKey = COALESCE(@shiftKey, ShiftKey)
                    WHERE Id = 1;";

                cmd.Parameters.AddWithValue("@in", counts.CountIn);
                cmd.Parameters.AddWithValue("@out", counts.CountOut);
                cmd.Parameters.AddWithValue("@frozen", counts.FrozenSeconds);
                // null = keep whatever shift is already stored
                cmd.Parameters.AddWithValue("@shiftKey", (object)counts.ShiftKey ?? DBNull.Value);
                cmd.ExecuteNonQuery();
            }
        }

        /// <summary>Remembers which counts were last written to the Excel log
        /// as "App Closed Mid-Shift" (null, null = clear it).</summary>
        public void SetLastLogged(int? countIn, int? countOut)
        {
            using (var conn = OpenConnection())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "UPDATE LiveCounts SET LastLoggedIn = @in, LastLoggedOut = @out WHERE Id = 1;";
                cmd.Parameters.AddWithValue("@in", (object)countIn ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@out", (object)countOut ?? DBNull.Value);
                cmd.ExecuteNonQuery();
            }
        }

        /// <summary>Resets counts to zero - used by "Reset Result" and by the
        /// automatic shift-rollover logic (Phase 4).</summary>
        public void ResetLiveCounts()
        {
            SaveLiveCounts(new LiveCountsModel { CountIn = 0, CountOut = 0, FrozenSeconds = 0.0 });
        }
    }
}
