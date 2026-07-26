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

---

# NHASH-01 (append, 2026-07-26) — 클론에서 POST-COMMIT이 초록이 됐다

앞 절의 미달 ①(*"`handoff-integrity`를 고치지 않았다"*)을 닫는다.

## ★ 결정적 실측

```
깨끗한 클론 · program-verify verify --gate POST-COMMIT
  → PASS 0/14 · worktreeCleanAtStart true · baselineCommit 16864d1b…
```

**처음으로 조율자 트리가 아닌 곳에서 게이트가 통과했다.**
그 전 상태(같은 명령, 같은 클론):

| 시점 | 결과 |
| --- | --- |
| 증거 보관 고치기 전 | `launch-disposition` **exit 2** (`launch root not found`) |
| `outbox` 추적 후 | **FAIL 1/14** — `handoff-integrity` hash-mismatch |
| **NHASH-01 후** | **PASS 0/14** |

## 무엇을 했나

**읽는 쪽(코덱스)**: `NormalizedContentHash.Compute` 하나를 만들고 `GateCleanCli`·
`HandoffIntegrityCli`가 쓴다.

**쓰는 쪽(조율자, 같은 커밋)**: `ProjectionCli.StampHashes`를 같은 함수로 바꾸고
`OrchestratorObserverCli`의 사본 8줄을 지웠다. **정규화 정의는 이제 저장소에 하나뿐이다.**

**둘은 같은 커밋에 있어야 했다.** 읽는 쪽만 바꾸면 기존 원시 바이트 해시와 안 맞는다.
지시서 §4 시험 6이 *"이 단계에서 `handoff-integrity`가 FAIL이어도 숨기거나
`WORKSTATE.json`을 고쳐 맞추지 마라"* 고 미리 못박았고, 실제로 반입 직후 exit 1이었다가
`projection`으로 4개를 재스탬프하니 **exit 0**이 됐다.

## 반증 시험 — 자기보고를 받지 않고 직접 돌렸다

`dotnet run --project docs/qa/gate-witness/NHashProbe/NHashProbe.csproj` → **exit 0, 4/4**

| 사례 | 기대 | 실측 |
| --- | --- | --- |
| CRLF본 vs LF본 | 같음 | **같음** |
| **내용이 한 글자 다름** | **다름** | **다름** |
| 줄 끝 공백·탭만 다름 | 같음 | 같음 |
| BOM 유무 | 같음 | 같음 |

**두 번째가 가장 중요하다.** 정규화가 표현 차이만 지우고 **내용 차이는 지우지 않는다.**
이게 아니면 `changedFiles` 무결성이 무의미해진다.

`gate-clean` 회귀도 확인했다 — dirty 픽스처 exit 1, clean 픽스처 exit 0으로 이전과 같다.

## 지표는 만족했으나 목적은 미달인 부분

1. **줄 끝 공백만 바뀐 변경은 이제 `changedFiles`에서 안 보인다.** 완화이며 숨기지 않는다.
   `gate-clean`에는 의도된 성질이고, 하네스마다 다른 정규화를 쓰면 *"같은 파일, 다른 해시"* 가
   되어 원래 문제로 돌아가므로 같은 함수를 쓰는 쪽을 택했다.
2. **조율자 작업 트리는 여전히 CRLF다.** 이제 해시가 정규화되므로 판정은 같지만,
   `git add`마다 CRLF 경고가 계속 뜬다. `git add --renormalize .`로 맞출 수 있으나
   **오늘 하지 않았다** — 별개 결정이고 diff가 크다.
3. **클론에서 `POST-EXECUTOR`·`LAND`는 안 돌렸다.** 전자는 더러운 트리를 전제하고
   후자는 18개라 시간이 든다. **`POST-COMMIT` 하나만 초록을 확인했다.**
