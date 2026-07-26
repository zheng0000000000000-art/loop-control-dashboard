# DAUTH-02 검증 — 착륙 절차를 스킬에 적는다

- **주체(actor)**: 산출은 **코덱스**(`CodexHarnessLauncher`, `LAUNCH-DAUTH-02`).
  검증·반입 집행은 **조율 세션(Claude Opus 5)**, 결재는 **사람**(git user `Jaehyuk`).
- **날짜**: 2026-07-26
- **선행**: `CPX-01` 착륙(`784ace6`). 지시서 §5가 정한 순서를 지켰다 — 하네스와 그 하네스를
  설명하는 절차를 같은 세션에 쓰지 않는다(`ADR-002` 자기 검증 회피).

## 사용한 하네스 (명령 · exit code · 수치)

| 명령 | exit | 수치 |
| --- | --- | --- |
| `codex-launch validate --request …` | 0 | `verdict: ACCEPTED` |
| `codex-launch launch … --manual` | 0 | `changedPaths` 1건, `scopeViolations` 0 |
| `context-pack-integrity` (①직후) | **1** | `failures: 1` (DLINT-01 stale) |
| `context-pack-integrity` (②이후) | **0** | `failures: 0` · `warnings: 0` · `cross: 4` |
| `measure dev-pack` | 0 | `{"gate":"dev-pack","violations":0,"attempt":1}` |
| `doc-integrity` | 0 | — |
| `handoff-integrity` | 0 | — |

## 반증 시험 (지시서 §4 — 절차이므로 문면 대조)

| # | 시험 | 기대 | 실측 |
| --- | --- | --- | --- |
| 1 | §1의 7개 항목이 문서에 있는가 | 7개 전부 | **7개 전부, 각 항목에 이유 포함** ✅ |
| 2 | 기존 §0~§6 제목 보존 | 전부 그대로 | **7개 제목 전부 보존, `git diff --numstat` = `22 0`(삭제 0)** ✅ |
| 3 | `doc-integrity` | exit 0 | **exit 0** ✅ |

시험 1에서 조율자의 첫 대조가 5번 항목을 "없음"으로 셌다. **오탐이었다** —
`grep "지우"`가 문서의 "지워서"를 못 잡은 것이고, 문서에는 `**pin을 지워서 해결하지 마라.**`가
그대로 있었다. **탐침이 틀렸지 산출물이 틀린 것이 아니었다.** 문면을 직접 열어 확인했다.

## 착륙 (두 걸음 — 이 문서가 설명하는 절차대로였다)

1. **①** 후보 패치 적용 → `context-pack-integrity` **exit 1**
   (`DLINT-01`이 `skills/common/directive-authoring.md`를 pin한다)
2. **②** `DLINT-01` pin 갱신 `df41047823eb…` → `79415d8d260c…` (`sha256sum`이 계산) → **exit 0**

**착륙 절차를 적는 문서가 그 절차대로 착륙했다.** 새 §7이 예고한 두 걸음이 그대로 재현됐다.

## 기준 변경

이 지시서를 런처로 쏘려면 `PermittedWriteRoots`에 `skills/`가 필요했다. 사람 결재를 받아 넓혔고
근거·경계·되돌리는 법은 `docs/handoff/BASELINE-CHANGES.md`(2026-07-26 항목)에 남겼다.
넓힌 폭은 `ADR-002`가 선언한 코덱스 영역까지이며, 경계가 여전히 서는지 실측했다:

```
skills/common/directive-authoring.md  -> ACCEPTED
server/OutboxManager.cs               -> allowed-paths-outside-codex-territory (exit 2)
docs/handoff/queue/x.md               -> allowed-paths-outside-codex-territory (exit 2)
outputs/launch/run-executor.ps1       -> allowed-paths-outside-codex-territory (exit 2)
```

## 참조한 스킬

`skills/common/directive-authoring.md` — §3(자기 모순 셀프 검사)·§4(해시는 프로그램이 계산).
이 지시서가 그 문서에 §7을 더했다.

## 지표는 만족했으나 목적은 미달인 부분

1. **완료 판정 체크리스트에는 착륙 항목을 넣지 않았다.** 지시서 §2가 기존 절 재작성을
   금지했고 §1도 요구하지 않았기 때문이다. 그래서 §7은 있지만 **내보내기 전 체크리스트는
   여전히 착륙을 묻지 않는다.** 다음에 이어야 할 자리다.
2. **충돌 4건은 그대로다.** 이번 착륙으로 두 pin을 갱신했지만 `CODEX-GATE-04`·`GATE-CP-01`이
   같은 파일을 쓰는 문제는 남아 있다. 절차를 적었을 뿐 순서를 정하지는 않았다.
3. **문면 대조 시험이 하네스가 아니다.** 조율자가 손으로 `grep`했고 한 번 오탐까지 냈다.
   `directive-lint`(DLINT-01)가 이 종류를 맡을 후보지만 아직 없다.
