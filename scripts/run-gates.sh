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
  # 하네스 하나 끝날 때마다 하트비트를 갱신한다 — 도커 측정·CI 대기처럼 이 묶음 전체가
  # 10분을 넘겨도 깨우기가 "조율자 없음"으로 안 읽게 한다. 최소 간격은 스크립트 안에서 지킨다.
  bash scripts/heartbeat-touch.sh "run-gates: $c" || true
done < scripts/harness-list.txt

# PowerShell 정적 검사. CI 에만 있고 로컬 커밋 전에는 안 돌고 있었다 - 2026-07-28 실측:
# 인코딩 검사를 매번 손으로 불러야 했다. 사람이 매번 같은 손질을 하고 있으면 코드가 할 일이다.
# 함정 검사기는 -SelfTest 를 먼저 돌린다. 안 잡게 된 검사기는 통과 도장만 찍는다.
for check in "check-script-encoding.ps1" "check-script-traps.ps1 -SelfTest" "check-script-traps.ps1" "backlog-policy-check.ps1 -SelfTest" "backlog-policy-check.ps1" "loop-smoke.ps1"; do
  printf '%-38s' "$check"
  if out=$(powershell -NoProfile -ExecutionPolicy Bypass -File scripts/$check 2>&1); then
    echo "ok"
  else
    code=$?
    echo "FAIL exit=$code"
    echo "$out" | tail -20 | sed 's/^/    /'
    FAILED=1
  fi
done
dotnet run --project server --no-build -- projection > /dev/null 2>&1
exit $FAILED
