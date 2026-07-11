# REVIEWER-HANDOFF — 검수자 세션 인수인계 (상시 최신)

> **쓰기 주체: 검수자만**(ADR-003 단일 기록자). 이 문서는 **판단할 때마다 갱신한다** — 세션 종료 시점이 아니다. 세션은 예고 없이 죽는다(한도·컨텍스트 한계).
> 갱신: 2026-07-12 00:0x

## 0. 새 검수자 세션이 읽는 순서 (이것만 읽으면 이어받는다)

1. **`docs/plan/INTENT-DIGEST.md`** — 이 프로젝트가 무엇을 하려는가. 핵심 철학 한 문장.
2. **이 문서** — 지금 어디까지 왔고 다음에 뭘 하는가.
3. **`docs/context/RUNTIME-INDEX.md`** (P0-04가 생성) — 기계가 만든 L0 상태. **손으로 쓴 문서보다 이걸 믿어라.**
4. **`outputs/review-log.md`**(조율자) + **`docs/handoff/sessions/`**(코덱스 최신) — 독립 관찰자 기록. **안 읽고 추측하면 틀린다. 2026-07-11에 검수자가 하루에 네 번 틀렸다.**
5. `docs/handoff/decisions/ADR-*.md` — 왜 이렇게 하기로 했는가.

## 1. 지금 어디인가

- **운영 등급**: `Required Before Multi-model Parallel Work` (ADR-001, 사람 승인). 실행자 4명 병렬 + 자동 스케줄러 가동 중.
- **Phase 0 진행** (계획서 흡수):

| DI | 상태 |
| --- | --- |
| P0-01 ALIGNMENT + ADR-001 | ✅ |
| P0-02 ADR 기반(ADR-001~006) + 배선 | ✅ |
| P0-03 `handoff-integrity` 하네스 | ✅ 주석 5건 보완 후 **커밋 완료(`a7068ad`)**. `server/` clean |
| P0-04 Projection 생성기 + WORKSTATE 해시 | ✅ **PASS**(2026-07-12 00:0x 재판정). `handoff-integrity` **exit 0** / `projection` 멱등 / **반증 시험 통과: 파일 1줄 변조 → exit 1, 원복 → exit 0**(게이트 공허하지 않음). 실행자 PID 9804 종료, 산출물 커밋됨 |
| P0-05 Context Pack + `context-pack-integrity` | ⬜ 코덱스 큐 |
| P0-06 파일 소유권 claim(`FILE-CLAIMS.json`) | ⬜ 코덱스 큐(사양 확정) |
| P0-07 `HS-GATE-P00` + 독립 재개 시험 | ⬜ **사람이 PASS 판정** |

- **`HS-GATE-P00` PASS 전까지 Phase 1(기능 개발)로 넘어가지 않는다.**

## 2. 지금 돌고 있는 것

| 주체 | 주기 | 이사(컨텍스트) |
| --- | --- | --- |
| 조율자 | 5분(`recursion1-result-check`) | 매 회차 새 세션 — 문제 없음 |
| 코덱스 | 15분 `:07/:22/:37/:52` | **2026-07-11부터 매 회차 새 스레드**(`target_thread_id` 제거). 인계는 `SESSION-*.md` 하나로 |
| sonnet | 발사식 | 1 DI = 1 프로세스 |
| **검수자(너)** | 대화 | **이 문서로 이사한다** |

## 3. 사람 결재 대기 (대행 금지)

- **ADR-006 승인됨** → `LEDGER-01`(ollama 토큰 계측) 지시서 **미발행**. 다음 발사 후보.
- `FEAT-01`(반입 승인 AI 위임) — 안전 보류. **`appsettings.json`의 `Tier2Approver.Enabled=true`로 코드에는 이미 켜져 있다**(모순, 브리핑 0-B항).
- `outputs/quarantine/` 3건 처리.
- push는 사람이 직접 한다(2026-07-11 47건 push 완료).

## 4. 다음 세 수

*(P0-04 검수 완료로 갱신 — 2026-07-12 00:0x)*

1. **WORKSTATE `changedFiles` 회전** — ⚠️ **P0-04 잔여 결함.** WORKSTATE는 `diId: P0-04`를 선언하는데 `changedFiles`는 **FIX-07의 파일 3건 그대로다**(앞 세션이 diId만 고치고 회전을 안 했다). 하네스는 **남의 DI 파일을 검증하며 green**이다. 해시 검증은 진짜지만 **핸드오프가 현실을 기술하지 않는다** = 목적 미달(ADR-005). FIX-07 항목을 `history`로 내리고 P0-04 산출물(`server/Cli/*`, projection·handoff-integrity 코드)로 교체 후 `projection` 재실행. **코덱스/실행자 몫 — 검수자는 WORKSTATE에 쓰지 않는다(ADR-003).**
2. **LEDGER-01 발사** — ollama 응답의 `prompt_eval_count`/`eval_count`를 이미 있는 `cost` 필드에 기록. **"토큰을 줄이자"는 프로젝트가 토큰을 안 재고 있다.** 발사는 사람 게이트.
3. **P0-05 → P0-06 → P0-07(HS-GATE)**. P0-05는 여전히 **data gate 블록**(코덱스 051: 기계가 읽을 `requiredInputs`/`readOrder` 실데이터가 없어 하네스를 못 만든다). 스키마부터 확정해야 진행된다.

### HS-GATE-P00 판정 시 사람이 알아야 할 구조적 한계

- **스탬핑 주체 = 검증 주체.** `projection`이 sha256을 쓰고 `handoff-integrity`가 그걸 검증한다. 파일 변조는 잡지만, **`projection` 재실행으로 언제든 green을 제조할 수 있다.** 현재 게이트의 목적은 "핸드오프 이후 드리프트 탐지"이며 그 범위에서는 유효하다. 폐기 사유는 아니지만 **게이트를 신뢰의 근거로 삼을 때 이 한계를 알고 있어야 한다.**
- ACK는 없을 것이다(ADR-004: `claude -p`는 첫 줄 ACK를 못 낸다. 5/5 실패). **폐기 사유 아니다.**

## 5. 절대 하지 말 것 (전부 실제 사고에서 나옴)

- **프록시로 원인을 단정하지 마라.** 커밋 접두사·타임스탬프 상관·에러 문구·정규식 매치는 **증거가 아니다.** 2026-07-11에 이걸로 네 번 틀렸다. **정답은 매번 실체에 있었다 — exit code, 호출부, 실제 전달된 입력, 그리고 주체에게 직접 묻기.**
- **결재(approve/reject/import)를 대행하지 마라.** 사람 몫이다.
- **기준 파일(`blueprint.json`·`workflow-definition.json`)을 고쳐 게이트를 통과시키지 마라.** 변경했으면 `BASELINE-CHANGES.md`에 주체·근거·되돌리는 법을 남긴다.
- **하네스를 직접 만들지 마라.** 제작은 코덱스, 검사는 조율자(ADR-002). 검수자가 만들다 같은 실패를 3회 반복했다.
- **남의 기록 파일에 쓰지 마라**(ADR-003). 검수자는 `outputs/reviewer-log.md`와 `BASELINE-CHANGES.md`·`decisions/`만 쓴다.
- **지표를 목표로 삼지 마라**(ADR-005). 지표는 목적의 프록시다. verification 문서의 `## 지표는 만족했으나 목적은 미달인 부분`을 반드시 확인하라.

## 6. 발사 방법 (반드시 이 방식)

```powershell
$argline = '-p "' + $prompt.Replace('"','\"') + '" --dangerously-skip-permissions'  # 단일 인용 문자열
Start-Process claude.exe -ArgumentList $argline -RedirectStandardOutput $log -PassThru
```

- **프롬프트는 한 줄로.** 배열 인자(`-ArgumentList @(...)`)는 공백에서 쪼개져 **프롬프트가 잘려 도착한다**(FAIL-2026-013). 지시 없는 실행자는 저장소를 읽고 **알아서 딴 일을 한다.**
- 발사 전 게이트: `gate-clean <대상경로>` exit 0 / 실행 중 sonnet 없음 / 이전 항목 커밋됨 / 다음 대기 존재.
- **발사는 사람 게이트다.**

## 7. 오늘의 한 줄

**문서·보고·표현은 프록시다. 판정은 실체로, 규칙은 코드로.**
그리고 — **규칙을 어디에 썼는지보다, 그 주체가 실제로 무엇을 읽는지가 중요하다.**
