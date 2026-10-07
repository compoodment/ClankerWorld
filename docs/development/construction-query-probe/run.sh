#!/usr/bin/env bash
# Replay synthetic checkpoints through a selected checkout's real private runtime.
set -euo pipefail
mode=${1:?Use setup, replay or changes}
target_root=$(cd "${2:?Pass the target checkout}" && pwd)
checkpoint_dir=${3:?Pass the synthetic checkpoint directory}
output_prefix=${4:?Pass a fresh output prefix}
case "${mode}" in setup|replay|changes) ;; *) printf 'Use setup, replay or changes.\n' >&2; exit 2 ;; esac
script_root=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
mkdir -p "${checkpoint_dir}" "$(dirname "${output_prefix}")"
checkpoint_dir=$(cd "${checkpoint_dir}" && pwd)
output_prefix="$(cd "$(dirname "${output_prefix}")" && pwd)/$(basename "${output_prefix}")"
probe_dir=$(mktemp -d)
trap 'rm -rf "${probe_dir}"' EXIT
cd "${target_root}"
dotnet new console --output "${probe_dir}" --framework net10.0 --no-restore >/dev/null
dotnet add "${probe_dir}" reference "${target_root}/src/ClankerWorld.Simulation/ClankerWorld.Simulation.csproj" >/dev/null
cp "${script_root}/${mode}.cs.txt" "${probe_dir}/Program.cs"
case "${mode}" in
    setup) probe_args=("${checkpoint_dir}/setup.json" "$(git rev-parse HEAD)" "4:96,16:96,16:384" 256 1 "${script_root}/setup.cs.txt" "") ;;
    replay) probe_args=("${checkpoint_dir}" "${output_prefix}") ;;
    changes) probe_args=("${checkpoint_dir}/start-16-96.json" "${output_prefix}") ;;
esac
dotnet run --project "${probe_dir}" --configuration Release -- "${probe_args[@]}"
