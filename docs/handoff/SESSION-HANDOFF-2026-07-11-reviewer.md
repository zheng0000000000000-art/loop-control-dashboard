# 인수인계 — 검수자 세션 2026-07-11 (하네스·스킬 정착 회차)

## 새 세션이 할 일 (순서)

1. `docs/STATUS.md` → `outputs/review-log.md`(조율자 최신) → `docs/handoff/sessions/SESSION-*codex*.md`(코덱스 최신)를 **먼저 읽는다.**
   **이번 세션의 가장 비싼 교훈**: 검수자가 저 기록들을 안 읽고 혼자 추측하다 **하루에 네 번 틀렸다.**
2. `docs/handoff/HARNESSES.md` — 하네스 목록·배선·필독 스킬.
3. `docs/handoff/HUMAN-INBOX.md` — 사람 결재 대기.

## 지금 상태 (전부 하네스 실측)

- **build**: exit 0 ✅ (서버 종료해 exe 락 해소 — I-3 풀림, 코덱스 QA 차단도 해소)
- **gate-clean**: PASS (contentDirty 0) / **doc-integrity**: INTACT 12/12 / **verify-behavior**: true
- **measure**: violations = **4** ⚠ (여러 문서가 3이라 적혀 있다 — 정정 필요. 코덱스가 먼저 지적했다)
- **push 대기**: 3건 (사람 게이트)

## 이번 세션에 선 것

**하네스 5종** (`server/Harness/`, HarnessRegistry 훅 — HOOK-01 `2e28f7a`):
`gate-clean`(트리 clean을 정규화 해시로) · `hs-scan`(승격 트리거 기계 탐지) · `claim-check`(자기보고 vs 실체) · `doc-integrity`(문서 잘림) · `e2e-usage`

**스킬 3종**:
- `skills/common/root-cause-diagnosis.md` — **프록시로 원인 단정 금지.** 검수자가 하루에 네 번 틀린 표가 들어 있다.
- `skills/common/executor-launch.md` — **발사 = 지시 도착 확인 + 실행 확인 + 범위 대조.**
- `skills/common/hs-gate.md` — 승격 심사. **2항의 "데이터 존재 관문"이 핵심.**

**조율자 프롬프트 교체** (스케줄 태스크 `recursion1-result-check`) — 저장소 정본 열람, 하네스로 판정, exit code로 빌드 판정. **18:51 회차부터 실제로 작동 확인.**

## 반드시 알아야 할 실패 (이번 세션 것)

| FAIL | 내용 |
| --- | --- |
| **FAIL-2026-010** | 줄바꿈(CRLF) 표현차가 발사 게이트를 영구 잠가 큐가 며칠간 정지 |
| **FAIL-2026-012** | 검수자가 커밋 접두사 `[loop]`를 행위주체로 오판 → **무인 결재 위반 22건을 날조.** `gate-audit` 하네스 철회 |
| **FAIL-2026-013** | **발사 프롬프트가 잘려서 도착.** 실행자는 지시서를 **받은 적이 없었다.** PowerShell `-ArgumentList @(...)` 배열이 프롬프트를 공백에서 쪼갬. 지시 없는 실행자는 저장소를 읽고 **알아서 딴 일을 했다**(안전 보류 항목 FEAT-01까지 구현). **"I-1 지시서 이탈"의 진짜 정체.** |

### 발사 방법 (반드시 이 방식으로)
```powershell
$argline = '-p "' + $prompt.Replace('"','\"') + '" --dangerously-skip-permissions'   # 단일 인용 문자열
Start-Process claude.exe -ArgumentList $argline -RedirectStandardOutput $log ...
```
- 프롬프트는 **한 줄**로. 상세는 지시서 파일에.
- 프롬프트 첫 지시 = **"맨 첫 줄에 ACK-<taskId>를 출력하라."** 출력에 ACK 없으면 **발사 실패 → 산출물 폐기.**

## 검수자가 반복한 실수 (같은 병 네 번)

| # | 본 것(프록시) | 단정 | 실제 |
| --- | --- | --- | --- |
| 1 | 커밋 접두사 `[loop]` | 무인 결재 22건 | 사람 승인에도 붙는 형식 |
| 2 | 파일 mtime = 프로세스 사망 시각 | 실행자의 지시서 이탈 | 다른 세션도 돌고 있었다 |
| 3 | CLI 에러 문구 | 세션 미격리 | 프롬프트 미도착이었다 |
| 4 | `error CS` 정규식 매치 0 | build 0/0 성공 | **exit code = 1(실패)**. exe 락 |

**정답은 매번 실체에 있었다** — exit code, 호출부, 실제 전달된 입력, **주체 본인의 진술**.
프로브 한 번(부작용 0)에 실행자가 답했다: *"메시지가 '아래'에서 끊긴 것 같습니다."*

## 다음 작업

**코덱스** (`docs/handoff/CODEX-QUEUE.md`, 코덱스가 스스로 픽업 — 조율자가 전달하지 않음):
- **H-00 `launch-check`** — ACK 에코백 검증(지시 미도착 사전 검출)
- **H-01 `build-verify`** — exit code + 락 우회. "락 실패"와 "코드 오류" 분해
- **H-0 `scope-check`** — 지시서 `## 허용 파일 (allowlist)` vs `git status` 대조
- H-1 `path-guard-check`, H-2~H-5

**sonnet** (`docs/handoff/SONNET-QUEUE.md`):
- #5 **ORCH-01** (관측 전용) — 발사 가능. #4 FEAT-01·#12 ACTOR-01은 **사람 결재 대기**.

## 사람 결재 대기 (대행 금지)
push 3건 · outbox 반입 3건(2건은 stale base → 거절 권장) · **ACTOR-01**(결재 액션에 actor 기록 — 오늘 세 번 "주체 미상"에 부딪혔다) · FEAT-01 승인 여부 · `outputs/quarantine/`의 격리 코드 처리.

## 한 줄
**문서·보고·표현은 프록시다. 판정은 실체로, 규칙은 코드로.**
그리고 **규칙을 어디에 썼는지보다, 그 주체가 실제로 무엇을 읽는지가 중요하다** — 조율자는 낡은 사본을, 코덱스 루틴은 옛 규칙을 읽고 있었다.
