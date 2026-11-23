#!/bin/sh
# Zips only the files of nsport/src that differ from what the PC already has (pushed/src), as $1 (delta zip),
# then records them as pushed. Unchanged files keep their date on the PC, so the build is incremental.
set -e
cd /home/claude
out=$1; rm -f "$out"
list=$(cd nsport && find src -type f | while read f; do cmp -s "$f" "../pushed/$f" || echo "$f"; done)
[ -z "$list" ] && { echo "no changes"; exit 0; }
(cd nsport && echo "$list" | zip -q "$out" -@)
echo "$list" | while read f; do mkdir -p "pushed/$(dirname "$f")"; cp "nsport/$f" "pushed/$f"; done
echo "$list" | wc -l
echo "$list"
