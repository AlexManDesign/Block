#!/usr/bin/env bash
# Regenerates reference/data/* by running the reference game (reference/source/*.js + reference/tex) in
# headless Chromium and evaluating its own tables/functions. Needs node 18+, python3 and Chromium
# (Playwright uses /opt/pw-browsers/chromium; adjust run.mjs executablePath elsewhere).
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"; root="$(cd "$here/../.." && pwd)"
tmp="$root/tools/.cache/refgame"; mkdir -p "$tmp" "$root/reference/data"
cd "$here"
[ -d node_modules/playwright ] || PLAYWRIGHT_SKIP_BROWSER_DOWNLOAD=1 npm i --silent
mkdir -p site/assets; ln -sfn ../../../../reference/tex site/assets/tex
cp "$root/reference/source/genWorker.js" "$root/reference/source/meshWorker.js" site/
node make_dump_main.mjs
node run.mjs 5000 dump_tables.js > "$tmp/main_tables.json" 2>/dev/null
node run.mjs 5000 dump_atlas.js 2>/dev/null | sed 's/^data:image\/png;base64,//' | base64 -d > "$root/reference/data/atlas.png"
node run.mjs 5000 dump_av.js 2>/dev/null > "$root/reference/data/atlas_sources.json"
node run.mjs 5000 extract_main.js 2>/dev/null > "$tmp/main_extract.json"
node extract_blocks.mjs "$tmp/main_tables.json"
python3 merge_main_extract.py "$tmp/main_extract.json"
python3 - "$tmp/main_tables.json" "$root/reference/data/faces.json" <<'PY'
import json, sys
d = json.load(open(sys.argv[1]))
json.dump({'faces': d['M'], 'w': d['G0'], 'h': d['V0']}, open(sys.argv[2], 'w'), separators=(',', ':'))
PY
