#!/bin/sh
# How Teeth.exe and TeethSender.exe in this package were built (Linux, Mono 6.8, mcs).
# They are compiled against the REAL .NET Framework 4.8 reference assemblies (/usr/lib/mono/4.8-api),
# so a method that does not exist in .NET Framework 4.8 gives a compile error instead of a crash on Windows.
# Build 1 used the wrong references and crashed with: Method not found String.Split(Char, StringSplitOptions).
# For Visual Studio use App Files\Source\Teeth.sln (net48) instead; this script is only for the supplied exes.
set -e
REF=/usr/lib/mono/4.8-api
ROOT="$(cd "$(dirname "$0")/.." && pwd)"          # App Files
LIB="$ROOT/lib"
mkdir -p /tmp/teeth-build/teeth /tmp/teeth-build/sender
cp "$ROOT/Source/Teeth/"*.cs "$ROOT/Source/Teeth/Teeth.ico" /tmp/teeth-build/teeth/
sed -i 's/readonly struct/struct/' /tmp/teeth-build/teeth/ShiftLogic.cs   # mcs does not know C# 7.2 readonly struct
cp "$ROOT/Source_Sender/TeethSender/"*.cs /tmp/teeth-build/sender/
( cd /tmp/teeth-build/teeth && mcs -nostdlib -noconfig -target:winexe -platform:x64 -out:/tmp/teeth-build/Teeth.exe -win32icon:Teeth.ico \
  -r:$REF/mscorlib.dll -r:$REF/System.dll -r:$REF/System.Core.dll -r:$REF/System.Data.dll -r:$REF/System.Drawing.dll \
  -r:$REF/System.Windows.Forms.dll -r:$REF/System.Xml.dll -r:$REF/System.Xml.Linq.dll -r:$REF/Facades/netstandard.dll -r:$REF/Facades/System.Runtime.dll \
  -r:"$LIB/System.Data.SQLite.dll" -r:"$LIB/ClosedXML.dll" -r:"$LIB/DocumentFormat.OpenXml.dll" -r:"$LIB/ExcelNumberFormat.dll" \
  -r:"$LIB/Irony.dll" -r:"$LIB/XLParser.dll" -r:"$LIB/SixLabors.Fonts.dll" *.cs )
( cd /tmp/teeth-build/sender && mcs -nostdlib -noconfig -target:winexe -platform:anycpu -out:/tmp/teeth-build/TeethSender.exe \
  -r:$REF/mscorlib.dll -r:$REF/System.dll -r:$REF/System.Core.dll -r:$REF/System.Drawing.dll -r:$REF/System.Windows.Forms.dll -r:$REF/Facades/System.Runtime.dll *.cs )
echo "Built /tmp/teeth-build/Teeth.exe and TeethSender.exe"
