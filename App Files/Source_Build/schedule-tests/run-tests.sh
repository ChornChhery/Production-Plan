#!/bin/sh
# Schedule tests: validator, Normalize, Target math, 2000 random schedules, and a real database round trip.
# Linux + Mono. Needs: apt-get install mono-mcs mono-devel libsqlite3-0
set -e
HERE="$(cd "$(dirname "$0")" && pwd)"; SRC="$HERE/../../Source/Teeth"; REF=/usr/lib/mono/4.8-api
T=$(mktemp -d); mkdir -p "$T/app"
sed -e 's#using System.Data.SQLite;#using SQLiteConnection = Mono.Data.Sqlite.SqliteConnection; using SQLiteCommand = Mono.Data.Sqlite.SqliteCommand;#' -e 's#Journal Mode=WAL;Synchronous=Normal;#Journal Mode=Wal;#' "$SRC/DatabaseManager.cs" > "$T/DatabaseManager.cs"
cp "$SRC/ShiftLogic.cs" "$SRC/Models.cs" "$SRC/ScheduleValidator.cs" "$SRC/AppBootstrap.cs" "$SRC/LinkSettings.cs" "$HERE/Test-Schedule.cs" "$T/"
sed -i 's/readonly struct/struct/' "$T/ShiftLogic.cs"
mcs -out:"$T/app/Test.exe" -r:$REF/System.Data.dll -r:$REF/System.Drawing.dll -r:$REF/System.Windows.Forms.dll -r:$REF/System.Xml.dll -r:/usr/lib/mono/4.5/Mono.Data.Sqlite.dll "$T"/*.cs 2>&1 | grep -v warning || true
cp /usr/lib/mono/4.5/Mono.Data.Sqlite.dll "$T/app/"
cd "$T/app" && mono Test.exe
