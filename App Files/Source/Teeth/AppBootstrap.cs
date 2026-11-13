using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Teeth
{
    /// <summary>
    /// Keeps the project folder tidy. The folder next to Teeth.exe looks like:
    ///
    ///   Teeth.exe
    ///   HOW-TO-USE.pdf
    ///   Log Data\                 (Excel log)
    ///   App Files\                (everything else)
    ///       lib\                  (all .dll files + x64\ / x86\ SQLite native code)
    ///       Teeth.sqlite          (database)
    ///       Source\, Tools\ ...
    ///
    /// Windows normally only looks for .dll files next to the .exe, so this
    /// class teaches the app where to find them (and SQLite's native DLL)
    /// BEFORE any database / Excel code runs. If the DLLs sit right next to
    /// the .exe instead (e.g. a Visual Studio bin folder) that works too.
    /// </summary>
    internal static class AppBootstrap
    {
        public static readonly string BaseDir = AppDomain.CurrentDomain.BaseDirectory;
        public static readonly string AppFilesDir = Path.Combine(BaseDir, "App Files");
        public static readonly string LibDir = Path.Combine(AppFilesDir, "lib");
        public static readonly string DbFile = Path.Combine(AppFilesDir, "Teeth.sqlite");

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr LoadLibrary(string path);

        /// <summary>Must be the very first thing Main does.</summary>
        public static void Init()
        {
            Directory.CreateDirectory(AppFilesDir);

            // 1) managed DLLs (ClosedXML, System.Data.SQLite, ...) from App Files\lib
            AppDomain.CurrentDomain.AssemblyResolve += ResolveAssembly;

            // 2) SQLite's native DLL (SQLite.Interop.dll) from App Files\lib\x64 or \x86
            string arch = Environment.Is64BitProcess ? "x64" : "x86";
            if (Directory.Exists(LibDir))
            {
                // System.Data.SQLite honours this variable: <dir>\x64\SQLite.Interop.dll
                Environment.SetEnvironmentVariable("PreLoadSQLite_BaseDirectory", LibDir);
            }
            foreach (string dir in new[] { Path.Combine(LibDir, arch), Path.Combine(BaseDir, arch) })
            {
                string native = Path.Combine(dir, "SQLite.Interop.dll");
                if (File.Exists(native))
                {
                    try { LoadLibrary(native); } catch { }
                    break;
                }
            }

            MigrateOldDatabase();
        }

        private static Assembly ResolveAssembly(object sender, ResolveEventArgs args)
        {
            string name = new AssemblyName(args.Name).Name;
            if (name.EndsWith(".resources", StringComparison.OrdinalIgnoreCase)) return null;

            foreach (string dir in new[] { LibDir, BaseDir })
            {
                string path = Path.Combine(dir, name + ".dll");
                if (File.Exists(path))
                {
                    try { return Assembly.LoadFrom(path); } catch { }
                }
            }
            return null;
        }

        /// <summary>Older versions kept Teeth.sqlite right next to Teeth.exe.
        /// If that file is found (and no new one exists yet) move it into
        /// App Files so existing schedule + counts are not lost.</summary>
        private static void MigrateOldDatabase()
        {
            try
            {
                string oldDb = Path.Combine(BaseDir, "Teeth.sqlite");
                if (File.Exists(oldDb) && !File.Exists(DbFile))
                {
                    foreach (string suffix in new[] { "", "-wal", "-shm" })
                    {
                        string from = oldDb + suffix;
                        if (File.Exists(from)) File.Move(from, DbFile + suffix);
                    }
                }
            }
            catch { /* worst case: a fresh database is created */ }
        }

        private static Icon _icon;

        /// <summary>The logo embedded in Teeth.exe, for window title bars.</summary>
        public static Icon AppIcon
        {
            get
            {
                if (_icon == null)
                {
                    try { _icon = Icon.ExtractAssociatedIcon(Application_ExePath()); } catch { }
                }
                return _icon;
            }
        }

        private static string Application_ExePath()
        {
            return System.Windows.Forms.Application.ExecutablePath;
        }
    }
}
