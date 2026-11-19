using System;
using System.Collections.Generic;
using System.IO;

namespace Teeth
{
    internal enum ScanVerdict
    {
        /// <summary>New code (and on the allowed list, if one is used): count +1 OUT.</summary>
        Accept,
        /// <summary>This QR code was already counted: do NOT count again.</summary>
        Duplicate,
        /// <summary>A list of valid codes is in use and this code is not on it.</summary>
        Unknown,
        /// <summary>The scanner's text could not be read (nothing to check): counted like before.</summary>
        Unreadable
    }

    internal sealed class ScanResult
    {
        public ScanVerdict Verdict;
        public string Code;
        /// <summary>For Duplicate: when the same code was counted first.</summary>
        public DateTime FirstScanAt;
    }

    /// <summary>
    /// Decides whether a scanned QR code may count as +1 OUT.
    ///   1. Duplicate check: the same code is counted only once (per shift, per day
    ///      or ever - see DuplicateScope in link.ini). Remembered in Teeth.sqlite, so
    ///      it survives closing Teeth or restarting the laptop.
    ///   2. Optional "valid codes" list (ValidCodesFile in link.ini): when set, a
    ///      code that is not on the list is refused. The list is re-read whenever the
    ///      file changes, so the software that keeps the QR codes can simply export
    ///      its codes to that file.
    /// Always called on the UI thread.
    /// </summary>
    internal sealed class ScanGuard
    {
        private readonly DatabaseManager _db;

        private HashSet<string> _valid;            // null = no list in use
        private DateTime _validFileStamp = DateTime.MinValue;
        private string _validFilePath;

        public ScanGuard(DatabaseManager db)
        {
            _db = db;
            _db.PurgeOldScans(DateTime.Now.AddDays(-60));
        }

        /// <summary>The same QR must give the same key even if the scanner's
        /// Caps Lock state or stray spaces differ between two scans.</summary>
        public static string Normalize(string raw)
        {
            if (raw == null) return "";
            return raw.Trim().ToUpperInvariant();
        }

        public ScanResult Check(string rawCode, string shiftKey, DateTime now)
        {
            string code = Normalize(rawCode);
            if (code.Length == 0)
            {
                return new ScanResult { Verdict = ScanVerdict.Unreadable, Code = "" };
            }

            if (LinkSettings.ValidCodesFile.Length > 0)
            {
                LoadValidList();
                if (_valid != null && !_valid.Contains(code))
                {
                    return new ScanResult { Verdict = ScanVerdict.Unknown, Code = code };
                }
            }

            if (LinkSettings.DuplicateCheck)
            {
                DateTime? first = _db.FindScan(code, LinkSettings.DuplicateScope, shiftKey, now);
                if (first.HasValue)
                {
                    return new ScanResult { Verdict = ScanVerdict.Duplicate, Code = code, FirstScanAt = first.Value };
                }
            }

            return new ScanResult { Verdict = ScanVerdict.Accept, Code = code };
        }

        /// <summary>Call after an Accept has been counted.</summary>
        public void Remember(string code, string shiftKey, DateTime now)
        {
            if (code.Length > 0) _db.AddScan(code, shiftKey, now);
        }

        /// <summary>Manual Reset Result: forget the codes so the same trays can be counted again.</summary>
        public void ForgetAll()
        {
            _db.ClearScans();
        }

        /// <summary>Appends a refused scan to App Files\Logs\Scan-Rejected.txt (best effort).</summary>
        public static void LogRejected(ScanResult r, DateTime now)
        {
            try
            {
                string dir = Path.Combine(AppBootstrap.AppFilesDir, "Logs");
                Directory.CreateDirectory(dir);
                string reason = r.Verdict == ScanVerdict.Duplicate
                    ? "DUPLICATE (first counted " + r.FirstScanAt.ToString("yyyy-MM-dd HH:mm:ss") + ")"
                    : "NOT ON VALID LIST";
                File.AppendAllText(Path.Combine(dir, "Scan-Rejected.txt"),
                    now.ToString("yyyy-MM-dd HH:mm:ss") + "\t" + r.Code + "\t" + reason + Environment.NewLine);
            }
            catch
            {
                // Logging must never break counting.
            }
        }

        private void LoadValidList()
        {
            try
            {
                string path = LinkSettings.ValidCodesFile;
                if (!Path.IsPathRooted(path)) path = Path.Combine(AppBootstrap.AppFilesDir, path);

                if (!File.Exists(path))
                {
                    // File missing: do not block the line. Fail open, but tell the log.
                    _valid = null;
                    return;
                }

                DateTime stamp = File.GetLastWriteTimeUtc(path);
                if (_valid != null && stamp == _validFileStamp && path == _validFilePath) return;

                var set = new HashSet<string>();
                foreach (string line in File.ReadAllLines(path))
                {
                    // First column of a CSV, or the whole line.
                    string cell = line.Split(new[] { ',', ';', '\t' }, StringSplitOptions.None)[0];
                    string n = Normalize(cell.Trim('"'));
                    if (n.Length > 0) set.Add(n);
                }

                _valid = set;
                _validFileStamp = stamp;
                _validFilePath = path;
            }
            catch
            {
                // Unreadable (e.g. being written right now): keep the previous list.
            }
        }
    }
}
