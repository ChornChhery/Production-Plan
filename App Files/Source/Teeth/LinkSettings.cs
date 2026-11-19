using System;
using System.Globalization;
using System.IO;

namespace Teeth
{
    /// <summary>
    /// Settings for the two-laptop setup, read from "App Files\link.ini".
    /// The file is created with these defaults on first run, so nothing has to
    /// be typed by hand unless a value needs tuning. Lines starting with # are
    /// comments.
    /// </summary>
    internal static class LinkSettings
    {
        /// <summary>TCP port Teeth listens on for "+1 IN" from laptop 1.
        /// The Sender on laptop 1 must use the same port.</summary>
        public static int Port = 5055;

        /// <summary>Seconds without any message from laptop 1 before the
        /// dashboard shows "NOT connected" (the Sender pings every 2 s).</summary>
        public static int LinkTimeoutSeconds = 6;

        /// <summary>Count a barcode scan as +1 OUT (global keyboard hook).</summary>
        public static bool ScanEnabled = true;

        /// <summary>A scan = at least this many characters followed by Enter.</summary>
        public static int ScanMinChars = 4;

        /// <summary>...with no more than this many milliseconds between keys.
        /// Scanners type in a few ms per character; people are far slower.
        /// Raise it if a slow scanner is not being counted.</summary>
        public static int ScanMaxGapMs = 50;

        /// <summary>The same QR code is counted as OUT only once.</summary>
        public static bool DuplicateCheck = true;

        /// <summary>How long a counted QR code is remembered: "shift" (default:
        /// the same code may count again in the next shift), "day" (until the next
        /// 00:00) or "always" (never counted again).</summary>
        public static string DuplicateScope = "shift";

        /// <summary>Optional text/CSV file with the valid QR codes, one per line
        /// (first column). Empty = no list, every new code is accepted. A relative
        /// path is relative to the "App Files" folder.</summary>
        public static string ValidCodesFile = "";

        private static string IniPath
        {
            get { return Path.Combine(AppBootstrap.AppFilesDir, "link.ini"); }
        }

        public static void Load()
        {
            try
            {
                if (!File.Exists(IniPath))
                {
                    WriteDefaults();
                    return;
                }

                foreach (string raw in File.ReadAllLines(IniPath))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#") || line.StartsWith(";")) continue;

                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;

                    string key = line.Substring(0, eq).Trim();
                    string val = line.Substring(eq + 1).Trim();

                    switch (key.ToLowerInvariant())
                    {
                        case "port": Port = ParseInt(val, Port, 1024, 65535); break;
                        case "linktimeoutseconds": LinkTimeoutSeconds = ParseInt(val, LinkTimeoutSeconds, 3, 120); break;
                        case "scanenabled": ScanEnabled = !(val == "0" || val.Equals("false", StringComparison.OrdinalIgnoreCase) || val.Equals("no", StringComparison.OrdinalIgnoreCase)); break;
                        case "scanminchars": ScanMinChars = ParseInt(val, ScanMinChars, 2, 50); break;
                        case "scanmaxgapms": ScanMaxGapMs = ParseInt(val, ScanMaxGapMs, 5, 500); break;
                        case "duplicatecheck": DuplicateCheck = !(val == "0" || val.Equals("false", StringComparison.OrdinalIgnoreCase) || val.Equals("no", StringComparison.OrdinalIgnoreCase)); break;
                        case "duplicatescope":
                            string sc = val.ToLowerInvariant();
                            if (sc == "shift" || sc == "day" || sc == "always") DuplicateScope = sc;
                            break;
                        case "validcodesfile": ValidCodesFile = val.Trim('"'); break;
                    }
                }
            }
            catch
            {
                // A broken ini must never stop the shop floor: keep defaults.
            }
        }

        private static int ParseInt(string text, int fallback, int min, int max)
        {
            int v;
            if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out v)) return fallback;
            if (v < min) return min;
            if (v > max) return max;
            return v;
        }

        private static void WriteDefaults()
        {
            string[] lines =
            {
                "# Teeth two-laptop settings. Restart Teeth after changing anything here.",
                "",
                "# Port that laptop 1 sends \"+1 IN\" to (must match the Sender's sender.ini).",
                "Port=" + Port.ToString(CultureInfo.InvariantCulture),
                "",
                "# Seconds of silence before the screen says laptop 1 is NOT connected.",
                "LinkTimeoutSeconds=" + LinkTimeoutSeconds.ToString(CultureInfo.InvariantCulture),
                "",
                "# Scanner = Result OUT. A scan is ScanMinChars or more characters typed",
                "# fast (gap between keys <= ScanMaxGapMs) and ended with Enter.",
                "# If real scans are missed, raise ScanMaxGapMs (try 80). If normal typing",
                "# is counted by mistake, lower it or raise ScanMinChars.",
                "ScanEnabled=true",
                "ScanMinChars=" + ScanMinChars.ToString(CultureInfo.InvariantCulture),
                "ScanMaxGapMs=" + ScanMaxGapMs.ToString(CultureInfo.InvariantCulture),
                "",
                "# Same QR scanned again = NOT counted again (red message on screen).",
                "# DuplicateScope: shift = may count again next shift, day = once per day,",
                "# always = never counted again.",
                "DuplicateCheck=true",
                "DuplicateScope=shift",
                "",
                "# Optional: text/CSV file of valid QR codes (one per line, first column),",
                "# e.g. exported by the software that keeps the codes. A code not in the",
                "# file is refused. Leave empty to accept every new code.",
                "ValidCodesFile="
            };
            File.WriteAllLines(IniPath, lines);
        }
    }
}
