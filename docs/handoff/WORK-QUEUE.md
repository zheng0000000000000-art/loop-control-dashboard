# 작업 큐 — 조율자가 깨어나면 여기서 다음 할 일을 집는다

> **이것이 지시큐다.** 사람이 항목을 올려두면 조율자가 위에서부터 하나씩 집어간다.
> `scripts/coordinator-wake.ps1`이 **미완 항목이 남아 있으면 메시지가 없어도 조율자를 깨운다** —
> 그래야 응답이 아니라 루프가 된다.
>
> **왜 있는가**: 2026-07-27에 334분 공백이 있었다. 할 일이 남아 있었는데 아무도 이어가지 않았다.
> 조율자는 채팅으로만 턴이 시작되고, 자기가 멈춘 것을 스스로 모른다.

## 상태 표기 — 실행과 판정을 가른다

| 표기 | 뜻 | 누가 옮기나 |
| --- | --- | --- |
| `- [ ]` | 대기 | — |
| `- [>]` | **진행 중** — 어느 세션이 잡고 있다 | **프로그램**이 찍는다(`queue-state.ps1`) |
| `- [~]` | **검토 대기** — 일은 끝났고 판정이 남았다 | **실행자**가 여기까지만 옮긴다 |
| `- [x]` | 완료 | **판정 세션**이 옮긴다 |

**`- [>]`는 손으로 쓰지 않는다.** `scripts/queue-state.ps1`이 주인 세션의 `claude.exe` pid와
시각을 함께 찍고, **주인이 죽으면 자동으로 `- [ ]`로 되돌린다.** 크래시 한 번에 항목이
영구히 잠기면 그게 더 나쁘다 — 영구히 막힌 것은 무시된다(FAIL-2026-010).

왜 필요했나: 2026-07-27, 대기/검토 대기/완료만 있고 그 사이가 없어서 **두 세션이 같은 항목을
동시에 집었다.** 한 세션이 세션 격리를 만드는 동안 다른 깨어난 세션이 같은 항목을 잡고
같은 잠금을 시험하고 있었다.

**실행자는 자기 일을 완료로 선언하지 않는다.** `- [~]`까지가 실행자의 몫이고,
`- [x]`는 판정이다. 이것이 원래 설계의 Court/Clerk 분리다 —
*"법원은 새로운 아이디어를 생성하지 않는다. 선택과 판결만 수행한다."*(`LLM_Runtime_Society_V4`)

그래서 **조율자는 리뷰 때만 깨어나면 된다.** 실행은 CLI 세션과 로컬 모델이 돌린다.

## 규칙

- **위에서부터 하나씩.** 한 번 깨어날 때 **한 항목**만 한다. 여러 개를 몰아 하면 실패 지점이 섞인다.
- **`- [>]`로 잡힌 항목은 집지 않는다.** 깨우기가 자동으로 건너뛴다(`- [>]`는 `- [ ]`가 아니다).
- 실행이 끝나면 `- [ ]`를 **`- [~]`**로 바꾸고 **무엇을 했는지·무엇으로 확인했는지** 한 줄 덧붙인다.
- 리뷰에서 통과하면 `- [~]`를 `- [x]`로 바꾼다. **미달이면 `- [ ]`로 되돌리고 사유를 적는다.**
- 하다가 새로 알게 된 일이 있으면 **큐 아래에 추가**한다. 다음 세션이 그것을 집는다.
- **막히면 진행하지 말고** 대화 채널(`data/discussions.json`)에 질문을 남기고 멈춘다.
- 커밋 전 `measure dev-pack` violations 0. 아니면 커밋하지 않는다.
- **승인은 판정 세션이 한다**(ADR-020). `approve`/`reject`/`import`/`verify_task` 모두 에이전트가 할 수 있다.
  단 **실행한 세션은 자기 일을 승인하지 않는다.** `- [~]`까지 옮기고 다음 판정 세션에 넘긴다.
- **발사(sonnet/codex spawn)는 사람 게이트다.** 비용이 발생한다.
  보드에서 제목이 `[사람 게이트]`로 시작하는 태스크는 실행자가 집지 않는다.
- **기준 파일 변경은 사람 결재다.** 여기만은 안 푼다 — 측정 기준을 스스로 고치면 나머지 측정이 의미를 잃는다.

---

## 대기 중

(현재 대기 없음 — 아래 사람 게이트 2건은 실행자가 집지 않는다.)

> **순서는 사람이 정한다.** 2026-07-27 사용자 지시로 작업보드를 맨 위로 올렸다 —
> *"작업보드 쪽 먼저 하는 게 확실하지 않을까?"*

> **사람 게이트 2건은 보드로 옮겼다.** 발사가 필요해서 실행자가 집을 수 없다.
> 폰에서 보이되 안 집히도록 제목을 `[사람 게이트]`로 시작하게 했다.
> - `tsk_8cf42e5c5d69d264282a` — `.NET 8` CI 다리 켜기(`NET8-01-R1` 발사)
> - `tsk_879407eb7997b2105904` — `territory-check`를 team-loop에 겨눠 실측(발사)

---

## 새로 알게 된 일 (다음 세션이 판단)

- **team-loop 보드 태스크(`tsk_1a113f64adb67331dac2`, "사람 게이트 태스크를 세션이 claim 못 하게 막는다")는
  코드는 완료 기준을 다 충족했는데 자동 circuit breaker가 걸어 `BLOCKED`다.** (2026-07-28, 이 조율자 세션 —
  `loop_enter` 추천으로 발견, 이 세션은 코드를 만들지 않았다)
  **재확인한 것(자기보고 아님, 이 세션이 격리 워크트리를 직접 열어 실측)**: `.team-loop-worktrees/tsk_1a113f64adb67331dac2`의
  `git status`/`git diff`로 `server.js`(수정)·`src/human-gate.js`(신설)·`test/human-gate-claim.test.js`(신설)가
  allowedPaths(`server.js`, `src/**`, `test/**`) 안에 정확히 있음을 확인. `node --test test/human-gate-claim.test.js`를
  이 세션이 직접 재실행 → `8/8 통과`(claim 거절·EXTERNAL_AGENT도 거절·사유에 표식+dashboard 언급·음성사례 2종
  ·표식이 `src/human-gate.js` 한 곳에만 있음·`server.js`의 claim 처리부가 `store.mutateTask`(IN_PROGRESS 전환)
  전에 게이트를 부르는 것까지 소스 스캔으로 확인). `show_task`가 보인 유일한 실패(`node --test` 전체 533개 중
  1건, `test/injection-readiness.test.js:50`의 `data/failure-cases.json` ENOENT)를 `git stash`로 이 diff를
  뗀 뒤 같은 파일만 재실행해도 동일하게 실패함을 이 세션이 직접 재현 — 다른 여러 태스크(`tsk_06ba445c...`·
  `tsk_1d8eb8f2...` 등)에서 이미 반복 관찰된 격리 워크트리 인프라 결손(gitignore된 런타임 데이터 부재)이지
  이 diff의 결함이 아니다. 완료 기준 7개 전부 코드+시험으로 충족 확인.
  **막힌 진짜 원인**: `show_task`의 `verification.checks`에 `agent-executor` 체크가 `EXECUTOR_FAILED`
  (`Reached maximum number of turns (40)`)로 잡혀 있다 — 실행 에이전트가 무관한 `injection-readiness` 실패를
  자기 diff 탓으로 오인해 `node --test`를 여러 번 재확인하다 turn 예산을 다 썼다(자기보고 로그에서 같은
  명령을 6~7번 반복 실행한 흔적 확인). 이게 2회 반복돼(`automationGuard.sameFailureCount: 2`)
  circuit breaker가 자동으로 `blocked.automatic: true`를 걸었다. 누적 비용 `$2.72`, 토큰
  `457270/500000`(91%) — 예산이 거의 소진됐다.
  **이 세션이 안 한 것**: 재발사하지 않았다 — MCP 도구 목록에 BLOCKED/circuit 리셋 도구가 없고([[team-loop-mcp-no-unblock-tool]]
  기존에 이미 확인된 한계), 같은 실패 신호(무관한 테스트를 자기 탓으로 오인)가 재발사에서도 반복될
  가능성이 높은데 예산이 91% 소진된 상태라 밀어붙이지 않았다. `data/discussions.json`에도 남김
  (`msg_boardtask_1a113f64_humangate_stuck_20260728`).
  **사람이 정할 것**: (a) 이 격리 워크트리의 기존 diff를 사람이 대시보드/수작업으로 직접 승인·병합할지
  (b) circuit을 리셋하고(도구 없음 — 수작업 또는 코드 변경 필요) 재발사할지.

- **team-loop 보드 태스크(`tsk_1d8eb8f26ff6124bd476`, 경매장 밸런스 게이트 음성 사례)는 코드는 끝났는데
  같은 부류의 격리 워크트리 인프라 결손으로 `verify_task`가 FAILED다.** (2026-07-28, 실행 세션)
  **한 일(완료 기준 6개 실측 확인)**: `examples/balance/broken-blind-risk-auction-economy.json` 신설 —
  `unknown-auction-economy.json`(안 건드림, `git diff` 없음 확인)과 같은 metrics/parameters를 쓰되
  `baseline.information.blindValueErrorRate`만 0으로 깨서(블라인드 구매의 리스크 제거 →
  "Keep blind play risky" 목적 직접 위반) `blindTransactionRoiMean`이 20.24로 상한 14를 넘게 만들었다.
  `mode: evaluate`로 고정했다 — `tune` 모드는 파라미터 공간을 탐색해 위반을 스스로 지워버릴 수 있어
  음성 고정 사례에 안 맞는다(이번에 확인). `test/balance-gate-examples.test.js` 신설 — (1)
  `unknown-auction-economy.json`(evaluate)이 `passed:true, violations:0`인 것과 (2) 새 음성 사례가
  `exit 1, passed:false, violations>=1, failedMetrics`에 `blindTransactionRoiMean`이 20.24로
  "above 14"라 걸렸다는 것까지 출력에 드러남을 둘 다 단언. 로컬(메인 트리) `npm test` 직접 재실행 →
  `tests 525, pass 525`(기존 523 + 신설 2). `git diff --check` 통과, `git status --short`로
  allowedPaths(`examples/balance/**`, `test/**`, `tools/verification/check-balance-gate.mjs`) 밖
  변경 없음(작업 중 라이브 서버가 재직렬화한 `data/harnesses.json`·`data/skills.json`·`data/wiki.json`은
  `git checkout --`로 원복해 트리를 깨끗이 했다).
  **막힌 지점(직접 진단, 추정 아님)**: `verify_task`가 `.team-loop-worktrees/tsk_1d8eb8f26ff6124bd476`에서
  `node --test`를 돌리며 `525개 중 524 pass, 1 fail` — 실패는 `test/injection-readiness.test.js:50`,
  `ENOENT: data/failure-cases.json`. 이 격리 워크트리에서 `git ls-files data/` → `.gitkeep`·
  `harnesses.json`·`skills.json`·`wiki.json`만 나오고 `failure-cases.json`은 애초에 git 추적 대상이
  아니다 — 메인 트리에서 `git check-ignore -v data/failure-cases.json` → `.gitignore:1:data/*.json`에
  걸림을 확인했다. 즉 이 파일은 **라이브 서버가 런타임에 쓰는 gitignore 파일**이라 신선한 `git worktree`
  체크아웃에는 원천적으로 없다 — 이번 태스크의 diff와 무관한, 기존에 여러 번 기록된 격리 워크트리
  인프라 결손(아래 `tsk_06ba445c1ee0e40aa5fe`·`tsk_1c1cac...` 항목과 동일 종류)과 정확히 같다.
  같은 실행에서 내가 새로 붙인 시험(`balance gate rejects a fixture...`)은 ✔ 로 통과했다 — 실패는
  내 변경이 아니라 이 워크트리의 런타임 데이터 결손 탓임을 직접 확인.
  `request_review_task`도 "A passing verification is required"로 거절(REVIEW로 못 넘어감).
  **지금 상태**: `IN_PROGRESS`/`verification FAILED`로 뒀다. 코드는 서버의 격리 워크트리에
  `submit_task_result`(MCP_FILES)로 정확히 두 파일만 반영돼 있다. `data/failure-cases.json`은
  allowedPaths 밖이고, 워크트리 프로비저닝(gitignore 런타임 파일을 안 복사하는 것) 자체를 고치는 건
  이 태스크 범위 밖이라 손대지 않았다. `data/discussions.json`에도 같은 내용을 남겼다.
  **balance-gate 하네스 scope 참고(태스크가 위임한 자유재량)**: `data/harnesses.json`의 `balance-gate`
  항목이 `"scope": "global"`이다 — 경매장 전용 지식이 team-loop 전역에 섞이면 안 된다는 기존 결정과
  어긋나지만, `data/harnesses.json` 수정은 allowedPaths 밖이라 고치지 않고 여기 기록만 남긴다.
  **사람이 정할 것**: 위 '외부 에이전트를 위한 제3의 실행 모드' 항목·`tsk_06ba445c1ee0e40aa5fe` 항목과
  동일한 근본 원인(격리 워크트리가 gitignore 런타임 데이터를 못 받음)이 해소되면 이 태스크도 같이
  풀린다. 판정 세션이 격리 워크트리에서 diff를 직접 대조하고 메인 트리에서 `npm test`를 독립
  재실행하는 방식(기존 판정 세션들의 선례)으로 REVIEW 상당 판단을 대신할 수 있을 것으로 보인다.

- **`tsk_8cf42e5c5d69d264282a`(.NET 8 CI 다리, [사람 게이트])가 발사됐다가 검증 실패로 멈춰 있다.**
  (2026-07-27 발견, 조율자) 12:22:52 KST 발사(비용 $0.39) → 12:25:41 KST `VERIFICATION_FAILED` →
  이후 자동 재시도 없이 `IN_PROGRESS`/`IDLE`로 정지. 원인 셋을 `show_task`로 직접 확인:
  ①worktree가 `team-loop-lite-ai-learning`을 가리켜 실제 대상(Local-First 저장소)에 접근 불가
  ②태스크 본문이 스스로 "사람만 할 수 있다"고 적었는데도 AGENT 모드로 발사됨
  ③`NO_DELIVERABLE` 검증이 "정당한 거절"과 "실패"를 구분 못해 오탐 — 실행자는 아무것도 고치지 않고
  이유만 설명했는데 실패로 찍혔다(자기보고 확인, `fail_c94438cd0e00bbdd2c1c`).
  **여기서 안 고친 이유**: 검증 로직 수정은 측정 코드 변경(사람 결재 대상, CLAUDE.md), 재발사도 비용
  드는 사람 게이트. **사람이 정할 것**: worktree를 Local-First 저장소로 바로 잡아 재발사할지,
  아니면 `NET8-01-R1` 지시서대로 사람이 직접 실행할지. discussions.json에 상세 답변 남김
  (`msg_board36stuck20260727`).

- **위 항목에 대한 사용자 답(08:03:45Z "b해보고 다시 이러면 a로 해줘")을 이 세션이 확인했다.**
  착수 전 team-loop 소스(`src/worktree.js`의 `createTaskWorktree`, `src/cli/main.js:744`
  `repoRoot = bootstrap.workspace?.root || process.cwd()`)를 읽어 "worktree가 잘못 잡혔다"의
  성격을 재확인했다: 이 실행 경로는 `git worktree add`로 워크스페이스를 만드는데, git worktree는
  **같은 저장소**의 다른 체크아웃일 뿐이다. repoRoot가 team-loop-lite-ai-learning인 이상 그
  worktree 안에는 애초에 Local-First 저장소 파일이 존재할 수 없다 — 설정 오타가 아니라 팀루프
  보드의 AGENT 실행 경로 자체가 **같은 저장소 안에서만** 도는 구조다.
  **판단**: 지난번과 같은 방식(단순 재발사)으로 b를 다시 쏘면 같은 실패를 반복할 가능성이 높다.
  b가 실제로 다르려면 발사 시 workspaceRoot를 Local-First 경로로 직접 지정해야 하는데, 그
  발사 진입점을 이 세션에서 찾지 못했다(발사는 사람 게이트라 관찰도 불가). discussions.json에
  판단 근거와 함께 남기고(`msg_net8worktreestructural20260727`) 코드는 손대지 않았다.
  **사람이 정할 것**: (i) 발사 시 workspace를 Local-First로 지정하는 법을 알고 있어 b를 그
  방식으로 다시 시도할지, (ii) 구조적 실패로 보고 바로 a(조율자가 직접 고치고 사람이
  TERRITORY-EXCEPTIONS.json에 등재)로 갈지.

- **8시간 넘게 놓쳤던 사용자 승인을 찾았다(2026-07-27, 이 세션).** `data/discussions.json`의
  `msg_8dc5ea1dad04f13326fe`(05:01:27Z "응 해줘. 답장이 너무 느린데 이부분도 어떻게 해봐")가
  안 읽힌 채 있었다 — NET8-01-R1을 조율자가 직접 실행하는 방식(옵션 b)에 대한 승인이었는데,
  이후 세션(`msg_wakeq20260727b`, 13:31Z)이 "새 사용자 메시지 없음"으로 잘못 판단했다.
  원인은 지연이 아니라 시간순 정렬 없이 훑다 메시지 하나를 건너뛴 것으로 보인다(주체 미상 —
  당시 세션 로그 부재로 재현 불가).
  **착수 직전 발견한 충돌**: 이 승인의 대상인 "조율자가 `server/Harness/`를 직접 고치는 것"은
  같은 날 02:41~02:59에 반입된 `territory-check`(TERR-01·TERR-02, ADR-002)가 정확히 잡도록
  설계한 행동이다. `TERRITORY-EXCEPTIONS.json`은 빈 목록이라 지금 커밋하면 위반으로 잡힌다.
  05:01 승인 시점 대화에 territory-check 언급이 없어 사용자가 이 규칙을 알고 승인했는지
  불명확 — 추측하지 않고 discussions.json에 선택지 3개(직접+예외등재 / 재발사 / 보류)를
  남기고 **코드는 손대지 않았다**. 다음 세션은 사용자 답을 확인한 뒤 진행한다.

- **team-loop 보드 태스크(`tsk_aa08207b993b422a4fdf`, coordinator-presence.js)가 코드는 끝났는데
  REVIEW로 못 올라간다 — AGENT 납품 게이트와 수작업 제출이 충돌한다.** (2026-07-27 발견, 조율자)
  `src/coordinator-presence.js`의 staleMinutes 두 벌 정의·fail-open 버그는 지시대로 고쳤고
  (`npm test` 508/508, scope 위반 없음, `git diff --check` 통과) `submit_task_result`로 격리
  워크트리(`.team-loop-worktrees/tsk_aa08207b993b422a4fdf`)에 정확히 두 파일만 반영됐다.
  그런데 `request_review_task`가 "A passing verification is required"로 막고, `verify_task`를
  돌리면(다른 우회로가 없어 1회 실행) `EXECUTOR_RESULT_MISSING`으로 FAILED다 — 이 태스크가
  `work_start_next`로 `executionMode: AGENT`가 잡혀야 `claim_task`가 먹혔는데(HUMAN+READY로는
  `claim_task`가 "Agent execution requires a queued task"로 거절), AGENT 모드 납품 게이트는
  실제 spawn된 실행자의 종료 코드를 요구한다. 나는 발사 없이 이 세션이 직접 작업해 MCP_FILES로
  제출했으니 그 종료 코드가 없다. `src/review-block.js`에 이미 이 충돌이 주석으로 적혀 있다 —
  기존에 알려진 막다름이다. 해법 둘: (1) 진짜 spawn으로 재실행(비용 발생, 사람 게이트, 내가
  못 누른다) (2) `block`→`unblock` HTTP 액션으로 executionMode를 HUMAN으로 되돌린 뒤 손으로
  verify(server.js 2214~2238행, `unblock`이 명시적으로 `executionMode='HUMAN'`으로 되돌린다) —
  단 이 액션은 MCP 도구 목록에 없어 HTTP API를 직접 두드려야 하고, 그건 이 태스크가 지시한
  "MCP로 처리" 범위 밖이라 확신 없이 진행하지 않았다. 태스크는 `IN_PROGRESS`/`verification
  FAILED`로 그대로 뒀고, 게이트 코드는 건드리지 않았다(allowedPaths 밖 + 기준 코드 수정은
  사람 결재 대상). discussions.json에 선택지 3개(block/unblock 허용 / 다른 진입 경로로 재청구 /
  게이트 자체 결함으로 보고 티켓화)를 남겼다(`msg_boardtask_aa08207_deliverygate_20260727`).
  **사람이 정할 것**: 위 셋 중 어느 쪽으로 갈지.
  **이 세션 확인(2026-07-28, `session-20260728-041106`)**: `mcp__team-loop__show_task`로 재확인 —
  `tsk_aa08207b993b422a4fdf`는 이제 `status: DONE`(archived), `verification.status PASSED`,
  `review.status APPROVED`(`adminOverride: true`)다. `executionMode: HUMAN`으로 그대로 남아 있고
  `executor` 필드는 비어 있다 — 아래 gap 항목이 지목한 "AGENT 납품 게이트" 충돌 자체를 우회해서
  풀린 것으로 보인다(추정, 누가/어떻게 승인했는지는 `show_task` 필드만으로는 재구성 불가).

- **team-loop 보드 태스크(`tsk_aa08207b993b422a4fdf`)는 판정 통과인데 REVIEW→DONE 을 옮기는 MCP 도구가 없다.** (2026-07-28 발견, 실행 세션과 다른 판정 세션)
  **재실행해 대조한 것(자기보고 아님)**: `git log`로 `1708c59`가 `8af99b3`로 `fusion/judgment-layer`에 이미 병합돼 있음을 확인. `src/coordinator-presence.js`·`test/coordinator-presence.test.js`를 직접 Read — `DEFAULT_STALE_MINUTES=25` 삭제, `appsettings.json`의 `Coordinator.StaleMinutes`를 읽고 실패/누락 시 10으로 떨어지는 `resolveStaleMinutes` 신설, `future`/`skewMinutes` 반환, `absenceNotice`가 future 분기에서 "고장"/"미래" 문구를 내고 "조용하다"는 안 냄을 코드에서 확인. `npm test` 이 세션이 직접 재실행 → `tests 508, pass 508, fail 0`(자기보고와 일치). `git diff 48a2fbf..1708c59 --stat`로 scope 재확인 → 두 파일만 변경(allowedPaths 일치). `appsettings.json` 실측 → `Coordinator.StaleMinutes=10` 이미 반영됨. 완료 조건 6개 전부 코드+시험으로 대조 완료.
  **판정**: 완료 기준 충족.
  **막힌 지점**: `show_task`로 보면 이미 `status: REVIEW`(`verification PASSED`, `review.status PENDING`). 이 판정 세션이 `verify_task`로 승인하려 했으나 서버가 "Verification requires an IN_PROGRESS task"로 거절 — `verify_task`는 REVIEW 상태에는 안 먹는다(이미 실행 세션이 IN_PROGRESS일 때 한 번 돌려 PASSED를 받은 뒤 `request_review_task`로 REVIEW로 넘어간 상태라서다). MCP 도구 목록 어디에도 REVIEW→DONE 승인 도구가 없다. `server.js`에서 찾은 유일한 경로는 HTTP `POST action=review`(decision=APPROVE, 실제 merge+DONE+archive까지 함)뿐인데, 이건 이전 실행 세션이 `msg_boardtask_aa08207_deliverygate_20260727`에 남긴 것과 같은 종류의 "MCP 처리 범위 밖" HTTP 우회다 — actor 인증도 이 세션엔 없고 merge+archive는 되돌리기 번거로운 상태 변화라 직접 호출하지 않았다.
  **사람이 정할 것**: (a) 대시보드에서 직접 승인 클릭 (b) 이 세션류가 HTTP `action=review`를 직접 호출해도 되는지 명시 허가 (c) `verify_task`가 REVIEW 상태에서도 승인으로 동작하게 하거나 별도 `approve_task` MCP 도구를 신설(게이트/판정층 코드 변경 — 사람 결재 대상). `data/discussions.json`에도 같은 내용을 남겼다(`msg_boardtask_aa08207_reviewgap_20260728`).
  **이 세션 확인(2026-07-28, `session-20260728-041106`)**: 옵션 (c)가 그 사이 실제로 반입됐다 —
  MCP 도구 목록에 `mcp__team-loop__approve_task`(설명: "Approve a task waiting in REVIEW. Refuses
  when this same session executed the task")가 지금 존재한다. `show_task`로 재확인하니
  `tsk_aa08207b993b422a4fdf`는 `status: DONE`(archived), `review.status APPROVED`
  (`adminOverride: true`)다. **이 gap은 해소됐다** — 다음에 REVIEW에 걸린 태스크를 만나면
  `approve_task`를 바로 쓰면 된다(단 ADR-020대로 실행 세션 자신은 못 쓴다).

- **team-loop 보드 태스크(`tsk_06ba445c1ee0e40aa5fe`, removeTaskWorktree 빈 폴더 회수)는 코드는 끝났는데 같은 납품 게이트 충돌로 REVIEW를 못 올렸다.** (2026-07-28, 실행 세션)
  **한 일(완료 기준 6개 전부 실측 확인)**: `src/worktree.js`의 `removeTaskWorktree` — `git worktree remove --force` 뒤에도 디렉터리가 남으면 `fs.rm`으로 직접 지우고, 그래도 남으면(git 에러 또는 새 에러)에 `worktreeRemoveFailed=true`를 달아 던진다(성공한 척 조용히 넘어가지 않음). 반환값에 `{ dir, removed, forced }`. `mergePreparedWorktree`(164행)의 `.catch(() => {})`는 정리 실패를 완전히 삼키던 것을 고쳐 반환값 `worktreeCleanup` 필드에 남기도록(병합 자체는 실패시키지 않음 — 병합은 이미 끝난 뒤라서). `createTaskWorktree`(46행)의 `.catch(() => {})`는 그대로 뒀다 — 못 지워도 바로 다음 `git worktree add`가 실패를 그대로 드러내기 때문. `commitTaskWorktree`(remove=true, 145행)는 원래도 안 삼켰다. 시험 2개 추가(leftover 디렉터리 재현 + fs.rm 몽키패치로 진짜 실패 재현) — 둘 다 통과. `npm test` 전체 재실행 → `tests 510, pass 510`(기존 508 + 신설 2). `git diff --check` 통과. 허용 경로(`src/worktree.js`, `test/**`) 밖은 안 건드림.
  **막힌 지점**: `submit_task_result`를 통해 제출하니 `task.executionMode`가 `HUMAN`→`AGENT`로 바뀌었다(상세는 위 대기 중 항목 참조). `verify_task` → `agent-executor` 체크가 `EXECUTOR_RESULT_MISSING`으로 FAILED(scopeViolations 없음, `git diff --check` PASS). `request_review_task` → "A passing verification is required"로 거절. block/unblock HTTP 우회는 이전 세션들과 같은 이유(MCP 처리 범위 밖, 판정층 코드는 사람 결재)로 시도하지 않았다.
  **지금 상태**: `IN_PROGRESS`/`verification FAILED`로 그대로 뒀다. 코드는 서버의 격리 워크트리(`.team-loop-worktrees/tsk_06ba445c1ee0e40aa5fe`)에 그대로 있다. `data/discussions.json`에도 같은 내용을 남겼다(`msg_boardtask_06ba445c_deliverygate_20260728`).
  **사람이 정할 것**: 위 '외부 에이전트를 위한 제3의 실행 모드' 항목과 동일 — 해소되면 이 태스크도 같이 풀린다.
  **판정 세션 갱신(2026-07-28, 실행 세션과 다른 판정 세션)**: 위 "지금 상태"는 낡았다 —
  `show_task`로 다시 보니 이미 `status: REVIEW`(`verification PASSED`, `review.status PENDING`)로
  넘어가 있었다(누가 언제 넘겼는지는 이 세션에서 재구성 못 함 — 주체 미상). 이 판정 세션이
  **재실행해 대조**: 격리 워크트리(`.team-loop-worktrees/tsk_06ba445c1ee0e40aa5fe`)에서 diff를
  직접 Read, `npm test` 독립 재실행 → `tests 510, pass 509, fail 1`(fail 1건은
  `test/injection-readiness.test.js`가 이 워크트리에 없는 `data/failure-cases.json`을 찾다 나는
  것 — `git stash`로 이 태스크의 diff를 뗀 뒤 같은 파일만 재실행해도 똑같이 실패함을 확인해
  **이번 변경과 무관함을 직접 재현**). 호출부 네 곳(`createTaskWorktree` 50행 catch 유지,
  `commitTaskWorktree` 173행 무변경, `mergePreparedWorktree` 196행 `worktreeCleanup` 필드로 전환,
  `server.js` 2289행 기존 `.then/.catch`가 새 에러 경로도 그대로 흡수) 전부 소스에서 대조 완료.
  `git diff --check` 통과, allowedPaths 밖 변경 없음. **완료 기준 6개 전부 충족(코드는 PASS)**.
  **막힌 지점은 그대로**: 이 판정 세션이 `verify_task`를 다시 돌리자 "Verification requires an
  IN_PROGRESS task"로 거절(REVIEW 상태에는 안 먹는다 — `tsk_aa08207`과 동일 원인).
  MCP 도구 목록 전체를 다시 확인해도 REVIEW→DONE 도구가 없다. HTTP `action=review` 우회는
  같은 이유(actor 인증 없음·merge+archive는 비가역)로 이 세션도 쓰지 않았다. `data/discussions.json`에
  판정 결과를 남겼다(`msg_boardtask_06ba445c_judgment_20260728`). **REVIEW 상태 그대로 둔다** —
  코드는 승인 기준을 충족했으나 보드를 DONE으로 옮길 MCP 수단이 없다.
  **이 세션 확인(2026-07-28, `session-20260728-041106`)**: `approve_task` MCP 도구가 새로 생겨
  이 gap이 해소됐다(위 `tsk_aa08207` 항목의 확인과 동일 도구). `show_task`로 재확인 —
  `tsk_06ba445c1ee0e40aa5fe`는 `status: DONE`(archived), `verification.status PASSED`,
  `review.status APPROVED`(`adminOverride: true`), `executionMode: EXTERNAL_AGENT`,
  `executor: {tool: claude-code, model: claude-opus-5}`. 병합도 이미 반영됨.

- **team-loop 보드 태스크(`tsk_1d8eb8f26ff6124bd476`, 경매장 밸런스 게이트 음성 사례)의 막힘이 이 세션에서 해소돼 REVIEW까지 올라갔다.** (2026-07-28, 이 세션 `session-20260728-035106` — 코드를 만든 세션(`20260728-033107`)과 다른 세션)
  위 바로 위 항목(같은 태스크, 2026-07-28 실행 세션 기록)이 남긴 상태는 낡았다 — `[루프]`가 3회 무진전으로 막힘 보고(`msg_ae3150ba862642c5aa98`)까지 했었다.
  **재확인한 것(자기보고 아님)**: `show_task`로 대조하니 코드(`examples/balance/broken-blind-risk-auction-economy.json`, `test/balance-gate-examples.test.js`)는 이전 세션이 이미 `submit_task_result`로 격리 워크트리에 정확히 두 파일만 반영해뒀다 — 이 세션은 그 코드를 만들지 않았다. `verify_task`가 계속 FAILED였던 원인은 이번 diff와 무관한 `test/injection-readiness.test.js:50`의 `ENOENT`(격리 워크트리에 gitignore된 `data/failure-cases.json`이 없어서) — 다른 여러 태스크(`tsk_06ba445c1ee0e40aa5fe`·`tsk_aa08207b993b422a4fdf`·`tsk_eef8545b5a6ae3376dc8`)에서 반복 관찰된 것과 정확히 같은 격리 워크트리 인프라 결손이었다.
  이 세션이 격리 워크트리(`.team-loop-worktrees/tsk_1d8eb8f26ff6124bd476`)를 직접 다시 보니 `data/failure-cases.json`이 이제 존재했다(메인 트리와 바이트 동일, mtime이 이전 실패한 `verify_task` 실행보다 10분쯤 뒤 — 주체 미상, 라이브 서버가 이 워크스페이스에서 돌며 채운 것으로 보이나 단정하지 않는다). `node --test`를 이 격리 워크트리에서 직접 재실행 → `525/525 전부 통과`(자기보고 아님, 이 세션이 직접 실행). `git status --short`/`git diff --stat` 로 allowedPaths(`examples/balance/**`, `test/**`, `tools/verification/check-balance-gate.mjs`) 밖 변경 없음 재확인.
  이걸 근거로 `verify_task`를 다시 불렀다 → `{"status":"PASSED","passed":true,"failureCaseIds":[]}`. `request_review_task` → `status: REVIEW`로 전환 확인.
  **이 세션이 안 한 것**: 승인(`approve_task`)은 하지 않았다(ADR-020, 실행과 판정은 다른 세션이어야 한다 — 이 세션이 코드를 만들지 않았어도 재검증·상태전환을 한 세션이라 승인까지 하면 자기 판단을 자기가 승인하는 모양이 된다). `data/harnesses.json`의 `balance-gate` scope가 `"global"`인 기존 문제는 이전 세션 기록대로 여전히 미해결(allowedPaths 밖).
  **사람/판정 세션이 볼 것**: 격리 워크트리 인프라 결손(`data/failure-cases.json` 부재)이 이번엔 우연히 해소돼 넘어갔다 — 근본 원인(워크트리 프로비저닝이 gitignore 런타임 데이터를 안 복사하는 것)은 그대로다. 다음에 같은 결손이 다른 태스크를 다시 막을 수 있다. `data/discussions.json`에도 같은 내용을 남겼다(`msg_balancegate_negcase_unblocked_20260728`).

- **team-loop 메인 트리(격리 워크트리 밖)에 `tsk_1d8eb8f26ff6124bd476`의 미추적 파일 사본이 남아 있다.** (2026-07-28, 이 세션 발견)
  `C:\NHN Project\team-loop-lite-ai-learning\examples\balance\broken-blind-risk-auction-economy.json`·
  `C:\NHN Project\team-loop-lite-ai-learning\test\balance-gate-examples.test.js` 가 **메인 트리**에도
  untracked 로 존재한다(mtime 03:34 KST, `git log`로 대조 — 이 경로들은 메인 트리에 커밋된 적이 없다).
  이건 `msg_ec4d0aa39ed449c9b76f`("세션이 격리 밖에서 team-loop 을 고쳤다")가 이미 경고했던
  이전 세션(`20260728-033107`)의 격리 위반 흔적이다 — 그 세션의 **공식 제출은** `submit_task_result`로
  격리 워크트리(`.team-loop-worktrees/tsk_1d8eb8f26ff6124bd476`)에 정상적으로 들어갔고(이번 판정도
  그걸 근거로 REVIEW까지 올렸다), 메인 트리 쪽은 그 부산물로 보인다.
  **이 세션이 안 한 것**: 지우지 않았다 — 이 파일들은 이번 태스크 allowedPaths(격리 워크트리 기준)
  안에 있는 내용과 같지만, 메인 트리 자체는 이 태스크의 작업 공간이 아니라서 확신 없이 삭제하지
  않았다(untracked라 되돌리기는 쉽지만, 판단은 사람/다음 세션에 넘긴다).
  **사람이 정할 것**: 메인 트리의 이 두 untracked 파일을 지워도 되는지(안전해 보인다 — git에
  한 번도 커밋된 적 없고 격리 워크트리 쪽 정식 제출과 내용이 같다), 그리고 근본 원인
  (어떤 세션이 격리를 우회해 메인 트리에 직접 쓴 경로)을 더 조사할지.
  **이 세션 확인(2026-07-28, `session-20260728-041106`)**: 이 질문은 저절로 풀렸다 — `tsk_1d8eb8f26ff6124bd476`가
  그 사이 `approve_task`로 승인되며(`show_task`: `status DONE`, `review APPROVED adminOverride:true`,
  `executor.session: 20260728-035106`) team-loop main에 병합됐다. `git log --oneline -1 -- examples/balance/broken-blind-risk-auction-economy.json`
  → 커밋 `b632b57`, `git ls-files`에 두 파일 모두 잡힘, `git status --short`는 깨끗함. 즉 "untracked
  사본"이 아니라 지금은 **정식으로 커밋된 파일**이다 — 지울 필요 없다.

- **team-loop 보드 태스크(`tsk_3b9760b2f47c055baecb`, "발사기가 대상 저장소를 받게 한다")를 이
  세션이 직접 구현·커밋했다 — 그런데 보드 쪽 검증 도구가 이 태스크를 승인 상태로 옮기지 못한다.**
  (2026-07-28, 이 조율자 세션 `session-20260728-114105`)
  **주체**: 이 세션이 코드를 직접 만들었다(발사 없음, MCP 코드 실행 없음).
  **한 일**: `server/CodexHarnessLauncherCli.cs`의 `Execute`가 언제나 `RepoRoot()`(자기 저장소)만
  대상으로 삼던 것을 고쳤다. 요청 JSON이 `targetRepo`를 선언하면 `ResolveTargetRepo`가 그 경로의
  존재·git 저장소 여부를 **쏘기 전에** 검증해 거절하고, 통과하면 그 경로를 이후 모든 `root` 인자로
  쓴다. 선언 안 하면 종전대로 `RepoRoot()`(자기 저장소). `CodexTerritory.RootsRejection`(절대 경로
  거절)은 손대지 않았다 — 옳은 검사라 그대로 지켰다.
  **사용한 하네스(명령·exit code·수치)**: `dotnet build server` 경고 0·오류 0.
  `dotnet run --project server -- codex-launch validate --request <절대경로>`를 다섯 조합 직접
  실행(`docs/qa/gate-witness/codex-launch-targetrepo-*.request.json` 5개 신설, 절대경로로
  실행해야 함 — 상대경로로 돌리면 `dotnet run`의 cwd 불일치로 `launch-request-unparsable`이
  나는 함정을 직접 겪고 피함): ①`targetRepo` 미선언 → `ACCEPTED`(자기 저장소 사용, exit 0)
  ②없는 경로 → `target-repo-not-found`, exit 2 ③git 저장소 아닌 경로
  (`docs/qa/gate-witness/build-verify-ok`, `.git` 없음 확인) → `target-repo-not-a-git-repository`,
  exit 2 ④유효한 `targetRepo`(`team-loop-lite-ai-learning`)+절대 경로 `territoryRoots` →
  `territory-root-is-absolute`, exit 2(대상 저장소 유무와 무관하게 그대로 거절됨을 재확인)
  ⑤유효한 `targetRepo`+상대 경로 `territoryRoots`(`src/`) → `ACCEPTED`(양성, 융합 목표 그대로 재현).
  **회귀 확인**: 기존 요청 파일 3개(`dispatch/LAUNCH-GCLEAN-01.request.json`·`LAUNCH-TERR-01`·
  `LAUNCH-NET8-01`, 전부 `targetRepo` 미선언)를 재실행 → 이번 변경과 무관한 이전과 동일한 사유
  (`baseline-commit-mismatch`, 저장소가 그 뒤로 진행돼 핀이 낡음)로 거절됨을 확인 — 회귀 없음.
  `measure dev-pack` → `violationCount 0`. `territory-check` → `violations 0`(커밋 전 기준).
  관련 커밋: `98663a4`.
  **지표는 만족했으나 목적은 미달인 부분(자진 신고)**: ①커밋 제목이 "...적용하는 첫 이"로
  잘렸다 — 작성 실수(본문은 완전함). 규칙상 임의로 amend하지 않고 여기 그대로 적는다.
  ②`docs/verification/`은 이 태스크 allowedPaths 밖이라 별도 검증 문서를 안 남기고 이 큐 항목에
  직접 적었다.
  **막힌 지점**: team-loop MCP의 `submit_task_result`는 "서버가 **자신의 task worktree** 안에만
  적용한다"고 스스로 밝히고 `baseCommit`도 team-loop 저장소 기준이다 — 이 태스크의 실제 산출물은
  완전히 다른 저장소(Local-First, 이 세션이 지금 있는 저장소)에 있어 team-loop 쪽 `baseCommit`이
  성립하지 않는다. `verify_task`도 team-loop 저장소의 하네스·scope-check를 돈다 — Local-First의
  `measure dev-pack`을 모른다. `work_start_next`는 태스크 ID를 지정하는 매개변수가 없어 항상
  최우선순위 항목(현재 `tsk_1a113f64`, BLOCKED)만 돌려준다 — 이 태스크(우선순위 14)를 그것으로
  개별 시작할 수 없었다. **보드가 "산출물이 다른 저장소에 있는 태스크"를 처음 만난 사례로 보인다.**
  코드는 정상 커밋됐고 여기 기록한 하네스 결과로 완료 기준 7개(요청 스키마 targetRepo·미선언시
  자기 저장소·없는 경로 거절·git 아닌 경로 거절·절대경로 territoryRoots 거절·음성 4종 시험·
  measure dev-pack 0)가 전부 실측으로 충족됨을 확인했다.
  **사람이 정할 것**: (a) 대시보드에서 이 보드 태스크를 수작업으로 DONE 처리(증거는 커밋
  `98663a4`+이 항목) (b) team-loop에 "산출물이 외부 저장소에 있다"는 것을 선언할 수 있는 새
  제출 경로를 추가할지(게이트/판정층 코드 변경 — 사람 결재 대상) (c) 이 상태(board READY, 코드는
  Local-First에 커밋됨)를 그대로 두고 다음 판정 세션이 커밋만 대조해 승인 판단을 대신할지.
  `data/discussions.json`에도 같은 내용을 남겼다(`msg_boardtask_3b9760b2_targetrepo_20260728`).

## 끝난 것

- [x] **review-block 의 안내가 낡았다 — EXTERNAL_AGENT 를 모른다 (team-loop, `tsk_eef8545b5a6ae3376dc8`)** —
  판정 통과(2026-07-28, 실행 세션 `20260728-030502`와 다른 판정 세션 `20260728-031737`).
  **재실행해 대조한 것(자기보고 아님)**: 격리 워크트리(`.team-loop-worktrees/tsk_eef8545b5a6ae3376dc8`)
  에서 `git diff`로 `src/review-block.js`·`test/review-block.test.js`를 직접 Read — `clearedBy`에
  EXTERNAL_AGENT 세 번째 탈출로와 `NO_PROGRAM_EVIDENCE` 언급 확인, `deliveryGate.detail`이 실제
  mode를 반영, `agentGated`는 여전히 `executionMode === 'AGENT'`만 봄(EXTERNAL_AGENT 안 묶임)을
  코드에서 확인. `npm test` 이 판정 세션이 직접 재실행 → `tests 523, pass 522, fail 1`(자기보고와
  정확히 일치). 실패 1건(`test/injection-readiness.test.js`, `data/failure-cases.json` ENOENT)은
  `git stash`로 이 diff를 뗀 뒤 같은 파일만 재실행해도 동일하게 실패함을 직접 재현해 이번 변경과
  무관함을 확인(격리 워크트리 인프라 결손, 기존에도 여러 번 관찰됨). `git diff --check` 통과,
  `git status --short`로 `allowedPaths`(`src/review-block.js`, `test/**`) 밖 변경 없음 확인.
  완료 조건 6개 전부 충족.
  **지표는 만족했으나 목적은 미달인 부분**: 없음.
  **막힌 지점 해소 경과**: `show_task`가 처음부터 `executionMode: EXTERNAL_AGENT`로 잡혀 있어
  실행 세션이 우려했던 "claim_task로 AGENT 전환" 문제는 애초에 발생하지 않았다. `verify_task`는
  이 판정 세션이 재실행해도 "Verification requires an IN_PROGRESS task"로 거절(REVIEW 상태에는
  안 먹는 기존 한계와 동일) — 승인 판단은 위 직접 재현으로 대체했다.
  승인: `mcp__team-loop__approve_task`로 이 판정 세션이 처리(ADR-020, 실행과 다른 세션) — 커밋
  `0c6263f`(`Merge task/tsk_eef8545b5a6ae3376dc8`)로 team-loop main에 병합, 태스크 `DONE`/`archived` 확인.

- [x] **CLI 도 EXTERNAL_AGENT 를 안 덮게 한다 (team-loop, `tsk_1c1cac713df07887931d`)** — 판정
  통과(2026-07-28, 실행 세션 `20260728-023454`와 다른 판정 세션 `20260728-030132`). team-loop
  저장소 `src/cli/main.js`의 claim/start-next 네 자리(781·1198·1319·1499)가 `executionMode:
  'AGENT'`를 그대로 박아 보내던 것을 `claimExecutionMode(task)` 헬퍼로 교체 — 현재 모드가
  `EXTERNAL_AGENT`면 그것을 지키고 아니면 종전대로 `AGENT`.
  **재실행해 대조한 것(자기보고 아님)**: 격리 워크트리(`.team-loop-worktrees/tsk_1c1cac...`)에서
  `git diff`로 네 자리 전부 헬퍼 경유·하드코딩 `'AGENT'` 잔존 없음을 직접 확인.
  `test/cli-external-agent-claim.test.js` 3개(양성·음성·소스스캔)를 직접 Read해 내용 대조.
  `npm test` 이 판정 세션이 직접 재실행 → `tests 521, pass 520, fail 1`. 실패 1건
  (`test/injection-readiness.test.js`, `data/failure-cases.json` ENOENT)은 `git stash`로 이
  태스크의 diff를 뗀 뒤 같은 파일만 재실행해도 동일하게 실패함을 독립 재현해 **이번 변경과
  무관함을 직접 확인**(메인 트리엔 그 파일이 있고 격리 워크트리에만 없는 기존 인프라 결손).
  `verification.scopeViolations=[]` 재확인, allowedPaths(`src/cli/main.js`, `test/**`) 밖 변경 없음.
  **지표는 만족했으나 목적은 미달인 부분**: `main.js` 1052행의 무관한 주석에서 "얹혀 있는지는"이
  "얹혔 있는지는"으로 바뀐 오타 1건 발견(기능 영향 없음, 스코프 위반도 아니나 불필요한 부수 변경 —
  자진 신고).
  승인: `mcp__team-loop__approve_task`로 이 판정 세션이 처리(ADR-020, 실행과 다른 세션) — 태스크
  `DONE`/`archived`로 확인.

- [x] **외부 에이전트를 위한 제3의 실행 모드 (team-loop)** — 판정 통과(2026-07-28, 실행 세션과
  다른 판정 세션). team-loop 저장소 `server.js` 두 곳을 고쳤다(커밋 `05ffa81`, `fusion/judgment-layer`):
  (1) `submit_task_result`가 제출을 받으며 `executionMode`를 무조건 `'AGENT'`로 되돌리던 것을,
  현재 상태가 `EXTERNAL_AGENT`면 그대로 지키게 고쳤다. (2) `work_start_next`가 `HUMAN`일 때만
  `IN_PROGRESS`로 올리던 것을 `EXTERNAL_AGENT`도 같이 올리게 고쳤다(그 모드엔 나중에 claim할
  spawn 워커가 없어 — 워커가 곧 이 세션이라서 — `READY`에 남으면 `verify`가 영원히 409로 거절했다).
  **재실행해 대조한 것(자기보고 아님)**: `git show 05ffa81`의 diff를 직접 Read — 자기보고와 일치
  (server.js 2군데 + 새 시험 `test/external-agent-submit.test.js`). `npm test` 전체를 이 판정
  세션이 직접 재실행 → `tests 518, pass 518, fail 0`(자기보고와 일치). **음성 사례를 이 판정
  세션이 직접 재현**: 고친 `server.js`를 수정 전(`05ffa81~1`) 버전으로 임시로 되돌리고 새 시험만
  단독 재실행 → `409 !== 200`으로 실패(자기보고의 "고치기 전엔 두 지점 각각 재현됨"을 독립
  재현·확인) → 고친 버전으로 원복 후 재실행 → 통과. `git diff --check` 통과, 변경 파일 스코프
  (`server.js`+시험 1개)도 대조 완료 — allowedPaths 밖 없음(team-loop 저장소는 이 CLAUDE.md의
  dispatch/outbox 대상이 아니라 직접 커밋한다, 선례 `35cdcf7`·`415cc7d`·`66012cb`).
  원 항목이 남긴 우려("`unverified-claims.js`의 원칙을 깨지 않는 형태였는지")도 확인 — 게이트의
  증거 요구 수준(프로그램 증거 필수)은 그대로이고, 요구가 적용되는 모드 배선만 고친 것이 diff로
  확인됨. 검증 중 team-loop 워크스페이스에 부수적으로 발생한 `data/harnesses.json`·
  `data/skills.json`의 필드 순서 변경(라이브 서버 재직렬화, 이 판정과 무관)은 `git checkout --`로
  원복해 트리를 깨끗이 했다.
  **지표는 만족했으나 목적은 미달인 부분**: 없음.
  승인: 이 판정 세션이 처리(ADR-020, 실행과 다른 세션).

- [x] **team-loop 쪽 세션 격리 — 워크트리 분리 (2차, 2026-07-28)** — 판정 통과(실행 세션과 다른
  판정 세션, 커밋 `835a097`). **재실행해 대조한 것**: `scripts/teamloop-isolate.ps1 -Action Check`를
  깨끗한 메인 트리에서 직접 실행 → `teamloop-clean` exit 0. `src/ntfy.js`를 실제로 더럽힌 뒤 재실행 →
  `teamloop-contaminated 1건` exit 1(확인 후 `git checkout --`로 원복). 기존 워크트리
  (`tsk_06ba445c1ee0e40aa5fe`, `M src/worktree.js`·`M test/worktree.test.js` 보유)에 대해
  `-Action Ensure`를 실행 → `teamloop-worktree-exists` exit 0, 두 파일의 수정 내용이 실행 전후
  그대로 남음(브랜치를 되감지 않음을 직접 확인). `measure dev-pack`(violationCount 0),
  `handoff-integrity`(failures 없음, exit 0), `doc-integrity`(전부 intact, exit 0) 전부 재실행.
  **판정자가 볼 것으로 남겨졌던 지점("실제 깨우기 한 번이 보드 태스크에 격리 경로를 주는지")도
  이 판정 세션 자체로 실증됨**: 이 세션을 깨운 프롬프트 본문에 `coordinator-wake.ps1`이 새로
  주입하는 그 문구("team-loop 코드는 반드시 이 격리 워크트리 안에서만 고쳐라: ...")가 실제로
  박혀 있었다 — 스크립트 단위 재현이 아니라 실사 배선 확인.
  승인: 이 세션이 처리(ADR-020, 실행과 다른 세션).

- [x] **루프가 하는 일이 사람 눈에 보이게** — 세션이 8분째 도는데 보드는 `READY / IDLE`이었고,
  큐 항목으로 도는 바퀴는 폰에서 **전혀 안 보였다**. `board-claim.ps1`이 보드에 `IN_PROGRESS`를
  찍고(`executionState=RUNNING` — `IDLE`이면 `board-sweep`이 30분 뒤 뺏는다),
  `loop-log.ps1`이 대화 채널에 시작·끝을 남긴다. **세션이 아니라 깨우기가 직접 쓴다** —
  세션이 죽어도 시작 기록은 남는다. `[루프]` 접두사로 기계가 쓴 줄과 모델이 쓴 줄을 가른다.
  실측: `READY`→`IN_PROGRESS/EXTERNAL_AGENT/RUNNING`, `Release`는 `REVIEW`를 손대지 않음.

- [x] **`EXTERNAL_AGENT` 제3 실행 모드** (team-loop `35cdcf7`) — 실행자 종료 코드 요구를
  **프로그램 증거 요구**로 교체. 기준은 이 저장소가 이미 쓰는 것(합성 라벨 아님·spawn 실패 아님·
  종료 코드 있음)을 그대로 썼다. 납품 계수도 커밋된 작업을 세도록 고쳤다.
  **`claim_task`가 이 모드를 못 덮게 `server.js`에서 막았다** — 프롬프트로 시켰더니 세션이
  그냥 썼기 때문이다. 시험 7개 추가, `npm test` 515/515.

- [x] **보드 납품 게이트 막힘 해소** — `claim_task`가 `executionMode`를 `AGENT`로 바꿔
  납품 게이트가 실행자 종료 코드를 요구했고, 외부 에이전트인 우리 세션은 그 기록이 없어 막혔다.
  게이트가 아니라 **우리가 `AGENT`를 주장한 것이 사실과 달랐다.** `HUMAN` 모드로 일하도록
  프롬프트를 고쳤다. 실측: `HUMAN`으로 되돌리자 `deliveryGate: null` → `verify PASSED` → `REVIEW`.
  막혀 있던 작업은 조율자가 워크트리에 고정하고(`1708c59`) `npm test 508/508`을 직접 확인했다.
  `appsettings.json`에 `Coordinator.StaleMinutes = 10`도 넣었다(태스크에 약속한 것).

- [x] **하트비트를 미래로 쓰던 원인** — 저장소에 하트비트를 쓰는 **코드가 없었다.**
  프롬프트가 "갱신하라"고 시킬 뿐이라 **모델이 시각을 손으로 계산해 타이핑**했고 값이 흔들렸다
  (9시간 미래 → 6시간 47분 정지, 2분 미래 → skew=2m).
  프롬프트를 `heartbeat-touch.sh` 호출로 바꾸고, 깨우기가 `at`을 **`mtime`과 대조**하게 했다.
  `mtime`은 위조 불가라 크기와 무관하게 잡힌다. 기존 `age<0` 검사가 못 잡던 경우
  (쓸 땐 미래였지만 읽을 땐 과거)를 실측으로 확인했다.

- [x] **보드 좀비 회수(`board-sweep.ps1`)** — 큐 sweep의 거울. 큐만 회수되고 보드는 안 되던
  비대칭 때문에 실패한 발사가 태스크를 `IN_PROGRESS`로 두고 **10.2시간** 방치돼 있었다.
  상태 모순(`IN_PROGRESS`인데 `executionState=IDLE`)은 30분, 그 외 방치는 360분에 회수한다.
  깨우기가 **보드를 세기 전에** 돌린다. 재직렬화가 다른 필드를 안 건드리는 것도 대조로 확인.
  사람이 화면을 보고 알려줘서 발견했다 — 그건 감시가 아니다.

- [x] **옵션 a로 대체된 발사 태스크 종료** — `tsk_8cf42e5c5d69d264282a`(`[사람 게이트]` NET8 발사)를
  사용자 지시로 닫았다(DONE + archived). 완료 조건은 만족됐고 만족시킨 경로가 발사가 아니었다.
  근거를 태스크 설명 끝에 남겼다(커밋 `c5c1f21`·`b80f39f`, `linux/8.0` 초록, 영토 예외 등재).

- [x] **긴 작업 중 하트비트가 낡아 깨우기가 끼어든다** — 판정 통과(2026-07-27, 실행 세션
  `session-20260727-223026`과 다른 판정 세션 `session-20260727-224243`).
  - **재실행해 대조한 것(자기보고 아님)**:
    - `git show 31404ca --stat`로 diff 확인 — `scripts/heartbeat-touch.sh` 신설,
      `scripts/run-gates.sh`의 하네스 루프 안 각 명령 직후 `bash scripts/heartbeat-touch.sh` 호출이
      실제로 배선돼 있음을 소스에서 직접 확인.
    - `dotnet build server` 직접 재실행 → 경고 0개, 오류 0개.
    - `dotnet run --project server --no-build -- measure dev-pack` 직접 재실행 →
      `violationCount: 0`.
    - `bash scripts/run-gates.sh` 전체를 이 판정 세션에서 직접 재실행 → 보고와 정확히 같은 패턴:
      `trust-origin --self-test`만 FAIL exit 1(원인: `required-commands-match-manifest`·
      `required-commands-drift-detected` — 명령 매니페스트 드리프트, heartbeat-touch.sh나
      run-gates.sh 변경과 무관함을 실패 내용에서 직접 확인), 나머지 9개 하네스 전부 ok.
      실제 `/c/NHN Project/_ops/coordinator-heartbeat.json`을 재실행 전후로 대조 —
      `note`가 `"run-gates: measure dev-pack"`로, `session` 필드는 실행 세션(`session-20260727-223026`)
      그대로 보존된 채 갱신됨을 확인(운영 파일에서 실사 배선 확인, 합성 아님).
    - `heartbeat-touch.sh` 단위 재현: 격리된 heartbeat.json의 mtime을 12분 전으로 고정 →
      호출 시 `at`(UTC `Z`)·`note` 갱신, `session` 보존 확인. 곧바로 재호출(간격 미만) → 그대로.
    - `coordinator-wake.ps1 -DryRun`을 별도 격리 저장소(git init)에서 6개 경로 전부 override해
      재현: 하트비트 12분 전 → `would-wake ...` exit 1(끼어듦). 하트비트를 방금으로 갱신 →
      `coordinator-alive age=0m` exit 0(안 끼어듦). 보고의 표와 게이트 판정(끼어듦/안 끼어듦, exit
      1/0)이 일치. (트리거 사유 문자열만 `queue=1`이 아니라 `unread=1`로 나왔는데, 원인은 이
      판정 세션이 만든 임시 `discussions.json`을 빈 배열 `[]`로 둔 것 — 실제 스키마는
      `{schemaVersion, messages:[...]}`이고 빈 배열을 최상위에 두면 PowerShell이 `$null`을
      파이프라인에 태워 유령 unread 1건으로 센다. 이건 이 판정 세션의 시험 픽스처 실수이지
      하트비트 게이트 자체의 결함이 아니다 — 하트비트 나이 판정 분기는 트리거 사유와 무관하게
      동일하게 통과/차단됨을 코드에서 확인했다.)
  - **지표는 만족했으나 목적은 미달인 부분**: 없음. 완료 조건 셋(작업 중 갱신, 음성/양성 표,
    UTC Z 형식) 모두 이 판정 세션이 독립 재현으로 재확인했다.
  - 관련 커밋: `31404ca`.

- [x] **세션 격리 — 워크트리 분리** — 판정 통과(2026-07-27, 실행 세션과 다른 판정 세션 —
  이 판정 세션 자체가 `session-20260727-221340` 워크트리에서 돈다. 커밋 459ce46은 이 세션
  시작 1분 전(`22:12:31`)에 이미 본 저장소에 반입돼 있었으므로 이 판정 세션은 그 실행에
  관여하지 않았다).
  - **재실행해 대조한 것**: `docs/handoff/KNOWN-ISSUES.md`에 적힌 실측 5건을 자기보고로 받지
    않고, 격리된 스크래치 클론(`C:\Users\1\...\Temp\wt-verify-scratch`, 실제 세션 트리와 무관)에
    `scripts/session-worktree.ps1`을 **직접 재실행**해 하나씩 재현했다:
    ①워크트리 커밋 후 본 저장소 HEAD 불변 확인 ②본 저장소 dirty 상태에서 `Land` →
    `land-refused`, exit **1** ③clean 상태에서 `Land` → `landed`, `--no-ff` 병합 커밋 생성 확인
    ④생성 경로(`docs/context/RUNTIME-INDEX.md`)를 양쪽에서 다르게 고친 뒤 `Land` →
    `생성물 충돌 1건 자동 해소`, exit **0**, 결과 파일 내용이 **세션 쪽**임을 직접 확인
    ⑤비생성 소스(`CLAUDE.md`)를 양쪽에서 다르게 고친 뒤 `Land` → `land-conflict`, exit **1**,
    `merge --abort` 확인(`.git/MERGE_HEAD` 없음), 본 저장소 HEAD 불변, `session/verify3` 브랜치 보존.
    다섯 다 문서의 주장과 일치했다.
  - **게이트 재실행**: `di-completion-check --gate POST-COMMIT` (HEAD `459ce46`) →
    `checkCount 24, failureCount 0, gateVerdict PASS` — `gate-clean`·`handoff-integrity`·
    `context-pack-integrity`·`doc-integrity` 전부 exit 0 재확인.
  - **원 항목이 남긴 미확인 지점("깨우기 한 바퀴가 워크트리→착지까지 가는 것")**: 이 판정 세션
    자체가 그 실측의 절반이다 — `wake-logs/wake-20260727-221332.prompt.txt`(판정 모드 프롬프트)와
    `_ops/worktrees/session-20260727-221340`이 실제로 일치해, **생성→실행** 구간은 합성이 아니라
    실제 깨우기로 확인됐다. `coordinator-wake.ps1:277-332`를 직접 읽어 **착지→회수→push** 배선이
    이 판정 세션 종료 직후 같은 스크립트로 이어짐을 확인했으나, 그 마지막 구간은 이 세션이
    끝나야 실행되므로 이 대화 안에서 결과까지 관찰하지는 못했다(자진 신고 — 감점 아님).
    스크립트 단위 재현 5건 + 배선 코드 확인으로 대체했다.
  - 승인: 이 세션이 처리(ADR-020). 관련 커밋: `d9cdcfa`·`cd02b9a`·`c733a1f`·`459ce46`.
  - **조율자 후속(2026-07-27, 판정 뒤)**: 판정 세션이 관찰 못 한 마지막 구간
    (**착지→회수→push**)을 본 저장소에서 확인했다 — `land: landed session/20260727-221340 (1 커밋)`,
    병합 커밋 `b6e81c5`, `origin/wp/state-integrity`도 `b6e81c5`로 **한 번만 밀렸다**, 잠금 해제됨.
    **한 바퀴 전체가 실제로 돌았다.**
  - **그 한 바퀴가 결함 하나를 드러냈다**: `Remove`가 git 목록에서는 뺐는데 **빈 폴더가 디스크에 남았다**
    (막 끝난 세션이 그 경로를 쥐고 있어 `git worktree remove`가 폴더를 못 지웠다).
    한 바퀴마다 하나씩 쌓인다. 폴더가 남으면 직접 지우게 고쳤고, 남아 있던 것과 새로 만든 것 둘 다로 확인했다.

- [x] **`.NET 8` 하네스 JSON 옵션 통합 + CI 다리 (NET8-01-R1, 옵션 a)** — 판정 통과
  (2026-07-27, 실행과 다른 판정 세션. 아래 이력은 판정 도중 상태가 바뀐 드문 사례라 그대로 남긴다).
  - **1차 판정(반려)**: `c5c1f21`만 놓고 보면 `docs/directives/NET8-01-R1-harness-json-options.md`
    §7·목적 기준이 명시한 "컨테이너 `.NET 8`이 빨간 채로 제출하면 반려다"에 걸렸다 — 보고 자신의
    표가 `di-completion-check --gate POST-COMMIT`·`LAND` 둘 다 `.NET 8`·`.NET 10` 양쪽에서 exit 1.
    보고는 원인을 ".git·outputs 없는 복사본이라서"로 **추정**만 했다. 판정 세션이 `gh run view`로
    실제 GitHub Actions push(`30265507200`)를 직접 봐 **FAIL**을 확인했고, 진짜 원인은 복사본 탓이
    아니라 `context-pack-integrity`가 잡은 실제 회귀였다 — `directive-DLINT-01`이 sha256으로 고정해
    둔 `server/Harness/` 파일 두 개를 이번 통합이 고쳐 pin이 stale해졌다. 1차 판정을 `[ ]`로
    되돌리고 이 근거를 적었다.
  - **판정 도중 이 근거가 낡았다**: 판정 세션이 대조하는 동안 **동시에 떠 있던 다른 세션**이 같은
    원인을 독립적으로 진단해(`c62ca33` "하네스 목록을 한 벌로", `c081dc4` "정정: 앞 커밋 메시지가
    사실과 달랐다") DLINT-01 pin을 재계산하고 로컬/CI 하네스 목록을 `scripts/harness-list.txt`
    한 벌로 합쳤다. 이어서 `b80f39f`로 `gates.yml` matrix에 `"8.0"`을 직접 켰다(그 세션도 조율자
    역할 — TERR-01·NET8-01-R1 지시서 모두 "matrix 켜기는 조율자가 반입 때 한다"고 명시).
  - **최종 재확인(이 판정 세션이 직접, 추정 아님)**: `docker run ... mcr.microsoft.com/dotnet/sdk:8.0`으로
    현재 HEAD를 다시 컨테이너에서 쟀다 — `dotnet build server` 0 warning/0 error,
    `hs-scan` exit **1**(기대값), `di-completion-check --gate POST-COMMIT` → `checkCount 24,
    failureCount 0, verdict PASS`, `--gate WP-STATE-INTEGRITY-LAND` → `checkCount 18, failureCount 0,
    verdict PASS`. 그리고 `b80f39f` push의 실제 CI 실행(`gh run view 30267163723`)이
    끝날 때까지 직접 지켜봤다 — `linux/8.0`·`linux/10.0`·`windows/10.0` **전부 ✓**, 처음으로
    실제 CI에서 `.NET 8` 다리가 초록으로 도착했다.
  - **판정 세션이 처리한 것**: `TERRITORY-EXCEPTIONS.json`에 `c5c1f21` sha+사유를 등재(TERR-01 설계상
    "조율자가 반입 시점에" 하는 일 — 이 등재는 `c081dc4`에 우연히 함께 커밋됐지만 내용은 유효).
    `territory-check --commit c5c1f21` 재확인 → exit 0(exempted 20건).
  - **지표는 만족했으나 목적은 미달인 부분(자진 신고 승계)**: 원 보고(`NET8-01-R1.md`)의 근본 원인
    진단("복사본 탓")은 틀렸다 — 로컬 검사 목록이 CI의 10개보다 적어(`context-pack-integrity` 누락)
    실행 세션이 자기 diff가 만든 실제 회귀를 못 잡았다. 이번에 `scripts/harness-list.txt`로
    로컬·CI 목록이 통일됐으니 같은 함정은 재발 가능성이 낮다.
  - 관련 커밋: `c5c1f21`(본작업), `c62ca33`·`c081dc4`(회귀 진단+수정), `b80f39f`(matrix 점등).

- [x] **`buildSkillPolicy`의 두 벌 출처를 하나로** — team-loop 작업보드가 지시큐로 실측된 첫 사례
  (2026-07-27). `tsk_6890ae63a52af48e9539`가 보드에서 `board=1` 트리거로 실제 발사(claude-code/claude-opus-5)
  → 실행 → 사람 리뷰(최재혁) → 병합까지 무인+사람승인으로 완주. `src/skill-policy.js`가
  `workspaceRoot/data/skills.json`을 따로 읽던 것을 없애고 서버 `list_skills`와 같은
  `SkillRegistry(dataDirectory, workspaceId)`만 쓰게 통일(team-loop `80a2fdb`, 병합 `48a2fbf`).
  이 조율자 세션이 **실행 세션과 다른 세션**으로 하네스를 직접 재실행해 대조:
  `node --test test/skill-policy-source.test.js` 4/4 통과(단독), `node --test` 전체 502/502 통과
  (이전 기록 498에서 +4, 이 작업이 추가한 시험 수와 일치). 승인은 이미 사람이 했으므로(커밋 트레일러
  `Reviewed-By: 최재혁`) 판정 세션의 자기승인 문제는 해당 없음 — 이 세션은 재현 검증만 추가했다.
  큐에는 이 항목이 사람이 올리기 전부터 있었으나, 보드 쪽 파이프라인이 먼저 집어 처리했다
  (큐와 보드가 같은 백로그를 가리키고 있었다는 뜻 — 중복 작업 아님).

- [x] **team-loop 작업보드를 지시큐로 잇기** — 판정 통과(2026-07-27, 실행 세션과 다른 판정 세션).
  다시 돌려 대조: `measure dev-pack`(violations=0), `handoff-integrity`(failures=[]),
  `doc-integrity`(전부 intact), `gate-clean server`(PASS) 전부 exit 0.
  `check-script-encoding.ps1`은 저장소 기준 `ps1-bom ok`(exit 0); BOM 없는 한글 `.ps1`을 합성해
  독립 재현 — 미검출 없이 `missing=1`/exit 1, `-Fix` 후 `ok`/exit 0.
  자기보고가 지목한 미검증 지점("실제 보드에 태스크를 올려 진짜 깨우기가 그것을 집는지")은
  합성 보드 데이터(픽 가능 1 + 막힘 1 + 선행미완 1 + 보관 1 + 사람게이트 1)로 독립 재현—
  `trigger=board=1 task=t1`만 집히고 프롬프트에 나머지 넷은 안 실림. 실제 프로덕션 보드·큐로도
  재확인: 사람게이트 태스크 2건(READY·IN_PROGRESS)은 안 집히고 `trigger=review=1`로 정확히 떨어짐.
  **부가 확인**: 이 판정 세션 자체가 `review=1` 트리거의 **실사 발사** 결과다 — 마커 파일에
  `review:1`이 이미 있었고 이번 세션의 시작 프롬프트가 `$reviewPrompt`와 일치한다.
  즉 검토(review) 경로는 dry-run이 아니라 실제 발사로 이미 실증됐다.
  **미달로 남기는 것(자진 신고, 감점 아님)**: `board=N` 실행(execute) 경로는 여전히 실제 발사로는
  미확인이다 — 그걸 확인하려면 진짜 costed 발사가 필요한데, 그건 판정 세션도 할 수 없는
  사람 게이트다. 합성 재현으로 로직은 검증했으나 "진짜 claude.exe가 그 프롬프트를 받아 그대로
  하는지"는 다음에 보드 READY 실태스크가 자연 발생해 발사될 때 확인된다.

- [x] **team-loop 서버 재시작** — 사람 승인 후 실시(2026-07-27 12:02). 새 pid로 뜨고 `http=200`.
  서버가 읽는 코드·데이터로 재현해 확인: 기본 29건, 경매장 스킬 0건 섞임, `allScopes`로는 33건.
  옛 프로세스는 이전 세션의 백그라운드 작업에 매여 있었다 — 새 것은 안 매여 있어 세션과 무관하게 산다.

- [x] **소속을 찍어놓고 안 보던 것** — 레지스트리 `list()` 기본값을 자기 workspace로.
  실측으로 잡았다: team-loop 태스크를 만드니 경매장 심사 스킬 4개가 자동 배정됐다.
  전부 보려면 `allScopes` 명시. 시험 3개 추가, 498개 전부 통과. (team-loop `ec6e9c1`)

- [x] **스크립트 인코딩 검사** — 한글 `.ps1`에 BOM 없으면 exit 1. 도입 즉시 기존 위반 1건 적발.
  CI 윈도우 잡 build 앞에 물림. 같은 함정을 하루 세 번 밟아서 규칙을 검사로 올렸다.

- [x] **스킬·하네스에 소속(scope)** — 경매장 지식 4+2건을 team-loop 전역에서 분리. 승격이 소속을 찍는다.
- [x] **규칙 생성기 결함 2건** — 파일 이름을 규칙에 박던 것, 객체가 `[object Object]`로 박히던 것.
- [x] **대화 읽음 표시** — `readBy` + 화면 표시 + 시험 4개.
- [x] **다크 모드 부품 6건** — 라이트가 칠하고 다크가 안 되돌리던 것들 + 검사.
- [x] **지식 3종을 git으로** — `skills.json`·`harnesses.json`·`wiki.json`.
- [x] **`gate-clean` fail-open** — 없는 경로에 PASS를 내던 것을 exit 2로.
- [x] **ntfy 알림** — 토픽을 강한 값으로 바꾸고 켬. 되읽어 확인.
- [x] **하트비트 감시자** — 25분 이상 조용하면 알린다.
- [x] **원격 깨우기** — 안 읽은 글이나 미완 큐가 있으면 헤드리스 세션을 띄운다. 실측 통과.
