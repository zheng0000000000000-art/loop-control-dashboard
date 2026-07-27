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

- **team-loop 보드 태스크(`tsk_aa08207b993b422a4fdf`)는 판정 통과인데 REVIEW→DONE 을 옮기는 MCP 도구가 없다.** (2026-07-28 발견, 실행 세션과 다른 판정 세션)
  **재실행해 대조한 것(자기보고 아님)**: `git log`로 `1708c59`가 `8af99b3`로 `fusion/judgment-layer`에 이미 병합돼 있음을 확인. `src/coordinator-presence.js`·`test/coordinator-presence.test.js`를 직접 Read — `DEFAULT_STALE_MINUTES=25` 삭제, `appsettings.json`의 `Coordinator.StaleMinutes`를 읽고 실패/누락 시 10으로 떨어지는 `resolveStaleMinutes` 신설, `future`/`skewMinutes` 반환, `absenceNotice`가 future 분기에서 "고장"/"미래" 문구를 내고 "조용하다"는 안 냄을 코드에서 확인. `npm test` 이 세션이 직접 재실행 → `tests 508, pass 508, fail 0`(자기보고와 일치). `git diff 48a2fbf..1708c59 --stat`로 scope 재확인 → 두 파일만 변경(allowedPaths 일치). `appsettings.json` 실측 → `Coordinator.StaleMinutes=10` 이미 반영됨. 완료 조건 6개 전부 코드+시험으로 대조 완료.
  **판정**: 완료 기준 충족.
  **막힌 지점**: `show_task`로 보면 이미 `status: REVIEW`(`verification PASSED`, `review.status PENDING`). 이 판정 세션이 `verify_task`로 승인하려 했으나 서버가 "Verification requires an IN_PROGRESS task"로 거절 — `verify_task`는 REVIEW 상태에는 안 먹는다(이미 실행 세션이 IN_PROGRESS일 때 한 번 돌려 PASSED를 받은 뒤 `request_review_task`로 REVIEW로 넘어간 상태라서다). MCP 도구 목록 어디에도 REVIEW→DONE 승인 도구가 없다. `server.js`에서 찾은 유일한 경로는 HTTP `POST action=review`(decision=APPROVE, 실제 merge+DONE+archive까지 함)뿐인데, 이건 이전 실행 세션이 `msg_boardtask_aa08207_deliverygate_20260727`에 남긴 것과 같은 종류의 "MCP 처리 범위 밖" HTTP 우회다 — actor 인증도 이 세션엔 없고 merge+archive는 되돌리기 번거로운 상태 변화라 직접 호출하지 않았다.
  **사람이 정할 것**: (a) 대시보드에서 직접 승인 클릭 (b) 이 세션류가 HTTP `action=review`를 직접 호출해도 되는지 명시 허가 (c) `verify_task`가 REVIEW 상태에서도 승인으로 동작하게 하거나 별도 `approve_task` MCP 도구를 신설(게이트/판정층 코드 변경 — 사람 결재 대상). `data/discussions.json`에도 같은 내용을 남겼다(`msg_boardtask_aa08207_reviewgap_20260728`).

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

## 끝난 것

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
