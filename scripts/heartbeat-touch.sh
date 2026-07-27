#!/bin/bash
# 오래 걸리는 작업 중에도 하트비트를 살려 둔다. 최소 간격 미만이면 쓰지 않는다(파일 경합 방지).
#
# 왜 있는가: 도커 컨테이너 측정·CI 대기처럼 한 번에 10분을 넘기는 지점에서 하트비트가 낡으면
# 깨우기(coordinator-wake.ps1)가 조율자를 "없음"으로 읽고 판정 세션을 끼워 넣는다
# (2026-07-27 실측, docs/handoff/KNOWN-ISSUES.md "두 세션이 한 작업 트리를 동시에 만졌다").
#
# 하트비트 파일이 없으면(이 컴퓨터가 아니면) 조용히 넘어간다 — CI 컨테이너에는 이 경로가 없다.
set -u

HEARTBEAT_PATH="${HEARTBEAT_PATH:-/c/NHN Project/_ops/coordinator-heartbeat.json}"
MIN_INTERVAL_SEC="${HEARTBEAT_MIN_INTERVAL_SEC:-60}"
NOTE="${1:-작업 중}"

[ -f "$HEARTBEAT_PATH" ] || exit 0

now=$(date -u +%s)
mtime=$(stat -c %Y "$HEARTBEAT_PATH" 2>/dev/null || echo 0)
age=$(( now - mtime ))
if [ "$age" -lt "$MIN_INTERVAL_SEC" ]; then
  exit 0
fi

python - "$HEARTBEAT_PATH" "$NOTE" <<'PY'
import datetime
import json
import sys

path, note = sys.argv[1], sys.argv[2]
try:
    with open(path, encoding='utf-8') as f:
        data = json.load(f)
except Exception:
    data = {}
data['at'] = datetime.datetime.now(datetime.timezone.utc).strftime('%Y-%m-%dT%H:%M:%SZ')
data['note'] = note
with open(path, 'w', encoding='utf-8') as f:
    json.dump(data, f, ensure_ascii=False)
PY
