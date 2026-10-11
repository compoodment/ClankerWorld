#!/usr/bin/env bash
# Compare actual native dialogue, events and checkpoints between two checkouts.
set -euo pipefail
if [[ $# -lt 3 || $# -gt 6 ]]; then
    printf 'Usage: bash docs/development/conversation-system-equivalence/run.sh BASE CHECKOUT OUTPUT [SEEDS] [TICKS] [MODES]\n' >&2
    exit 2
fi
script_root=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
base_root=$(cd "$1" && pwd)
candidate_root=$(cd "$2" && pwd)
mkdir -p "$3"
output_root=$(cd "$3" && pwd)
seeds=${4:-town-project-real-donation}
ticks=${5:-64}
modes=${6:-generated}
if [[ -e "${output_root}/base" || -e "${output_root}/candidate" ]]; then
    printf 'Use an output directory without previous base/candidate results.\n' >&2
    exit 2
fi
command -v dotnet >/dev/null
command -v python3 >/dev/null
probe_root=$(mktemp -d)
trap 'rm -rf "${probe_root}"' EXIT
cp "${script_root}/Program.cs.txt" "${output_root}/probe.cs"
cp "${script_root}/../../../scripts/compare-tick-equivalence.py" "${probe_root}/compare.py"
for variant in base candidate; do
    target_root=${base_root}
    if [[ "${variant}" == candidate ]]; then target_root=${candidate_root}; fi
    project_root="${probe_root}/${variant}"
    (
        cd "${target_root}"
        dotnet new console --name TickEquivalence --output "${project_root}" --framework net10.0 --no-restore >/dev/null
        dotnet add "${project_root}/TickEquivalence.csproj" reference "${target_root}/src/ClankerWorld.Simulation/ClankerWorld.Simulation.csproj" >/dev/null
        cp "${output_root}/probe.cs" "${project_root}/Program.cs"
        dotnet run --project "${project_root}/TickEquivalence.csproj" --configuration Release -- \
            "${output_root}/${variant}" "${seeds}" "${ticks}" "${modes}" \
            "$(git rev-parse HEAD)" "$(git status --porcelain)"
    ) >"${output_root}/${variant}.log" 2>&1 || { cat "${output_root}/${variant}.log" >&2; exit 2; }
done
python3 "${probe_root}/compare.py" "${output_root}/base" "${output_root}/candidate"
