#!/usr/bin/env bash
# Verify every commit between upstream/master and HEAD builds on its own.
# Usage: verify-commits.sh [solution-path]
set -uo pipefail

upstream="${UPSTREAM_REF:-upstream/master}"
solution="${1:-PocketMC.Desktop.sln}"
branch="$(git rev-parse --abbrev-ref HEAD)"
fail=0

for commit in $(git log --format=%h --reverse "$upstream"..HEAD); do
  git checkout -q "$commit" || { echo "checkout $commit failed"; fail=1; break; }
  subject="$(git log -1 --format=%s)"
  output="$(dotnet build "$solution" 2>&1)"
  errors="$(printf '%s\n' "$output" | grep -cE 'error [A-Z]+[0-9]+')"
  if [ "$errors" -eq 0 ]; then
    echo "PASS $commit $subject"
  else
    echo "FAIL $commit $subject"
    printf '%s\n' "$output" | grep -E 'error [A-Z]+[0-9]+' | head -5
    fail=1
  fi
done

git checkout -q "$branch"
[ "$fail" -eq 0 ] && echo "ALL COMMITS BUILD" || echo "SOME COMMITS FAIL"
exit "$fail"
