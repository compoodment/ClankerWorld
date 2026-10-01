#!/usr/bin/env bash
# usage: shoot.sh <only-list or all> [themes]
S=/tmp/claude-0/-home-user-ClankerWorld/62ab178e-62a2-5836-a4b8-0ab0a61611f1/scratchpad
cd /home/user/ClankerWorld/src/ClankerWorld.GodotClient
dotnet build ClankerWorld.GodotClient.csproj 2>&1 | grep -E " error " | sort -u | head
ONLY=""; [ "$1" != "all" ] && ONLY="--only=$1"
for t in ${2:-light}; do
  DISPLAY=:99 CI=true timeout 400 godot --path . --rendering-driver opengl3 -- --fs-out=$S/audit/${t:0:1} --fs-size=1920x1080 --fs-scale=0 --fs-theme=$t --audit2 $ONLY > $S/audit/run-$t.log 2>&1
  echo "$t exit=$?"
done
grep -h "AUDIT2\|SCRIPT ERROR\|Unhandled\|ERROR: " $S/audit/run-*.log | grep -v "ALSA\|status < 0" | head
