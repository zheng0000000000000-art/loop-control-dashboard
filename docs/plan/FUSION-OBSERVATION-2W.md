# 융합 2주 관찰 — 기준선과 관찰 항목

> **결정**: 범위를 미리 정하지 않고 **먼저 합치고 상황을 본다**(`ADR-018` §3-a).
> **이 문서의 목적은 하나다 — 2주 뒤 무엇이 달라졌는지 셀 수 있게 지금 값을 찍어두는 것.**
> 기준선이 없으면 "나아졌다"는 감상이지 측정이 아니다.

- 기준선 측정: **2026-07-27**
- 다음 판정: **2026-08-10경**
- 판정 주체: **사람**

## 0. 승격 규칙 (관찰 중 계속 적용)

team-loop `BRAINSTORM-WORKFLOW.md` §7이 절차를 정했으나 **횟수를 안 적어놨다.**
사용자 경험값으로 채운다:

```
같은 실패 유형 5회   →  승격 후보 (하네스 또는 스킬)
1~4회               →  기록만
검사기 오탐          →  학습하지 않고 하네스 결함으로 수정
```

**5회는 근거 없는 숫자가 아니다** — 사용자 경험값이고, 아래 기준선에서 가장 큰 실패 둘이
정확히 5회에 걸려 있다.

## 1. 기준선 — team-loop 실패 뭉치 (MCP `promotion_status`, 2026-07-27)

정책: `OPTIMISTIC` · `minimumOccurrences: 2` · `stableAfterCleanAudits: 3` · `autoRollback: true`

### 1-1. 미해결 실패 (횟수와 판정)

| 실패 코드 | 내용 | 횟수 | 판정 | 결정가능성 |
| --- | --- | ---: | --- | ---: |
| `EXECUTOR_FAILURE_IGNORED` | 실행자 실패를 검증이 무시했다 | **5** | HOLD | **0** |
| `FALSE_COMPLETION_NO_DELIVERABLE` | 승인됐는데 산출물이 안 바뀌었다 | **5** | HOLD | **0** |
| (AI 리뷰) | 리뷰어가 프롬프트의 **예시를 그대로** 반환 | 3 | HOLD | 0 |
| `AI_REVIEW_VERDICT_INVALID` | `APPROVE\|REJECT`를 문자 그대로 반환 | 2 | HOLD | 0 |
| `MAX_TURNS_RECOVERY_EXHAUSTED` | 12턴 소진, 축소 재시도도 12턴 소진 | 2 | HOLD | 0 |
| `LOOP_STALLED_MAX_TURN_DEPTH` | 자율 루프가 복구 깊이 소진 | 2 | HOLD | 0 |
| `executor_failed` | (격리된 하네스의 원인) | **31** | QUARANTINED | — |

**여섯 개 전부 `decidability: 0`이다.** *"기계가 PASS/FAIL로 가를 수 있는 근거가 없어
하네스로도 스킬로도 굳히지 않았다."*

### 1-2. 승격 결과

| 상태 | 건수 | 비고 |
| --- | ---: | --- |
| `STABLE` | 2 | `scope-violation-resolution`, `delivery-integrity-harness` (각 cleanAudits 3) |
| `ROLLED_BACK` | 2 | 낙관 승격 후 **같은 실패 재발**(5→6, 3→4)로 자동 롤백 |
| `FILED`(위키) | 5 | 결정가능성 0이라 규칙으로 굳히지 않음 |
| `QUARANTINED` | 1 | 하네스 테스트가 `spawn agent-executor ENOENT`로 실패 |

**롤백 2건은 시스템이 제대로 돈 것이다.** 격리 1건은 **존재하지 않는 명령을 검사로 등재**한 것이며,
이 저장소 규칙 하나로 막힌다 — *"등재 전에 기대 exit code를 실측하고 note에 수치를 적어라."*

### 1-3. 보드

`unknown-auction` workspace 기준 **활성 태스크 0건**. 그레이박스가 2026-07-26에 나왔고
아직 돌리지 않았다.

## 2. 기준선 — Local-First (2026-07-27)

| 항목 | 값 |
| --- | ---: |
| 게이트 검사 | POST-EXECUTOR 13 · POST-COMMIT 22 · LAND 18 = **53** |
| self-test 케이스 | state-transition 19 · recovery 8 · trust-origin 29 = **56** |
| CI | 리눅스 컨테이너(.NET 10) · 윈도우 — **초록** |
| 코덱스 발사 | 4회 — 반입 2(TERR-01·TERR-02), 반려 2(NET8-01 빌드 실패 · R1 환경 차단) |
| **로컬 모델이 돈 DI** | **0회** |
| ollama 이벤트 | `proposal.generated` 137 · `proposal.created` 137 · `review.tier1_completed` 154 — **전부 measure 루프 안** |

## 3. 2주 동안 셀 것

**뺀 것**: *"사람이 손으로 개입한 횟수"* — 지금은 개입이 곧 작업이라 지표가 안 된다(사용자 판단).

### 3-1. 루프가 닫히는가 (가장 중요)

- `unknown-auction`에 **태스크가 몇 건 완료됐나**, 그중 **반려율**
- 태스크가 **실제 커밋까지 갔나** — `Merge task/tsk_*` 증가분
- **그레이박스를 `npm test`가 덮는가** — 안 덮으면 "돌려봤다"의 증거가 못 된다

### 3-2. 실패가 줄었는가

- `EXECUTOR_FAILURE_IGNORED` **5 → ?**
- `FALSE_COMPLETION_NO_DELIVERABLE` **5 → ?**
- **새로 5회에 도달한 실패 유형이 있나** → 그게 다음 승격 후보다
- 롤백·격리 건수 변화

### 3-3. 비용 (Local-First 쪽이 무거운지)

- 게이트 1회 소요 시간(현재 커밋당 **2~4분**)
- 발사당 반려율, 재시도 횟수

### 3-4. 열린 채로 두는 것

- **로컬 모델로 DI를 한 번이라도 돌렸나** — `ADR-018` §3-b의 **D**. 2주 안에 한 번도 없으면
  **`Local-First`의 목표는 계속 열린 채다.** 그 사실을 판정에 명시한다.

## 4. 2주 뒤 판정에서 답할 것

1. **어느 쪽 루프가 실제로 닫혔나** — 이게 융합 범위를 정하는 근거다.
2. **5회에 도달한 실패가 무엇인가** — 그것만 승격한다. 나머지는 기록만.
3. **`decidability: 0`이던 둘이 가려지게 됐나** — 이 저장소의 대응물을 얹은 효과가 여기 나온다.
4. **Phase 0의 D를 열 것인가, 아니면 목표를 바꿀 것인가.**

## 5. 이 문서를 갱신하는 법

**기준선 표(§1·§2)는 고치지 마라.** 2026-07-27의 값이고, 고치면 비교가 사라진다.
관찰 결과는 **아래에 append**한다.

---

## 관찰 1 (2026-07-27) — 실행자를 남의 저장소에 겨눠봤다

**질문**: 실행자가 작동한다면 융합 작업 자체에도 쓸 수 있나? (사용자 제안)

### 답: 쓸 수 있다

`CodexHarnessLauncherCli.RepoRoot()`는 **CWD에서 위로 올라가며 `.git`을 찾고**,
`git worktree add`도 그 root에서 돈다. **CWD를 대상 저장소에 두면 그 저장소가 root가 된다.**

실측 — CWD를 `team-loop-lite-ai-learning`에 두고 로컬퍼스트 하네스 실행:

```
gate-clean docs      exit 1 · contentDirtyCount 1   ← 미추적 파일을 정확히 잡았다
```

**남의 저장소에서 실제로 판정한다.** 이것이 성립하면 `ADR-011`의 **D**(로컬 실행자로 한 바퀴)가
처음으로 열릴 수 있다 — 여태 돌릴 대상이 자기 자신뿐이었다.

### 막는 것 하나 — 영토가 하드코딩이다

`CodexTerritory.Roots = ["server/Harness/", "skills/", "docs/qa/"]`는 **이 저장소 구조**다.
team-loop은 `src/`·`test/`·`tools/`·`mcp/`라 발사 요청이 전부
`allowed-paths-outside-codex-territory`로 거절된다.

**그 자리에 들어갈 것이 team-loop에 이미 있다.** 그쪽 project context:
*"`allowedPaths`, verification evidence, delivery state, and approval remain **server-owned**."*
**태스크가 `allowedPaths`를 소유한다.** 하드코딩 상수를 **태스크가 주는 목록**으로 바꾸는 것이
융합의 첫 조각이다.

> **주의**: 그것은 **완화 방향**이다. 하드코딩의 목적이 *"실행자가 자기 영토를 스스로 넓힐 수 없게"*
> 였는데, 요청에서 받으면 요청 작성자가 넓힐 수 있다. **사람 결재 대상이다.**

### ★ 그 과정에서 fail-open을 찾았다 — 융합에 직격이다

```
gate-clean server            exit 0   ← team-loop에 없는 경로
gate-clean nonexistent-xyz   exit 0
```

**`gate-clean <경로>`는 경로가 존재하지 않아도 PASS를 낸다.**

**`POST-COMMIT` order 1이 `gate-clean server`다.** 대상을 team-loop으로 바꾸는 순간
**첫 검사부터 조용히 초록**이 된다. 이 저장소 안에서도 `server/`를 개명하면 같은 일이 난다.

`--manifestt`가 fixture 대신 production을 재고 exit 0을 냈던 것과 **같은 부류**다 —
그때는 옵션 오타였고 이번엔 **인자 경로**다. 옵션 검증(`CliOptions`)은 오늘 고쳤지만
**인자로 받은 경로의 실재 여부는 아무도 안 본다.**

**고칠 자리**: `server/Harness/GateCleanCli.cs` — **코덱스 영토**라 지시서가 필요하다.
**규칙 형태로 옮길 것**: *"검사 대상 경로가 존재하지 않으면 PASS가 아니라 입력 오류(2)다."*

---

## 관찰 2 (2026-07-27) — 도메인 오염이 이미 일어나 있다

**사용자 제약**: *"미지의 경매장 쪽 정보가 팀루프 자체에 섞이는 건 지양하고 싶다."*

**이미 섞여 있다.** MCP `list_skills`의 **전역 스킬**에 경매장/심사 도메인이 4개 있다:

```
judging-video-clarity            "첫 30초에 장르·코어 루프·AI 차별점이 보여야"
judging-ai-native-gameplay       "AI-on/off 런을 비교"
judging-technical-documentation  "프롬프트 나열식 문서는 반려"
judging-nhn-fit-human-review     "NHN 적합성은 참고 증거로만 표기"
```

**team-loop은 오케스트레이션 도구인데 그 전역 스킬이 특정 프로젝트의 심사 기준을 담고 있다.**
`workspaces/unknown-auction/`은 gitignore로 분리돼 있지만 **`data/skills.json`은 workspace 구분이
없다** — 한쪽에서 승격된 지식이 다른 쪽 에이전트에게도 규칙으로 실린다.

같은 경로로 들어온 다른 오염:

```
scope-violation-handling  →  "src/store.js 경로를 수정하지 않는다"
                             "public/index.html 경로를 수정하지 않는다"
                             "docs/MEMBER-USAGE-AI.md 경로를 수정하지 않는다"
```

**한 번의 실패에서 나온 파일 이름이 전역 규칙이 됐다.**

### 이것이 융합 목록에 더하는 것

- **스킬·하네스에 소속(scope)이 필요하다** — 전역인가, 특정 workspace 것인가.
  지금은 구분이 없어서 **승격이 곧 전역 오염**이다.
- 로컬퍼스트의 대응 규칙이 있다: `skills/domains/`는 **"이번 작업이 바꿀 파일 경로가 그 스킬의
  트리거와 일치할 때만" 읽는다.** 전역(`skills/common/`)과 도메인이 폴더로 갈려 있고,
  **애매하면 읽지 않는다**가 기본값이다. 그 구분이 team-loop에는 없다.

---

## 추가 관찰 항목 (2026-07-27, ADR-020 완화에 따라)

승인 권한을 에이전트에게 넘기면서 사람 눈을 안 거치게 된 것 셋. **2주 뒤에 실제 발생 횟수를 센다.**

| # | 관찰할 것 | 어떻게 세나 |
| --- | --- | --- |
| 1 | 판정 세션의 오판 | 승인된 항목 중 나중에 되돌린 것의 수 |
| 2 | 완료 조건 자체가 목적을 안 담은 경우 | `## 지표는 만족했으나 목적은 미달인 부분` 자진 신고 건수 |
| 3 | 반입한 산출물의 설계 품질 문제 | 반입 후 재작업이 필요했던 건수 |

**하나라도 나오면 규칙을 다시 조인다.** 0이면 완화를 유지한다.
같은 실패가 5번이면 규칙으로 올린다는 기준(사용자, 2026-07-27)을 여기에도 적용한다.

---

## 추가 관찰 항목 (2026-07-28, ADR-021 완화에 따라)

발사를 조율자 재량으로 넘기면서 사람 눈을 안 거치게 된 것. **2주 뒤에 센다.**

| # | 관찰할 것 | 어떻게 세나 |
| --- | --- | --- |
| 1 | 근거 없이 쏜 발사 | 발사 기록 중 "왜 쐈는지"가 안 적힌 건수 |
| 2 | 결과 없이 끝난 발사 | 발사했는데 반입도 반려도 안 된 건수 |
| 3 | 같은 발사의 반복 | 같은 지시서를 두 번 이상 쏜 건수 |

하나라도 나오면 규칙을 다시 조인다.
