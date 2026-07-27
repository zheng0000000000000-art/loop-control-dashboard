#!/bin/bash
# 커밋 전에 도는 하네스 묶음. CI와 같은 목록(scripts/harness-list.txt)을 읽는다.
# 실패하면 그 명령의 출력을 그대로 찍고 멈춘다 — 출력을 버리면 원인을 추측하게 된다.
set -u
cd "$(dirname "$0")/.."
FAILED=0
while IFS= read -r c; do
  case "$c" in ''|'#'*) continue;; esac
  printf '%-38s' "$c"
  if out=$(dotnet run --project server --no-build -- $c 2>&1); then
    echo "ok"
  else
    code=$?
    echo "FAIL exit=$code"
    echo "$out" | tail -20 | sed 's/^/    /'
    FAILED=1
  fi
done < scripts/harness-list.txt
dotnet run --project server --no-build -- projection > /dev/null 2>&1
exit $FAILED
