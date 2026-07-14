```context-pack
{
  "diId": "06C-2-R4",
  "requiredInputs": [
    { "path": "docs/directives/_header.md", "sha256": "b37a27f81792e82575a793f671839fdf463895e8ce4d1d4ccf7c5bea1213b2ee" },
    { "path": "docs/verification/_template.md", "sha256": "15f1b6dbdb703c94d6d7259b9417e17f438c980fad25b50b7ed96bc4da354b69" },
    { "path": "docs/handoff/queue/directive-06C-2-R3-trust-origin-reason-and-hooks.md", "sha256": "444cdef48d808b06bb8349b3ad9a9f0c4700db1411e26dbf32673c352597347f" },
    { "path": "docs/verification/06c2-r3-trust-origin-reason-and-hooks.md", "sha256": "3d1094e95687f043210518ec1067d8854b20aa1d7d6246fab11bf7ef71eb86fe" },
    { "path": "outputs/review/06C-2-R3.codex.md", "sha256": "3ac99a4823cd4aea59ed9c444f61b8b1849a3d449513be2088ce376c620991b3" },
    { "path": "server/StateApplierCli.cs", "sha256": "414ded71e2ef4cde27651c31a62a601d996f461a3cc116e08c7f0feb83fd1039" },
    { "path": "skills/common/directive-writing.md", "sha256": "f2f153dc8af933e402646d071e89cac9d52d50638447d26925d844ebad985024" },
    { "path": "skills/common/verification.md", "sha256": "9476af73b5c5347569d96c98de022f739eec3b77ec15317f37ea0beae53876ea" }
  ],
  "readOrder": [
    "docs/context/RUNTIME-INDEX.md",
    "docs/directives/_header.md",
    "skills/common/directive-writing.md",
    "skills/common/verification.md",
    "outputs/review/06C-2-R3.codex.md",
    "docs/handoff/queue/directive-06C-2-R4-trust-origin-review-contract.md",
    "server/TrustOriginCli.cs",
    "server/StateApplierCli.cs"
  ],
  "forbiddenActions": ["git push", "approve", "reject", "import", "spawn-executor", "edit-baseline", "create-trust-origin-record-in-canonical"]
}
```

# 06C-2-R4 — trust-origin review contract closure

이 지시서는 docs/directives/_header.md의 불변 제약을 따른다.

- actor: CORE_INFRA_EXECUTOR (sonnet)
- 배경: 06C-2-R3는 독립 Codex read-only 검수에서 FAIL. 핵심 코드 조건은 대체로 닫혔지만 verification 보고 계약을 어겼고, high-risk reason 검사에 두 개의 잔여 위험이 남았다.
- 목표: R3 검수 FAIL 사유를 닫고, high-risk reason 검사를 더 엄격하게 만들어 검수자가 지적한 우회 경로를 제거한다.

## 반려 사유

정본: `outputs/review/06C-2-R3.codex.md`

1. `docs/verification/06c2-r3-trust-origin-reason-and-hooks.md`가 `skills/common/` 참조 의무를 `없음`으로 적었다. AGENTS.md 계약 위반이다.
2. `RunHighRiskEnvelopeCheck`가 `stderrText.Contains("trusted-human-receipt-required")`만 보므로 JSON reason/code의 정확한 값을 보장하지 않는다.
3. high-risk self-test 설명이 `$TEMP fixture` 사용이라고 적지만, 실제 `StateApplierCli.RunApply`는 canonical repo root의 WORKSTATE를 읽는다. 이 의존성을 숨기면 검증 설명이 부정확하다.

## 고칠 것

### A. common skill 참조 계약을 지켜라

작업 시작 전에 반드시 아래 파일을 읽고, R4 verification 문서에 그대로 기록한다.

- `skills/common/directive-writing.md`
- `skills/common/verification.md`

R3 verification 문서를 수정하지 말고, 새 문서 `docs/verification/06c2-r4-trust-origin-review-contract.md`를 작성한다.

### B. high-risk reason은 JSON 필드 정확 매칭으로 판정하라

`RunHighRiskEnvelopeCheck(kind)` 또는 보조 함수에서 stderr를 파싱해 아래 중 하나가 정확히 `trusted-human-receipt-required`인지 확인하라.

- JSON field `reason`
- 또는 기존 코드가 실제로 `code`를 쓰는 경우 JSON field `code`

금지:

- 단순 `stderrText.Contains("trusted-human-receipt-required")`만으로 PASS 처리 금지.
- exit 1만으로 PASS 처리 금지.
- `StateApplierCli.cs` 수정 금지.

권장:

- stderr가 여러 줄이면 각 줄을 JSON으로 파싱해 `reason` 또는 `code` 필드를 확인한다.
- 파싱 실패 라인은 무시 가능하지만, 어떤 라인에서도 정확 매칭이 없으면 `reasonMatched=false`.

### C. high-risk self-test의 의존성을 정직하게 검증/보고하라

현재 `StateApplierCli.RunApply`는 high-risk 분기 전에 envelope 파싱과 repo root WORKSTATE 로드를 수행한다. 따라서 high-risk self-test는 tmpRoot fixture만 쓰는 검증이 아니다.

해야 할 것:

- R4 verification 문서에 high-risk 3종 검사가 canonical repo WORKSTATE 의존성을 가진다는 사실을 명시한다.
- 이 의존성이 현재 검증에서 문제되지 않는 이유를 코드 근거로 적는다. 예: canonical WORKSTATE 로드 후 high-risk branch가 request/candidate 파일 읽기보다 먼저 fail-closed.
- 가능하면 self-test case의 note도 부정확하지 않게 수정한다.

### D. R3 회귀는 유지하라

기존 R1/R2/R3 회귀 case를 유지한다.

필수 유지:

- exact knownExceptions set
- `phaseChangeReady:false`, `replayReady:false`
- production precondition 기본 true 금지
- high-risk 3종: exit 1 + 정확 reason/code match
- hooks malformed/empty object/array/string/null fail-closed

## 완료 기준

1. `dotnet build server -c Release -nologo` → exit 0
2. `dotnet run --project server -c Release -- trust-origin --self-test` → exit 0
3. self-test 출력에서 high-risk 3종이 `exit=1`과 정확 reason/code match를 기록
4. hooks 빈 객체/배열/문자열/null 모두 false, no-hooks는 true
5. `dotnet run --project server -c Release -- verify-behavior` → `behaviorEqual:true`
6. `dotnet run --project server -c Release -- measure dev-pack` → violations 0
7. canonical `docs/handoff/trust-origin/TO-2026-001.json` 미생성
8. `docs/handoff/WORKSTATE.json`·`docs/handoff/WORKSTATE.applier-log.jsonl` 변경 없음
9. R4 verification 문서에 참조한 common skills 목록과 게이트 JSON 한 줄 기록

## 허용 파일 (allowlist)

- server/TrustOriginCli.cs
- docs/verification/06c2-r4-trust-origin-review-contract.md

## 금지

- `server/StateApplierCli.cs` 수정 금지. 읽기만.
- canonical trust-origin record 생성 금지.
- `WORKSTATE.json`·`WORKSTATE.applier-log.jsonl` 수정 금지.
- 기준 파일·측정 코드 수정 금지.
- git commit/push/tag 금지.
- approve/reject/import 금지.

## 보고

`docs/verification/06c2-r4-trust-origin-review-contract.md`를 작성한다.

반드시 적을 것:

- Codex R3 FAIL 사유가 어떻게 닫혔는지.
- 참조한 스킬 목록.
- high-risk 3종의 실제 exit와 정확 reason/code match.
- high-risk 검사의 canonical WORKSTATE 의존성과 그 해석.
- hooks 형태별 self-test 결과.
- canonical record 미생성과 WORKSTATE/applier-log 무결성.
- 게이트 결과 한 줄: `{"gate":"dev-pack","violations":0,"attempt":1}`
- `## 지표는 만족했으나 목적은 미달인 부분`.
