# FEAT-01 — 한정 이양: 게이트 클린 반입의 상위 AI 승인

```context-pack
{
  "diId": "FEAT-01",
  "requiredInputs": [
    { "path": "docs/directives/_header.md", "sha256": "b37a27f81792e82575a793f671839fdf463895e8ce4d1d4ccf7c5bea1213b2ee" },
    { "path": "docs/verification/_template.md", "sha256": "cca162b7cfc7387e3d369148c5d0d170cea9dfe73d86feb9c77a40ab44829145" },
    { "path": "docs/handoff/decisions/ADR-005-metric-vs-purpose.md", "sha256": "0b9fb7c5756b27923ceeafab6c8dcd44d00b3c73708d00abdb0887f1891a4bed" },
    { "path": "docs/handoff/QUOTA-POLICY.md", "sha256": "4bc62f76041527b6984eb2e9d3e0dc1d5a985c7329b62ae2ac65450711462a5c" },
    { "path": "skills/common/verification.md", "sha256": "9476af73b5c5347569d96c98de022f739eec3b77ec15317f37ea0beae53876ea" }
  ],
  "readOrder": [
    "docs/context/RUNTIME-INDEX.md",
    "docs/directives/_header.md",
    "docs/handoff/queue/directive-FEAT01-conditional-delegation.md",
    "docs/handoff/decisions/ADR-005-metric-vs-purpose.md",
    "docs/verification/_template.md"
  ],
  "forbiddenActions": ["git commit", "git push", "approve", "reject", "import", "spawn-executor", "edit-baseline"]
}
```

> Context Pack 형식은 `docs/directives/_header.md`의 「Context Pack」 절을 따른다. **`sha256`은 프로그램이 계산했다**(`Get-FileHash`, 검수자 2026-07-12). 손으로 적은 값이 아니다.
> **주의(사람 결재 대기)**: 이 지시서는 **안전 보류 중**이다 — `HUMAN-INBOX.md` 참조. 발사 금지.

이 지시서는 `docs/directives/_header.md`의 불변 제약을 따른다. 작업 시작 전 먼저 읽어라.
유형: implementation. 근거: 사람이 승인한 한정 이양안(게이트 클린 반입만 상위 AI가 검토·승인, 감사·일일캡·이상 시 자동중단+사람복귀, DECISIONS 기록). 현재 Tier2Approver가 활성 상태이므로 그 위에 조건을 얹는다.

## 전제 조건
server/ clean. 현재 Tier2Approver/결재 관련 코드 위치를 먼저 확인하고 설계(파일명 가정 금지).

## 목표
outbox 반입(import) 중 **게이트 클린 조건을 전부 충족하는 것만** 상위 AI(구독 실행자)가 승인할 수 있게 한다. 그 외 반입과 모든 proposal 결재는 사람 유지.

## 게이트 클린 조건 (전부 충족 시에만 AI 반입 승인 후보)
1. 반입 대상이 stale 아님(`originalFileHashes` 검사 통과 — 기존 stale 가드).
2. 반입 후 `measure` 위반 수 비악화(반입 전/후 비교, 사본에서 사전 측정).
3. 코어 3파일(Engine/Storage/Guardrails) 도메인 무지 유지.
4. 기준 파일(blueprint.json·workflow-definition.json) 무수정.
5. 대상 task가 검증 문서를 동반(verification 존재).

## 백스톱 (필수)
- **기본 꺼짐**: 설정 `conditionalDelegation.enabled` 기본 false. 코드가 자동으로 켜는 경로 없음(이양 결정=사람).
- **일일 캡**: `maxAiImportsPerDay`(기본 예: 5). 초과 시 사람 대기.
- **감사**: 모든 AI 반입을 `import.ai` 이벤트로 기록(taskId·게이트 결과·승인 근거·구독 계층·일일 카운터). 대시보드 감사 섹션(있으면 재사용).
- **이상 시 자동중단**: 반입 후 measure 악화·verify 실패 감지 시 롤백 proposal 생성 + AI 반입 자동 비활성화 + 사람 복귀 대기.
- **사람 우선**: 사람이 이미 대기시킨 항목은 AI가 건드리지 않음.

## 검수 기준 (검증 가능 7개)
1. `enabled` 기본 false에서 게이트 클린 task라도 AI 반입이 발생하지 않고 사람 대기로 남는다.
2. enabled=true에서 게이트 클린 task가 AI 반입되고 `import.ai` 이벤트에 게이트 결과·근거·계층·카운터가 남는다.
3. 게이트 실패 task(예: measure 악화 or 기준파일 수정 포함)는 AI 반입되지 않고 사람 대기.
4. 일일 캡 초과 시 이후 반입이 사람 대기로 남는다.
5. 반입 후 이상(measure 악화) 주입 시 자동 비활성화 + 롤백 proposal + 사람 복귀.
6. 기존 사람 반입 경로(approve-import/reject-import)는 무변경.
7. `measure dev-pack` 위반 비악화, `dotnet build server -c Release` 0/0.

## v9 산출물 + DECISIONS
WORKSTATE 갱신(diId FEAT-01), `docs/verification/feat01-conditional-delegation.md`, `docs/directives/FEAT01-conditional-delegation.md` 보관. **DECISIONS.md에 한정 이양 결정·조건·백스톱·되돌림 조건 기록**(이양은 사람이 켠다는 고정점 명시).

## 허용 파일 (allowlist)

- server/Tier2Approver.cs
- server/Tier2ApproverTestCli.cs
- docs/verification/feat01-conditional-delegation.md
- docs/handoff/WORKSTATE.json

> 이 목록 밖의 파일을 수정하면 산출물 전체가 반려된다. 필요하면 고치지 말고 중단하고 보고하라.

## 경계 / 보고
server/ + 위 문서 + DECISIONS.md만. dashboard 감사 UI가 필요하면 최소만(별도 판단). docs/qa/·docs/wiki/ 무접촉. **proposal 승인·거절 로직 무변경**(이번은 반입 이양만). git commit/push 금지. `-c Release`. stdout에 자가점검표. rate limit 시 QUOTA_SIGNAL.
