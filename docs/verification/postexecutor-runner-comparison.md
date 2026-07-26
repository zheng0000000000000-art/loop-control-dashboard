# `POST-EXECUTOR` 대조 — 더러운 트리를 만들어 두 러너를 나란히 돌렸다

- **주체(actor)**: 조율 세션(Claude Opus 5). 결재는 사람(git user `Jaehyuk`).
- **날짜**: 2026-07-26 · 앞선 `docs/verification/dicc01-no-self-rebuild.md`의 미달 항목 ③을 닫는다.

## 왜 따로 재야 했는가

`POST-EXECUTOR`의 `gate-clean ['server']`는 **기대값이 1**이다 — 실행자가 방금 산출물을 냈고
아직 커밋 전인 상태를 전제한다(`POST-COMMIT`은 같은 명령에 0을 기대한다).
앞 대조는 깨끗한 트리에서 했으므로 그 게이트만 빠져 있었다.

`gate-clean`은 `git status --porcelain -- server`를 본다(`GateCleanCli.cs:34,52`).
**더러움이 `server/` 안에 있어야 한다.**

## (a) 비소스 더러움 — 두 러너 일치

`server/executor-scratch.txt`(미추적)를 두어 `gate-clean` **exit 1**을 만든 뒤:

| 러너 | 결과 |
| --- | --- |
| `di-completion-check --gate POST-EXECUTOR` | **PASS** 13검사 실패 0 (exit 0) |
| `program-verify verify --gate POST-EXECUTOR` | **PASS** 13검사 실패 0 (exit 0), `worktreeCleanAtStart` false |

세 게이트 전부 대조 완료:

| 게이트 | `di-completion-check` | `program-verify` |
| --- | --- | --- |
| `POST-COMMIT` | PASS 12/12 | PASS 12/12 |
| `POST-EXECUTOR` | **PASS 13/13** | **PASS 13/13** |
| `WP-STATE-INTEGRITY-LAND` | PASS 18/18 | PASS 18/18 |

## (b) 소스가 더러운 경우 — 게이트를 잴 수 없다

실행자가 실제로 바꾸는 것은 `.cs`다. 그러면 **바이너리가 낡는다.**
`server/Engine.cs`를 바이너리보다 새롭게 하고 재빌드 없이 돌렸다.

| 러너 | exit | verdict | 검사 실행 | newestSource |
| --- | --- | --- | --- | --- |
| `di-completion-check` | **2** | `NOT-MEASURED` | **0개** | `Engine.cs` |
| `program-verify` | **2** | `NOT-MEASURED` | **0개** | `Engine.cs` |

**이것은 결함이 아니라 참인 신호다.** 실행자가 소스를 바꿨는데 그 소스로 빌드하지 않은 채
게이트를 돌리면 **바뀌기 전 바이너리를 재는 것**이다. 그건 판정이 아니다.
`POST-EXECUTOR`를 돌리기 전에 빌드가 선행돼야 한다는 뜻이며, 그 순서가 이제 강제된다.

## 대조가 드러낸 차이 하나 — 고쳤다

처음 (b)를 쟀을 때 **결정은 같았지만 보고 모양이 달랐다.**

```
di-completion-check   verdict: NOT-MEASURED
program-verify        verdict 필드 자체가 없음 (error 객체만)
```

소비자가 `verdict`를 읽으면 내 쪽에서는 아무것도 얻지 못한다. **`ADR-016` §6이 난 자리가
정확히 이런 어긋남이다.** `ProgramVerifierCli`가 `gateId`와 `verdict: "NOT-MEASURED"`를
같이 내도록 고쳤고, 다시 재서 **낱말까지 일치**함을 확인했다.

## 사용한 하네스

| 명령 | exit |
| --- | --- |
| `gate-clean server` (더럽힌 뒤) | 1 — 의도한 조건 성립 확인 |
| `build-verify` | 0 |
| `measure dev-pack` | 0 · `{"gate":"dev-pack","violations":0,"attempt":1}` |
| `doc-integrity` · `context-pack-integrity` · `handoff-integrity` | 0 |

정리: `server/executor-scratch.txt` 삭제, `Engine.cs` mtime 복구용 재빌드 완료.
mtime은 **현재 시각**으로만 찍었다 — 미래로 찍으면 재빌드해도 계속 거부된다.

## 지표는 만족했으나 목적은 미달인 부분

1. **(a)의 더러움은 실행자가 실제로 만드는 종류가 아니다.** 미추적 텍스트 파일이지 소스 변경이 아니다.
   소스 변경으로는 (b)가 되어 애초에 잴 수 없으므로, **`POST-EXECUTOR`가 전제하는 상태와
   낡음 판정이 서로 밀어낸다.** 실무 순서는 "실행자 산출 → 빌드 → POST-EXECUTOR"여야 한다.
   그 순서를 문서나 하네스가 강제하지는 않는다 — 지금은 사람이 지켜야 한다.
2. **`ADR-016` §8의 정본 결정은 여전히 남아 있다.** 세 게이트 전부 일치했다는 것이
   두 벌을 유지해도 된다는 뜻은 아니다.
