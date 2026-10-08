#!/usr/bin/env bash
# Downloads the test timings of main's latest green Verify runs, one folder per run, for
# ci-plan.js to read with readMainTimings:
#   bash .github/scripts/main-timings.sh <dir> <count>
# Needs GH_TOKEN with actions: read and GITHUB_REPOSITORY. A run whose timings can't be
# downloaded, for example because they have expired, is left out.
set -euo pipefail

dir="$1"
count="$2"
mkdir -p "$dir"
runs="$(gh api "repos/${GITHUB_REPOSITORY}/actions/workflows/ci.yml/runs?branch=main&event=push&status=success&per_page=${count}" \
  --jq '.workflow_runs[].id')"
for run_id in $runs; do
  (
    gh run download "$run_id" --repo "$GITHUB_REPOSITORY" --pattern 'test-timings-*' --dir "$dir/$run_id" \
      && echo "Timings from run ${run_id}" \
      || { echo "Could not download the timings of run ${run_id}"; rm -rf -- "${dir:?}/${run_id}"; }
  ) &
done
wait
