```context-pack
{
  "diId": "06C-2-R3",
  "requiredInputs": [
    { "path": "docs/directives/_header.md", "sha256": "b37a27f81792e82575a793f671839fdf463895e8ce4d1d4ccf7c5bea1213b2ee" },
    { "path": "docs/verification/_template.md", "sha256": "15f1b6dbdb703c94d6d7259b9417e17f438c980fad25b50b7ed96bc4da354b69" },
    { "path": "docs/handoff/queue/directive-06C-2-R2-trust-origin-highrisk-launcher-check.md", "sha256": "5e343814547844287c54eb1eb625df083e846ee3811a64bcaa5369e830ae24e8" },
    { "path": "docs/verification/06c2-r2-trust-origin-highrisk-launcher-check.md", "sha256": "a4033e299051aa35018fb1987f0f5b7622c15ee7ffd1f640eae2dec660353195" },
    { "path": "outputs/review/06C-2-R2.codex.md", "sha256": "8b5e2a906a510431deeef62a9376ad9252f96a739ef9a1b4b0f4d3c83270119f" },
    { "path": "server/StateApplierCli.cs", "sha256": "414ded71e2ef4cde27651c31a62a601d996f461a3cc116e08c7f0feb83fd1039" }
  ],
  "readOrder": [
    "docs/context/RUNTIME-INDEX.md",
    "outputs/review/06C-2-R2.codex.md",
    "docs/handoff/queue/directive-06C-2-R3-trust-origin-reason-and-hooks.md",
    "server/TrustOriginCli.cs",
    "server/StateApplierCli.cs",
    "docs/directives/_header.md"
  ],
  "forbiddenActions": ["git push", "approve", "reject", "import", "spawn-executor", "edit-baseline", "create-trust-origin-record-in-canonical"]
}
```

# 06C-2-R3 — high-risk reason and hooks presence

이 지시서는 docs/directives/_header.md의 불변 제약을 따른다.

- actor: CORE_INFRA_EXECUTOR (sonnet)
- 배경: 06C-2-R2는 독립 Codex 검수에서 FAIL. 남은 결함은 두 개다.
- 목표: 선행조건 9·10 검사가 **계약의 reason까지** 확인하게 한다.

## 반려 사유

정본: `outputs/review/06C-2-R2.codex.md`

1. high-risk 검사가 exit 1만 본다. stderr를 버리므로 `trusted-human-receipt-required`가 아닌 다른 exit 1도 PASS가 된다.
2. launcher settings의 `hooks` 판정이 `JsonObject hooks && hooks.Count > 0`로 좁다. 계약은 `hooks` 필드가 있으면 false다.

## 고칠 것

### A. high-risk는 exit code와 reason/code를 같이 확인하라

`RunHighRiskEnvelopeCheck(kind)`는 단순 `int`가 아니라 최소 `(exitCode, stderrText, matchedReason)` 수준의 결과를 반환해야 한다.

성공 조건:

- exit code == 1
- stderr JSON 또는 text에 `trusted-human-receipt-required`가 실제로 포함됨

PHASE_CHANGE, RECOVERY, REPLAY 세 종류 모두 이 조건을 만족해야 `CheckHighRiskClosed`가 true다.

금지:

- stderr를 `TextWriter.Null`로 버리지 마라.
- exit 1만으로 성공 판정하지 마라.
- self-test note에 기대 설명만 쓰고 실제 reason을 기록하지 않는 것 금지.

### B. hooks 필드 존재 자체를 fail-closed로 보라

`CheckAutoLauncherOff(root)` 규칙:

- settings 파일 없음 → skip 가능.
- settings 파일 있음 + malformed JSON → false.
- settings 파일 있음 + valid JSON + `hooks` 필드 없음 → 통과.
- settings 파일 있음 + valid JSON + `hooks` 필드 존재 → false.

`hooks` 값이 `{}`, `[]`, `"x"`, `null`이어도 필드가 있으면 false다.

### C. self-test를 추가/강화하라

필수 case:

| case | 기대 |
| --- | --- |
| high-risk 3종 | 각 case가 `exit=1`과 실제 `trusted-human-receipt-required` reason match를 기록 |
| `launcher-settings-hooks-empty-object` | `{"hooks":{}}` → false |
| `launcher-settings-hooks-array` | `{"hooks":[]}` → false |
| `launcher-settings-hooks-string` | `{"hooks":"x"}` → false |
| `launcher-settings-hooks-null` | `{"hooks":null}` → false |
| `launcher-settings-no-hooks` | `{}` → true |

기존 R1/R2 회귀 case는 유지한다.

## 완료 기준

1. `dotnet build server -c Release -nologo` → exit 0
2. `dotnet run --project server -c Release -- trust-origin --self-test` → exit 0
3. high-risk 3종 self-test 출력에 `reasonMatched=true` 또는 동등 필드가 있음
4. hooks 빈 객체/배열/문자열/null 모두 false, no-hooks는 true
5. R1/R2 회귀 case 유지
6. canonical 저장소에 trust-origin record 없음
7. `WORKSTATE.json`·`WORKSTATE.applier-log.jsonl` 변경 없음
8. `dotnet run --project server -c Release -- measure dev-pack` → violations 0

## 허용 파일 (allowlist)

- server/TrustOriginCli.cs
- docs/verification/06c2-r3-trust-origin-reason-and-hooks.md

## 금지

- `server/StateApplierCli.cs` 수정 금지. 읽기만.
- canonical trust-origin record 생성 금지.
- `WORKSTATE.json`·`WORKSTATE.applier-log.jsonl` 수정 금지.
- 기준 파일·측정 코드 수정 금지.
- git commit/push/tag 금지.
- approve/reject/import 금지.

## 보고

`docs/verification/06c2-r3-trust-origin-reason-and-hooks.md`를 작성한다.

반드시 적을 것:

- Codex R2 반려 사유 2개가 어떻게 닫혔는지.
- high-risk 3종의 실제 exit와 reason match.
- hooks 형태별 self-test 결과.
- canonical record 미생성과 WORKSTATE/applier-log 무결성.
- `## 지표는 만족했으나 목적은 미달인 부분`.
