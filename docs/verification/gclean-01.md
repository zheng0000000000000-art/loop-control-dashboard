# GCLEAN-01 반입 검증 — 재지 못한 것을 통과로 적지 않는다 (2026-07-27)

## 주체

- **구현**: 코덱스, `LAUNCH-GCLEAN-01-R2`, exit 0, 4분 8초.
- **검증·반입**: 조율 세션(Claude Opus 5).

## 무엇이 문제였나

```
gate-clean nonexistent-xyz    exit 0
gate-clean server  (경로가 없는 저장소에서)   exit 0
```

**경로가 없어도 PASS였다.** `POST-COMMIT` order 1이 `gate-clean server`라 **`server/`를 개명하면
그 게이트의 첫 검사가 아무것도 안 재고 통과**한다. 인자 없을 때의 기본값도 `server`라
다른 저장소에 겨누면 기본 실행부터 fail-open이었다.

`--manifestt` 오타가 fixture 대신 production을 재고 exit 0을 냈던 것과 같은 부류다 —
그때는 **옵션**이었고 이번은 **인자로 받은 경로**다.

## 조율자 재실행 — 코덱스 보고와 전부 일치

| 명령 | 코덱스 | 조율자 재실행 |
| --- | ---: | ---: |
| `gate-clean server` (clean 트리) | 0 | **0** |
| `gate-clean nonexistent-xyz` | 2 | **2** — `missingPaths: ["nonexistent-xyz"]` |
| `gate-clean server nonexistent-xyz` | 2 | **2** |
| `--status-fixture …/gate-clean-dirty.status` | 1 | **1** (회귀 없음) |
| `--status-fixture …/no-such-fixture.status` | 2 | **2** |

검증 클론에서 `gate-clean server`가 1로 나온 적이 있는데 **패치가 미커밋이라 `server/`가
더러웠던 것**이고, 커밋 후 0이었다. 그 구분을 안 했으면 회귀로 오판할 뻔했다.

컴파일 오류 0건. `measure`·`verify-behavior`·`context-pack-integrity`·`handoff-integrity`·
`doc-integrity`·`territory-check` 전부 0.

## 코덱스가 덤으로 잡은 것

> *"격리 worktree의 `.git` 포인터 파일도 저장소 루트로 인식하도록 기존 루트 탐색을 보완했다."*

**worktree에서는 `.git`이 디렉터리가 아니라 파일이다.** `Directory.Exists(".git")`로 루트를 찾으면
worktree 안에서는 그 지점을 지나쳐 버린다. 지시서에 없던 것을 스스로 찾아 고쳤다.

> **아직 안 고친 곳이 있다**: `CodexHarnessLauncherCli.RepoRoot()`도 같은 방식으로 찾는다.
> 융합에서 실행자를 다른 저장소에 겨눌 때 그 저장소가 worktree면 같은 문제가 난다. **남은 구멍으로 적는다.**

## 매니페스트 배선 (POST-COMMIT order 23·24)

등재 전에 실측했다. `gate-clean nonexistent-xyz` → 2, `--status-fixture <없는 파일>` → 2.

## 이 발사가 세 번 걸린 이유 — 둘 다 조율자 잘못이다

1. **첫 발사**: 조율자가 `Stop-Process -Name LocalFirstWorkflowDashboard.Server`로 exe 잠금을
   풀다가 **돌고 있던 발사까지 죽였다.** 증거가 안 만들어져 산출물을 주워 쓰지 않고 버렸다.
2. **R1**: 조율자가 **계속 append하는 관찰 문서를 참조 입력 핀으로 걸었다.**
   코덱스가 착수를 거부하고 요구·실제 해시를 둘 다 적어 보고했다. **`_header.md`가 금지하는 바로 그것**이다.

**둘 다 실행자가 옳게 행동했다.** 특히 R1은 파일을 하나도 안 건드리고 멈췄다.

## 지표는 만족했으나 목적은 미달인 부분

1. **`RepoRoot()`의 worktree 문제는 안 고쳤다**(위 §). 융합에 직접 걸리는 구멍이다.
2. **인자 없이 실행할 때의 기본값 `server`는 그대로다.** 지시서가 바꾸지 말라고 했다 —
   기본값이 바뀌면 매니페스트의 기존 검사가 다른 것을 재게 된다. 다만 **다른 저장소에서
   기본 실행은 이제 exit 2**가 되므로 조용히 통과하지는 않는다.
3. **게이트 증거 보고는 처분 기록 커밋에서 잰 것**이라 반입 시점 판정이 아니다(TERR와 동일).
