#!/bin/sh
# Runs the duplicate-scan tests (17 checks) on Linux with Mono + Mono.Data.Sqlite.
# Needs: apt-get install mono-mcs mono-devel libsqlite3-0
# Uses a copy of DatabaseManager.cs with System.Data.SQLite swapped for Mono.Data.Sqlite (Windows-only native DLL otherwise).
set -e
HERE="$(cd "$(dirname "$0")" && pwd)"; SRC="$HERE/../../Source/Teeth"; REF=/usr/lib/mono/4.8-api
T=$(mktemp -d); mkdir -p "$T/app"
sed -e 's#using System.Data.SQLite;#using SQLiteConnection = Mono.Data.Sqlite.SqliteConnection; using SQLiteCommand = Mono.Data.Sqlite.SqliteCommand;#' -e 's#Journal Mode=WAL;Synchronous=Normal;#Journal Mode=Wal;#' "$SRC/DatabaseManager.cs" > "$T/DatabaseManager.cs"
cp "$SRC/ScanGuard.cs" "$SRC/LinkSettings.cs" "$SRC/Models.cs" "$SRC/AppBootstrap.cs" "$HERE/Test-ScanGuard.cs" "$T/"
mcs -out:"$T/app/Test.exe" -r:$REF/System.Data.dll -r:$REF/System.Drawing.dll -r:$REF/System.Windows.Forms.dll -r:$REF/System.Xml.dll -r:/usr/lib/mono/4.5/Mono.Data.Sqlite.dll "$T"/*.cs
cp /usr/lib/mono/4.5/Mono.Data.Sqlite.dll "$T/app/"
cd "$T/app" && mono Test.exe
