# 클론에서 돌려보고 알아낸 것 — 기록이 저장소와 함께 이동하는가

- **주체(actor)**: 조율 세션(Claude Opus 5). 사람 지시("증거 보관부터 하자"). 결재는 사람.
- **날짜**: 2026-07-26

## 왜 클론에서 돌렸나

`disposition.json`의 `gateReport`가 `outputs/gates/`를 가리키는데 그 경로가 gitignore된다는 것을
발견했다. 참조가 전부 추적되는지 **정적으로** 확인하고 고쳤다고 적었는데,
**그건 검증이 아니었다.** 실제로 클론해서 돌리니 한 단계가 더 있었다.

## 단계별 실측

| 단계 | 클론에서 `launch-disposition outbox` |
| --- | --- |
| 게이트 보고를 `docs/handoff/gate-evidence/`로 옮긴 뒤 | **exit 2** — `launch root not found: outbox` |
| `outbox/`의 세 종류를 추적하게 한 뒤 | **exit 0 · launchCount 19 · violations 0** |

`.gitignore:10`이 `outbox/`를 통째로 제외하고 있었다. **처분 기록 19건이 전부 미추적**이었고,
`POST-COMMIT`의 `launch-disposition ['outbox']`는 **다른 어떤 기계에서도 통과할 수 없었다.**
`BASELINE-CHANGES.md`에 두 건으로 기록했다.

## ★ 그런데 클론의 `POST-COMMIT`은 아직 초록이 아니다 — 오늘 일과 무관한 이유로

```
클론 POST-COMMIT → FAIL 1/14 (worktreeCleanAtStart true)
  handoff-integrity [] exp 0 got 1
    hash-mismatch: server/Program.cs
    hash-mismatch: server/OllamaExecutor.cs
```

내 트리에서는 통과한다. 원인을 **실측**했다.

| 파일 | 내 트리 | 클론 |
| --- | --- | --- |
| `server/Program.cs` | 105,728 B · CRLF 2,405줄 | 103,323 B · CRLF **0** |
| `server/OllamaExecutor.cs` | 30,139 B · CRLF 613줄 | 29,526 B · CRLF **0** |

**줄바꿈만 다르다**(정규화 후 바이트 동일 — `True` 확인).

`.gitattributes`는 `*.cs text eol=lf`이고 머리 주석은 이렇게 적혀 있다:

> 줄끝 정규화 — CRLF 재작성이 git에 '수정됨'으로 잡혀 발사조건①(server clean)을
> 영구 거짓으로 만들던 데드락 방지(2026-07-11). 저장소 기준은 LF.

**즉 클론이 옳고 내 작업 트리가 낡은 쪽이다.** 2026-07-11 이전의 CRLF가 내 디스크에 남아 있고,
git은 add 시점에만 정규화하므로 작업 트리를 되돌려 주지 않는다(커밋마다 뜨는 CRLF 경고가 그것이다).

### 두 하네스가 같은 위험을 다르게 다룬다

```
gate-clean          GitTools.NormalizedHash(head) == NormalizedHash(work)   ← 정규화한다
handoff-integrity   SHA256.HashData(File.ReadAllBytes(full))                ← 원시 바이트
```

`gate-clean`은 2026-07-11에 이 문제를 겪고 정규화를 넣었다. **`handoff-integrity`는 안 받았다.**
그래서 `WORKSTATE.json`의 `changedFiles` 해시는 **CRLF 작업 트리에서 계산된 값**이고,
저장소 기준(LF)으로 체크아웃한 어떤 기계에서도 맞지 않는다.

**이것은 오늘 만든 문제가 아니다.** 클론 실행이 드러냈을 뿐이다.

## 지표는 만족했으나 목적은 미달인 부분

1. **`handoff-integrity`를 고치지 않았다.** `server/Harness/`는 코덱스 영역이고, 무엇보다
   *"해시를 정규화한다"* 는 **기록의 의미를 바꾸는 결정**이다. 대안은 기록된 해시를
   LF 기준으로 다시 계산하는 것인데 그것도 상태 변경이다. **사람 결재로 올렸다.**
2. **내 작업 트리를 정규화하지 않았다.** `git add --renormalize .`로 맞출 수 있지만,
   그러면 `WORKSTATE.json`의 해시가 이번엔 내 트리에서도 안 맞게 된다.
   **어느 쪽이 참인지 정하는 것이 먼저다.**
3. **다른 게이트는 클론에서 안 돌렸다.** `POST-EXECUTOR`는 더러운 트리를 전제하고,
   `LAND`는 18개라 시간이 든다. `POST-COMMIT` 하나만 확인했다.
