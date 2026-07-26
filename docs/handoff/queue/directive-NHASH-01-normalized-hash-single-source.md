```context-pack
{
  "diId": "NHASH-01",
  "requiredInputs": [
    { "path": "docs/directives/_header.md", "sha256": "b37a27f81792e82575a793f671839fdf463895e8ce4d1d4ccf7c5bea1213b2ee" },
    { "path": "docs/verification/_template.md", "sha256": "15f1b6dbdb703c94d6d7259b9417e17f438c980fad25b50b7ed96bc4da354b69" }
  ],
  "readOrder": [
    "docs/context/RUNTIME-INDEX.md",
    "docs/directives/_header.md",
    "docs/handoff/queue/directive-NHASH-01-normalized-hash-single-source.md",
    "docs/verification/clone-truth-check.md",
    "server/Harness/GateCleanCli.cs",
    "server/Harness/HandoffIntegrityCli.cs"
  ],
  "forbiddenActions": ["commit", "push", "state-transition", "pass", "product-code-edit", "approve", "reject", "import", "edit-baseline"]
}
```

이 지시서는 `docs/directives/_header.md`의 불변 제약을 따른다.
**DI 유형: `harness`** → 반증 시험 필수.

---

# NHASH-01 — 정규화 해시를 한 곳에 두고 `handoff-integrity`가 그것을 쓴다

- actor: **HARNESS_EXECUTOR (codex)** — `server/Harness/**`는 `ADR-002` 코덱스 배타 영역이다.
- 근거: `docs/verification/clone-truth-check.md` · `.gitattributes`(2026-07-11)

---

## 0. 문제 (실측 2026-07-26)

깨끗한 클론에서 `POST-COMMIT`이 **FAIL 1/14**다.

```
handoff-integrity → hash-mismatch: server/Program.cs, server/OllamaExecutor.cs
```

두 파일은 **줄바꿈만 다르다**(조율자 트리 CRLF 2,405줄 / 클론 0줄, 정규화 후 바이트 동일).
`.gitattributes`는 `*.cs text eol=lf`이고 **저장소 기준은 LF다 — 클론이 옳다.**

```
gate-clean          GitTools식 NormalizedHash   ← 2026-07-11에 같은 데드락으로 넣음
handoff-integrity   SHA256(원시 바이트)          ← 안 받음  (HandoffIntegrityCli.cs:255)
```

그래서 `WORKSTATE.json`의 `changedFiles` 해시는 **작업 트리의 줄바꿈에 묶인다.**

## 0-A. 정규화 함수가 이미 **두 벌** 있다

```
server/Harness/GateCleanCli.cs:210        internal static string NormalizedHash(byte[] raw)
server/OrchestratorObserverCli.cs:139     private static string NormalizedHash(byte[] raw)   ← 사본
```

**셋째 사본을 만들지 마라.** `HREG-02`가 `BuiltInCommands`에서 없앤 것과 같은 병이고,
갈리면 두 하네스가 같은 파일에 다른 해시를 낸다.

## 1. 무엇을 하는가

1. 정규화 해시의 **정의를 하나만** 둔다. `server/Harness/` 아래 공용 자리에 놓아라
   (`BinaryFreshness.cs`가 같은 방식의 선례다). 이름·파일은 실행자가 정한다.
2. `GateCleanCli`가 그것을 쓰고 자기 정의를 지운다. **판정은 바뀌지 않아야 한다**(§4 시험 5).
3. `HandoffIntegrityCli`의 `changedFiles` 비교가 그것을 쓴다(`HandoffIntegrityCli.cs:255`).

`server/OrchestratorObserverCli.cs`와 `server/ProjectionCli.cs`는 **`server/` 루트라 영역 밖**이다.
조율자가 **같은 반입 커밋에서** 바꾼다(§6).

## 1-A. 이 정규화는 줄바꿈보다 넓다 — 알고 써라

현행 정의는 ①BOM 제거 ②CRLF/CR → LF ③**각 줄 끝 공백·탭 제거** ④끝 개행 정규화를 한다.
즉 **줄 끝 공백만 바뀐 변경은 해시가 같아진다.** `gate-clean`에는 의도된 성질이지만
`changedFiles` 무결성에는 **완화**다.

**임의로 더 세거나 약한 변형을 만들지 마라.** 하네스마다 다른 정규화를 쓰면
"같은 파일, 다른 해시"가 되고 그게 이 문제의 원인이다. §4 시험 3이 이 성질을 **명시적으로** 잰다.

## 2. 하지 않을 일 (하면 반려)

- `server/ProjectionCli.cs`·`server/OrchestratorObserverCli.cs` 수정 — 영역 밖(§6).
- `docs/handoff/WORKSTATE.json` 수정 — **상태 기록이다. 손대지 마라.**
- 정규화 규칙 변경(§1-A). **위치만 옮기고 소비자를 늘린다.**
- `gate-clean`의 `content-dirty` / `representation-only` 판정 변경.
- `GATE-MANIFEST.json` 수정.

## 3. 완료 조건

### 지표 기준 (기계 판정)

- [ ] `server/**`에 정규화 해시 **정의가 하나만** 남는다(사본 두 벌이 하나로)
- [ ] `gate-clean server` · `gate-clean --status-fixture …dirty.status` 판정이 **이전과 같다**
- [ ] `build-verify` **exit 0**
- [ ] §4의 6개 시험이 전부 기대값

### 목적 기준 (사람 판정)

**"같은 내용의 파일은 줄바꿈이 달라도 같은 해시를 낸다. 내용이 다르면 다른 해시를 낸다."**

## 4. 반증 시험 (전부 실측)

| # | 시험 | 기대 |
| --- | --- | --- |
| 1 | 같은 내용의 **CRLF본과 LF본** | **해시 동일** |
| 2 | **한 글자라도 내용이 다른 파일** | **해시 다름** — 정규화가 눈을 멀게 하지 않는다 |
| 3 | 줄 끝 공백만 다른 두 파일 | **해시 동일** — §1-A의 완화를 실측으로 남긴다 |
| 4 | BOM 있는 본과 없는 본 | **해시 동일** |
| 5 | `gate-clean` 기존 픽스처(`gate-clean-dirty.status`·`gate-clean-clean.status`) | **exit·판정 이전과 동일** |
| 6 | `handoff-integrity` (조율자 트리에서) | §아래 — **실패해도 된다** |

**시험 2가 가장 중요하다.** 정규화는 표현 차이만 지워야 한다. 내용 차이를 지우면
`changedFiles` 무결성이 무의미해진다.

**시험 3을 빼지 마라.** 완화를 숨기지 말고 수치로 남긴다.

### 시험 6 — `handoff-integrity`가 실패하는 것이 **정상이다**

`WORKSTATE.json`의 기존 해시는 **원시 바이트로 기록**돼 있다. 읽는 쪽만 정규화하면 안 맞는다.
쓰는 쪽(`ProjectionCli`)은 **영역 밖**이라 조율자가 같은 커밋에서 바꾸고 재스탬프한다(§6).

**그러니 이 지시서 단계에서 `handoff-integrity`가 FAIL이어도 그것을 숨기거나
`WORKSTATE.json`을 고쳐서 맞추지 마라.** 보고에 *"쓰는 쪽 미반영으로 예상된 실패"*라고 적어라.
**억지로 초록을 만들면 그게 기준 조작이다.**

## 5. 착륙

`context-pack-integrity`를 돌려 stale해진 pin이 있는지 확인하라.
**소유 지시서를 가정하지 마라 — stale로 지목된 경로를 grep해서 실제 소유자를 찾고,
그 경로가 `requiredInputs`인지 `readOrder`인지까지 보아라.**

## 6. 후속 (조율자가 **같은 반입 커밋에서** 한다)

1. `server/ProjectionCli.cs`의 `StampHashes`가 같은 함수를 쓰게 한다(쓰는 쪽).
2. `server/OrchestratorObserverCli.cs`의 사본을 지우고 같은 함수를 쓰게 한다.
3. `handoff-integrity --projection`으로 재스탬프한 뒤 `handoff-integrity` **exit 0** 확인.
4. **깨끗한 클론에서 `POST-COMMIT`을 돌려 초록을 확인한다.** 이것이 이 작업의 목적이며,
   조율자 트리에서만 확인하면 애초의 문제를 반복하는 것이다.

## 허용 파일 (allowlist)

- server/Harness/
- docs/qa/gate-witness/**

> 검증 문서는 영역 밖이라 조율자가 쓴다.
