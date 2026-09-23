#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")"
BLENDER="${BLENDER:-blender}"
PYTHON="${PYTHON:-python3}"
mkdir -p previews
"$PYTHON" -c 'import PIL, numpy' || {
  echo 'Install Pillow and numpy in the selected Python environment first.' >&2
  exit 1
}
"$BLENDER" -b -t 4 --python-exit-code 1 -P scripts/verify_runtime.py 2>&1 | tee previews/cluster_preflight.log
# Fresh full batch: overwrite the two partial 4.5.3 outputs with this exact source.
"$BLENDER" -b -t 4 --python-exit-code 1 -P scripts/render_all.py 2>&1 | tee previews/cluster_render.log
cp scripts/check_assets.py ../cognitohazard-v-1/tools/check_assets.py
(cd ../cognitohazard-v-1 && "$PYTHON" tools/check_assets.py) 2>&1 | tee previews/cluster_validation.log
"$PYTHON" scripts/test_validator.py 2>&1 | tee previews/cluster_validator_tests.log
