#!/bin/sh
# Runs the REAL LinkClient.cs (laptop 1) and LinkServer.cs (Teeth) against a fake "cable" that can be pulled,
# plugged back, or can swallow the OK reply. Linux + Mono only (maintainer tool). Takes about 1 minute.
# Checks: counts wait during a cable pull, are saved in pending.txt, arrive exactly once, survive a restart of
# the sender, and wait while Teeth is closed.
set -e
HERE="$(cd "$(dirname "$0")" && pwd)"
APP="$(cd "$HERE/../.." && pwd)"
REF=/usr/lib/mono/4.8-api
mkdir -p /tmp/teeth-sim && cd /tmp/teeth-sim && rm -f pending.txt
mcs -nostdlib -noconfig -target:exe -out:Sim.exe -nowarn:649 -r:$REF/mscorlib.dll -r:$REF/System.dll -r:$REF/System.Core.dll -r:$REF/Facades/System.Runtime.dll \
  "$HERE/Sim.cs" "$APP/Source/Teeth/LinkServer.cs" "$APP/Source/Teeth/LinkSettings.cs" "$APP/Source_Sender/TeethSender/LinkClient.cs" "$APP/Source_Sender/TeethSender/SenderSettings.cs"
mono Sim.exe
