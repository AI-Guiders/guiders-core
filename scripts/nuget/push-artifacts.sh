#!/usr/bin/env bash
# Push nupkg + paired snupkg (snupkg only when nupkg newly published).
# NuGet/Home#10475 — skip-duplicate nupkg + unconditional snupkg breaks symbol validation.
set -euo pipefail

GLOB="${1:?usage: push-artifacts.sh 'artifacts/AIGuiders.*.nupkg'}"
key="${NUGET_API_KEY:?NUGET_API_KEY required}"
src="${NUGET_SOURCE:-https://api.nuget.org/v3/index.json}"

shopt -s nullglob

push_nupkg() {
  local pkg="$1"
  local log
  if ! log=$(dotnet nuget push "$pkg" --api-key "$key" --source "$src" --skip-duplicate 2>&1); then
    echo "$log"
    return 1
  fi
  echo "$log"
  if echo "$log" | grep -qiE 'already exists|already been pushed|was not pushed'; then
    return 2
  fi
  return 0
}

shopt -s nullglob
files=( $GLOB )
if [[ ${#files[@]} -eq 0 ]]; then
  echo "No packages match: $GLOB" >&2
  exit 1
fi

for f in "${files[@]}"; do
  echo "=== $f ==="
  rc=0
  push_nupkg "$f" || rc=$?
  if [[ $rc -eq 1 ]]; then
    exit 1
  fi
  if [[ $rc -eq 2 ]]; then
    echo "skip snupkg (nupkg duplicate): ${f%.nupkg}.snupkg"
    continue
  fi
  sym="${f%.nupkg}.snupkg"
  if [[ -f "$sym" ]]; then
    echo "push $sym"
    dotnet nuget push "$sym" --api-key "$key" --source "$src" --skip-duplicate
  fi
done
