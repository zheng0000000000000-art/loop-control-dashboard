# ADR-018 — 이 저장소의 판정층을 `team-loop-lite-ai-learning`이 흡수한다

- 상태: 사람 승인 대기 (**결정 자체는 사람이 이미 내렸다. 이 문서는 그 결정을 저장소에 처음 기록하는 것이다.**)
- 일시: 2026-07-27
- 제안: 사람(사용자) — 조율 세션(Claude Opus 5)이 받아 적음
- 근거 문서: `C:\NHN Project\team-loop-lite-ai-learning\docs\archive\migrations\LOCAL-FIRST-IMPORTS.md`,
  `docs/handoff/decisions/ADR-002-harness-ownership-split.md`, `ADR-015-harness-actor-substitution.md`

## 1. 상황

**결정은 있었는데 어느 저장소에도 없었다.**

2026-07-27 세션에서 사용자가 말했다 — *"팀루프랑 로컬 퍼스트 저쪽이랑 두개를 합치려고 했었어"*,
*"직전에는 팀루프쪽에서 융합 하기로 했었어"*, *"흡수긴 한데 합치는 대상은 몰라도 형태는 2번이였어."*

조율자가 양쪽을 찾았고 **없었다**:

- 이 저장소: `docs/handoff/`·`docs/plan/`·`docs/handoff/decisions/`에 융합 언급 없음.
- team-loop: `docs/`·최근 30커밋·`docs/daily/`에 없음. `통합` 문자열 매치는 **전부 문서 병합 이야기**다.

**그 결과 2026-07-27 세션 전체를 이 저장소의 C# 개발에 썼다** — 인수인계만 보고는 알 수 없었다.
이건 이 저장소가 반복해서 싸워 온 실패 유형(기록과 실체의 불일치)의 **본체 사례**다.
그래서 이 ADR을 먼저 남긴다.

### 실측한 두 쪽의 실체 (2026-07-27)

| | 이 저장소 | team-loop-lite-ai-learning |
| --- | --- | --- |
| 런타임 | **C# / .NET 8** (`net8.0`, RollForward Major) | **Node.js** (추적 266 파일 중 js 189) |
| 주 인터페이스 | CLI 하네스 + 대시보드(5173) | **MCP** + 작업보드 HTML 내보내기 |
| 판정 자산 | POST-COMMIT 22검사, 반증 픽스처, CI(리눅스·윈도우) | 자체 verification/judging 도구, task worktree |
| 상태 원본 | `docs/handoff/WORKSTATE.json` | 자체 `data/`·`workspaces/` |

**이미 한 방향 이식이 있었다.** team-loop의 `LOCAL-FIRST-IMPORTS.md`:

> 옛 C# 구현을 통째로 베끼지 않는다. 재사용할 것은 **운영 규칙·실패 분류·하네스 계약**이다.
> 런타임은 Node.js로 유지한다.

스킬 4개(`powershell-encoding`·`execution-verification`·`root-cause-diagnosis`·`path-escape-qa`)가
그쪽 `data/skills.json`에 실제로 반입됐다. **그 문서들은 지금 그쪽 `docs/archive/`에 있다.**

반대 방향 근거도 있다 — `ADR-015`는 team-loop 저장소에서 `codex exec`로 실제 리뷰를 반복 실행해
exit code를 실증했다고 기록한다.

## 2. 선택지

1. **한 저장소로 모으기(monorepo)** — 두 런타임 공존. 비용 낮고 얻는 것도 적다.
   상태 원본이 두 벌로 남아 이 저장소가 오늘만 세 번 고친 문제(정의 두 벌)를 저장소 규모로 반복한다.
2. **오케스트레이션은 team-loop, 판정층을 그쪽이 흡수** — 인터페이스만 정의하고 **상태 원본을 한 벌로.**
3. **한쪽 런타임으로 완전 흡수** — 어느 쪽을 버릴지가 전부. C# 게이트 자산이 크지만 team-loop이
   MCP·오케스트레이션의 본체라 방향이 갈린다.

## 3. 선택

**2번. 방향은 흡수 — team-loop이 받는 쪽이다.**

**무엇을 합칠지는 아직 정하지 않았다**(사용자 원문: *"합치는 대상은 몰라도"*). 그건 남은 결재다.

## 4. 판단 기준

**정확성 > 구현 비용.** 이 저장소의 반복 실패는 "같은 정의가 두 벌이라 한쪽만 낡는 것"이다.
2026-07-27 하루에만 세 번 고쳤다 — `RequiredGateCommands`(이름 교체 뒤 낡아 경로가 통째로 죽음),
`SelfTestGateCounts`(생산자·검사자가 같은 표를 읽어 대조가 공회전), 영토 목록(`CodexTerritory`로 통합).
**저장소를 합치되 상태 원본을 두 벌로 두면 같은 병을 더 큰 규모로 얻는다.** 그래서 1번을 배제했다.

## 5. 결과

### 5-1. 넘어가는 것 — 규칙·계약

C# 코드가 아니라 **규칙 형태로 남긴 것만 건너간다.** 2026-07-27에 확보한 것:

| 규칙 | 근거 |
| --- | --- |
| 면제를 **경로가 아니라 그 커밋의 반입**에 묶는다 | `docs/verification/terr-02.md` |
| **안 물린 픽스처는 시험이 아니다** — 기대값을 실측한 뒤 매니페스트에 등재한다 | `negative-case-count-measured.md` |
| **표를 실재에 못 박는다** — 선언과 실측을 구분한다 | `selftest-case-count-measured.md` |
| **exit code로 증명되지 않는 수정이 있다** — 그때는 수치를 본다 | `terr-02.md` §핵심 반증 |
| **런타임을 컨테이너로 격리해야 "어느 것이 돌았나"가 사실이 된다** | `portability-linux-2026-07-27.md` |
| **영구히 빨간 게이트는 무시된다** — 고치기 전에 켜지 마라 | FAIL-2026-010, `gates.yml` 주석 |

team-loop은 이미 `task/tsk_*` worktree·브랜치로 돌므로 **영토·반입 결속 규칙은 거의 그대로 얹힌다.**

### 5-2. 안 넘어가는 것 — C# 구현

`TerritoryCheckCli.cs`, `HarnessJson.cs`, `SelfTestCensus.cs`, `.NET 8 TypeInfoResolver` 수정,
`global.json`, `.github/workflows/gates.yml`의 .NET 매트릭스. **런타임이 달라 코드로는 안 건너간다.**

### 5-3. 이 저장소에서 앞으로

**새 C# 기능을 시작하기 전에 이 ADR을 확인한다.** 만들 것이 있으면 **코드가 아니라 규칙 형태로**
남겨야 건너간다. 진행 중이던 것 정리:

- `TERR-01`·`TERR-02` — 반입 완료. **규칙은 위 표에 옮겨 적었다.**
- `NET8-01`·`NET8-01-R1` — **환경 차단으로 진행 불가.** 코덱스 격리 사본에서 NuGet(`NU1301`)에
  닿지 못하고 docker named pipe도 막혀 **컴파일 증거를 만들 수 없다.** 지시서로 못 고친다.
  융합 후 Node 쪽에서는 이 문제 자체가 없다(.NET 8 전용 버그).

## 6. 되돌림 조건

- team-loop의 판정 자산이 이 저장소의 게이트 수준(반증 픽스처·기대 exit code·증거 보고)에
  **실측으로** 못 미치는 것이 확인되면 흡수 방향을 재검토한다.
- **되돌리는 법**: 이 ADR을 `폐기`로 바꾸고 사유를 적는다. 코드 변경은 아직 없으므로 되돌릴 것이 없다.

## 7. 관련 실패 사례

- **FAIL-2026-010** — 영구히 빨간 게이트는 무시된다. 융합 중 임시 적색을 만들지 않는 근거.
- **FAIL-2026-012** — 프록시로 원인을 단정하지 마라. 이 ADR의 §1은 **찾아본 결과 없었다**는
  실측이지, "기록이 없으니 결정이 없었다"는 추론이 아니다. 결정은 사용자 진술로 존재한다.
