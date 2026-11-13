using System;
using System.Globalization;
using System.IO;

namespace TeethSender
{
    /// <summary>
    /// Settings from "sender.ini" next to TeethSender.exe (created with these
    /// defaults on first run). Lines starting with # are comments.
    /// </summary>
    internal static class SenderSettings
    {
        /// <summary>IP address of laptop 2 (where Teeth runs).</summary>
        public static string Host = "192.168.50.2";

        /// <summary>Must match Port in Teeth's "App Files\link.ini".</summary>
        public static int Port = 5055;

        /// <summary>Enter must follow P within this many milliseconds.</summary>
        public static int KeyWindowMs = 500;

        public static string BaseDir
        {
            get { return AppDomain.CurrentDomain.BaseDirectory; }
        }

        private static string IniPath
        {
            get { return Path.Combine(BaseDir, "sender.ini"); }
        }

        public static void Load()
        {
            try
            {
                if (!File.Exists(IniPath))
                {
                    File.WriteAllLines(IniPath, new[]
                    {
                        "# Teeth Sender settings. Restart TeethSender.exe after changing anything here.",
                        "",
                        "# IP address of laptop 2 (the laptop that runs Teeth).",
                        "Host=" + Host,
                        "",
                        "# Must be the same number as Port in Teeth's App Files\\link.ini.",
                        "Port=" + Port.ToString(CultureInfo.InvariantCulture),
                        "",
                        "# Enter must come within this many milliseconds after P to count as the key button.",
                        "KeyWindowMs=" + KeyWindowMs.ToString(CultureInfo.InvariantCulture)
                    });
                    return;
                }

                foreach (string raw in File.ReadAllLines(IniPath))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#") || line.StartsWith(";")) continue;

                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;

                    string key = line.Substring(0, eq).Trim().ToLowerInvariant();
                    string val = line.Substring(eq + 1).Trim();

                    if (key == "host" && val.Length > 0) Host = val;
                    else if (key == "port") Port = ParseInt(val, Port, 1024, 65535);
                    else if (key == "keywindowms") KeyWindowMs = ParseInt(val, KeyWindowMs, 50, 5000);
                }
            }
            catch
            {
                // Keep the defaults if the file cannot be read.
            }
        }

        private static int ParseInt(string text, int fallback, int min, int max)
        {
            int v;
            if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out v)) return fallback;
            return Math.Max(min, Math.Min(max, v));
        }
    }
}
