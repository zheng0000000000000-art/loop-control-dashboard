# CODEX-DIRECTIVE-AUTHOR role plan

- 상태: 초안
- 작성일: 2026-07-14
- 범위: Codex가 sonnet 실행자에게 줄 지시서/프롬프트를 작성하는 역할을 맡을 수 있는지에 대한 역할 설계
- 전제: 이 문서는 실행 통로 구현이 아니다. 발사·approve/reject/import/push/merge는 사람 게이트다.

## 현재 판단

Codex는 이 저장소에서 **지시자 설계/지시서 작성자** 역할을 맡을 수 있다. 현재 파일 접근으로 세션 이관 문서, 설계 문서, 큐, 지시서 공통 제약, 검증 규칙을 읽을 수 있음을 확인했다.

확인한 입력:

| 분류 | 파일 |
| --- | --- |
| 저장소 규칙 | `AGENT-GUIDE.md`, `CLAUDE.md`, `docs/directives/_header.md` |
| 현재 상태 | `docs/context/RUNTIME-INDEX.md`, `docs/handoff/SESSION-BRIEF-2026-07-13.md`, `docs/handoff/REVIEWER-HANDOFF.md` |
| 운영 설계 | `docs/handoff/ORCHESTRATOR-PROGRAM-VISION.md`, `docs/plan/wp/CODEX-HARNESS-LAUNCHER-minimal-contract.md`, `docs/plan/wp/CODEX-HEADLESS-DISPATCH-CLEANUP-plan.md` |
| 결정 기록 | `docs/handoff/decisions/ADR-002-harness-ownership-split.md`, `docs/handoff/decisions/ADR-009-event-driven-coordinator.md`, `docs/handoff/decisions/ADR-015-harness-actor-substitution.md` |
| 큐 | `docs/handoff/SONNET-QUEUE.md`, `docs/handoff/CODEX-QUEUE.md` |
| 스킬 | `skills/common/directive-writing.md`, `skills/common/executor-launch.md`, `skills/common/hs-gate.md`, `skills/common/powershell-encoding.md`, `skills/common/root-cause-diagnosis.md`, `skills/common/verification.md` |

단, Codex가 지시서를 쓸 수 있다는 말은 그 지시서의 산출물을 PASS 처리할 수 있다는 뜻이 아니다. **지시자와 검수자는 다른 주체여야 한다.** 이 문서의 결론은 "Codex는 지시서를 작성할 수 있다"이지 "Codex가 작성-실행-검수-반입을 닫힌 루프로 수행할 수 있다"가 아니다.

## 역할 경계

| 역할 | 책임 | Codex 현재 가능 여부 | TRUSTED_BASELINE 전 | TRUSTED_BASELINE 후 |
| --- | --- | --- | --- | --- |
| 지시자 | Context Pack, allowlist, 완료 기준, 금지 행동, 검증 명령, QUOTA_SIGNAL 처리, 보고 형식을 갖춘 sonnet 지시서 초안 작성 | 가능 | 가능. 단 수동 문서 작성만 | 가능. launcher 구현 이후에도 지시서 작성과 발사는 분리 |
| 검수자 | 실행 산출물의 독립 재실행, negative test, PASS/FAIL/BLOCKED 판정 | 같은 지시서에 대해서는 불가 | 별도 reviewer 세션 또는 별도 read-only Codex가 수행 | Program Verifier와 별도 reviewer/read-only Codex가 수행 |
| 실행자 | 지시서에 따라 격리 작업공간에서 파일 수정 및 verification 작성 | 이 세션 역할 아님 | sonnet 수동 발사만. 발사는 사람 승인 필요 | 자동 발사는 별도 launch gate 이후에만 가능 |
| 조율자 | 큐 우선순위, 발사 시점, 종료 감지, 상태 갱신, 커밋 레인 관리 | 이 세션 역할 아님 | 자동 스케줄러 중단. 수동 dispatch만 | C# OrchestratorService 또는 승인된 조율 경로가 담당 |

핵심 경계:

- Codex 지시자는 **지시서를 쓴다**. 발사하지 않는다.
- Codex 지시자는 **검수 기준을 설계한다**. 자기 산출물의 최종 PASS를 찍지 않는다.
- Codex 지시자는 **산출물이 어떤 증거를 남겨야 하는지 요구한다**. 그 증거의 진위를 판정하는 주체는 별도다.
- Codex 지시자는 **코드 구현을 하지 않는다**. 이 문서의 범위는 문서화된 역할 설계다.

## 지시자 입력

Codex 지시자는 지시서 작성 전 아래 입력을 좁은 순서로 읽는다.

1. `docs/context/RUNTIME-INDEX.md`
2. `AGENT-GUIDE.md`
3. `CLAUDE.md`
4. `docs/directives/_header.md`
5. 해당 작업의 큐 항목과 선행 결정 ADR
6. 해당 작업이 수정할 경로와 직접 관련된 설계 문서
7. `skills/common/` 전체
8. 도메인 스킬은 변경 대상 경로의 트리거와 일치할 때만

지시서 작성 착수 전 자가 검사:

| 검사 | 질문 | 부족할 때 |
| --- | --- | --- |
| 완료 기준 | 실행 명령과 exit code 또는 수치로 검증 가능한가 | 선택지 딸린 질문으로 되묻고 대기 |
| 대상 범위 | allowlist로 쓸 파일·glob이 특정되는가 | 추측하지 않고 범위 선택지를 제시 |
| 원칙 충돌 | blueprint, workflow-definition, ADR, 금지선과 충돌하지 않는가 | "기준 변경입니까?"를 사람에게 확인 |
| 주체 분리 | 지시자·실행자·검수자가 분리되는가 | 같은 주체가 닫힌 루프를 만들면 발행 보류 |
| 상태 전제 | L0와 손문서가 충돌하지 않는가 | L0를 우선하고 충돌을 지시서 위험에 기록 |

## 지시서 출력 형식

sonnet에게 넘길 지시서는 최소한 다음 절을 가진다.

1. 제목과 task ID
2. `이 지시서는 docs/directives/_header.md의 불변 제약을 따른다`
3. 머리의 `context-pack` 블록
4. 목표와 비목표
5. 작업 절차
6. `## 허용 파일 (allowlist)`
7. 금지 행동
8. 완료 기준
9. 검증 명령
10. QUOTA_SIGNAL 처리
11. 산출물 보고 형식
12. 검수자에게 넘길 Evidence 목록

Context Pack 규칙:

```context-pack
{
  "diId": "DI-...",
  "requiredInputs": [
    { "path": "docs/context/RUNTIME-INDEX.md", "sha256": "<프로그램으로 계산한 sha256>" }
  ],
  "readOrder": [
    "docs/context/RUNTIME-INDEX.md",
    "docs/directives/_header.md",
    "docs/handoff/queue/<directive>.md"
  ],
  "forbiddenActions": [
    "git commit",
    "git push",
    "approve",
    "reject",
    "import",
    "spawn-executor",
    "edit-baseline"
  ]
}
```

작성 규칙:

- `sha256`은 LLM이 추정하지 않고 `Get-FileHash` 같은 프로그램 결과로 채운다.
- `requiredInputs`는 읽기 참조이고 `allowlist`는 쓰기 대상이다. 둘을 불필요하게 겹치지 않는다.
- 신규 지시서에는 `## 허용 파일 (allowlist)`를 기계 파싱 가능한 목록으로 둔다.
- 지시서 #12 이후 형식은 불변 제약 전문을 인라인으로 싣지 않고 `_header.md` 참조 한 줄만 둔다.

allowlist 예시:

```markdown
## 허용 파일 (allowlist)
- server/Harness/**
- docs/qa/fixtures/**
- docs/verification/<task>.md
```

완료 기준 예시:

```markdown
## 완료 기준
- `dotnet run --project server -c Release -- build-verify` exit 0
- `dotnet run --project server -c Release -- verify-behavior` exit 0, `behaviorEqual=true`
- `dotnet run --project server -- measure dev-pack` exit 0, violations 0
- 변경 파일이 allowlist 안에만 있다.
- verification 문서에 actor, 하네스 결과, 참조한 스킬, 게이트 JSON 한 줄이 있다.
```

산출물 보고 형식:

```markdown
## 실행자 보고
- actor: sonnet, taskId=<TASK>
- changedFiles:
- harness:
  - command:
  - expectedExit:
  - actualExit:
  - keyMetrics:
- verification:
- quota:
  - `QUOTA_SIGNAL` 발생 여부:
  - 중단 시 `CHANGED:`:
  - 중단 시 `NEXT:`:
- 목적 미달 자진신고:
```

## sonnet 발사 전 사람 게이트

sonnet 발사는 사람 게이트다. Codex 지시자는 지시서를 작성하고, 사람이 명시 승인하기 전에는 실행자를 발사하지 않는다.

발사 전 사람이 확인할 항목:

| 항목 | 기준 |
| --- | --- |
| 지시 도착 확인 | task ID echo 또는 파일 기반 ACK 등 현재 launcher 규약으로 확인 가능해야 한다 |
| 실행 확인 | PID, JSONL 이벤트, 산출물 존재 등 실체 근거가 있어야 한다 |
| 범위 대조 | 산출물 변경 파일이 지시서 allowlist 안이어야 한다 |
| QUOTA_SIGNAL | 한도 신호가 있으면 발사·재발사 판단을 사람이 한다 |
| 기존 진행 항목 | 이미 진행 중인 sonnet 작업이 있으면 새 발사를 하지 않는다 |

TRUSTED_BASELINE 전에는 자동 스케줄러와 자동 발사를 켜지 않는다. 수동 dispatch도 사람 승인 후에만 한다. `executor: "codex"` dispatch는 현재 실제 LLM 라우팅으로 취급하지 않는다.

## 검수자 분리

검수 주체 분리 규칙:

- 지시서를 쓴 Codex는 그 지시서 산출물을 최종 PASS 처리하지 않는다.
- 검수는 별도 reviewer 세션 또는 별도 read-only Codex 검수 세션이 수행한다.
- 검수자는 실행자 자기보고를 증거로 보지 않고, 하네스와 negative test를 직접 재실행한다.
- 검수자는 `docs/verification/<task>.md`의 `완료 판정`을 채운다.
- 지시자 산출물의 품질 검수도 별도 주체가 한다. 이 문서는 설계 초안이며, 최종 판정 문서가 아니다.

검수자가 재실행할 최소 항목:

| 분류 | 예 |
| --- | --- |
| 범위 | `scope-check` 또는 `git status` 정규화 대조 |
| 동작 | `build-verify`, `verify-behavior` |
| 측정 | `dotnet run --project server -- measure dev-pack` |
| 지시서 무결성 | `context-pack-integrity` 적용 대상이면 requiredInputs hash 대조 |
| 반증 | 지시서가 요구한 negative fixture 또는 실패 주입 |

## 금지 사항

Codex 지시자에게 금지되는 행위:

- approve/reject/import/push/merge 대행
- sonnet 발사 또는 자동 재발사
- TRUSTED_BASELINE 전 자동 발사·자동 조율 구현
- blueprint, workflow-definition, 측정 코드 변경으로 게이트 통과
- `Engine.cs`, `Storage.cs`, `Guardrails.cs`에 도메인 지식 삽입
- 실행자·검수자·조율자 역할을 한 세션에서 닫는 것
- `executor: "codex"` dispatch를 실제 Codex 실행 성공으로 간주하는 것
- `codex exec` 프로세스 exit code만으로 성공을 판정하는 것
- QUOTA_SIGNAL을 작업 실패나 지시서 이탈로 단정하는 것

## TRUSTED_BASELINE 전/후 차이

| 영역 | TRUSTED_BASELINE 전 | TRUSTED_BASELINE 후 |
| --- | --- | --- |
| Codex 지시자 | 문서와 지시서 초안 작성 가능 | 계속 가능 |
| sonnet 발사 | 사람 명시 승인 후 수동 발사만 | 별도 launch gate 통과 후 제한적 자동화 가능 |
| Codex launcher | 구현 착수 금지 | `CodexHarnessLauncher`와 Program Verifier 결속 구현 가능 |
| 자동 조율 | 중단 유지 | C# OrchestratorService 또는 승인된 조율 경로로 단계적 재개 가능 |
| 검수 | reviewer/read-only Codex가 직접 재실행 | Program Verifier + 독립 검수 병행 |
| 상태 반영 | 사람 결재와 StateApplier 경로만 | 동일. launcher가 state를 직접 쓰지 않음 |

## 다음 세션 프롬프트 예시

Claude 또는 Codex가 같은 역할을 이어받을 때 사용할 프롬프트 예시:

```text
너는 이 저장소의 지시자 설계/지시서 작성자다. 실행자나 검수자가 아니다.

먼저 다음을 읽어라:
1. AGENT-GUIDE.md
2. CLAUDE.md
3. docs/context/RUNTIME-INDEX.md
4. docs/plan/wp/CODEX-DIRECTIVE-AUTHOR-role-plan.md
5. docs/directives/_header.md
6. 필요한 큐 항목과 관련 ADR
7. skills/common/ 전체

해야 할 일:
- sonnet 실행자에게 줄 지시서 초안을 작성한다.
- Context Pack, allowlist, 완료 기준, 금지 행동, 검증 명령, QUOTA_SIGNAL 처리, 산출물 보고 형식을 포함한다.
- 발사하지 않는다.
- approve/reject/import/push/merge하지 않는다.
- 자기 지시서의 산출물을 PASS 처리하지 않는다.
- 검수자는 별도 reviewer 세션 또는 별도 read-only Codex 세션으로 지정한다.

작업 후에는 docs/verification/<task>.md에 actor, 사용한 하네스, 참조한 스킬, dev-pack 게이트 JSON 한 줄을 남겨라.
```

이 프롬프트를 Claude에게 줄 때도 같은 분리 원칙을 유지한다. Claude가 지시자를 맡으면 검수자는 별도 Codex read-only 또는 다른 reviewer 세션이어야 한다.

## 잔여 위험

- 손으로 쓴 큐 표와 L0가 어긋날 수 있다. 판단은 `docs/context/RUNTIME-INDEX.md`와 실체 산출물을 우선한다.
- `codex exec`는 헤드리스 진입점으로 관찰됐지만, exit code가 내부 실패를 의미하지 않을 수 있다. 성공 판정은 Program Verifier와 artifact 대조가 맡아야 한다.
- Context Pack 해시를 LLM이 손으로 채우면 검증이 창작이 된다. 해시는 프로그램으로 계산해야 한다.
- 지시자와 검수자를 같은 세션에서 운영하면 ADR-002의 실패가 재발한다.
- QUOTA_SIGNAL을 무시하면 부분 산출물을 실패나 이탈로 오귀인할 수 있다.
- TRUSTED_BASELINE 전 자동 발사 구현을 시작하면 WP-STATE-INTEGRITY land gate와 충돌한다.
- 이 문서는 역할 설계 초안이다. 최종 PASS 판정은 별도 검수 주체가 해야 한다.
