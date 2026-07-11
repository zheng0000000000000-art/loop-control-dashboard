# INTENT-DIGEST — 이 프로젝트가 무엇을 하려는가 (세션 인수인계용)

> **새 세션은 이 문서를 먼저 읽는다.** 사람의 로드맵 3종(`LLM_Runtime_Framework_Roadmap` · `LLM_Runtime_Society_Four_Pillars` · `Business_Aligned_Technical_Roadmap_Supplement`)에서 **작업 판단에 실제로 영향을 주는 것만** 추렸다. 원본은 사람이 보유하며, 이 요약은 **탐색용이지 최종 근거가 아니다**(계획서 §0.6).
>
> 작성: 검수자 세션, 2026-07-11.

## 1. 핵심 철학 (한 문단)

**LLM은 가능한 적게 기억하고 적게 생성한다. 프로그램이 가능한 많이 기억하고, 검색하고, 조립하고, 시뮬레이션하고, 검증한다. LLM은 판단만 한다.**

작업 중 판단이 갈릴 때 이 문장이 심판이다. "프롬프트로 시킨다" vs "코드로 강제한다"에서 **항상 후자**다.
2026-07-11 실증: 발사 프롬프트의 `ACK` 지시가 **5회 중 5회 무시**됐다. 말은 안 지켜지고, 검사만 지켜진다.

## 2. 우선순위 (세 로드맵의 합의)

```
P0  Runtime / Context Engineering   ← 지금 여기
P1  Knowledge (Wiki·Bundle·Skill·Harness·Promotion) + Resource Ledger
P2  Simulation · Society(Court·Clerk·Law) · Archive
P3+ Registry · Marketplace · Enterprise  ← 사업 기능. Runtime 성공 이후.
```

**원칙: 사업 기능이 좋아 보여도 Runtime 품질보다 우선하지 않는다.** 새 아이디어가 떠올라도 Runtime 개발을 방해하지 않는다.

**성공 기준(P0)**: 토큰 절감 · 작업 안정성 · 재사용성 · 실행 속도.

## 3. 우리 저장소의 좌표 — 로드맵을 안 보고 만들었는데 로드맵대로 자랐다

| 로드맵 항목 | 저장소의 실물 |
| --- | --- |
| ROOT 2 Knowledge (`Task → Candidate → Promotion → Wiki`) | `docs/wiki/failures/` · `hs-scan`(승격 트리거 기계 탐지) · `HS-CANDIDATES.md` · `skills/common/` |
| ROOT 3 Simulation ("실행 전 예측") | `orch-observe` — 발사 여부를 **계산만** 하고 실행하지 않는다 |
| ROOT 6 Event System ("사고가 아니라 행동을 기록") | `run-log.json` · ACTOR-01(결재 액션에 actor 기록) |
| ROOT 1 Context Engineering | **Phase 0에서 짓는 중** (Context Pack = 지시서, `context-pack-integrity`) |
| Axis 3 Society(P2, "연구") | **이미 굴러간다**: 조율자(Clerk) · HUMAN-INBOX(결재 큐) · ADR(Law Book) · HS-GATE(승격 심사) · 하네스(집행) |

> **Society는 "미뤄둔 P2"가 아니라 이미 P0에서 작동 중이다.** 필요해서 저절로 생겼다. 인정하되 **최소로 유지**한다 — 통제 체계가 제품보다 커지지 않게(계획서 §0 운영 원칙).

## 4. 지금 걸리는 것 두 가지 (검수자 판단, 사람 결정 대기)

**① 성공 기준을 측정하지 않는다 — 가장 큰 구멍**
로드맵의 P0 성공 기준은 **토큰 절감**인데, **토큰을 재는 코드가 없다.** `run-log`의 `cost` 필드는 664건 전부 0이고, ollama가 응답으로 주는 `prompt_eval_count`/`eval_count`를 그냥 버린다.
사람이 직접 써둔 원칙이 답이다 — **"경제 시스템보다 먼저 계측 시스템이 필요하다."** → `ADR-006`(Resource Ledger를 P0로).

**② 이름 체계가 넷이다**
`ROOT n`(Framework) / `Axis n`(Four Pillars) / `P0~P4`(Business) / `Phase·WP·DI`(v9 micro-directives).
2026-07-11에 **"같은 것을 다른 이름으로 재발명"** 해서 하루를 태웠다. 문서 층위에도 같은 씨앗이 있다. Phase 0 이후 매핑표 한 장(`ROADMAP-MAP.md`)을 만든다. **지금은 아니다.**

## 5. 새 세션이 하지 말아야 할 것

- 로드맵의 화려한 항목(Marketplace·Cognitive ABI·AI Runtime Language)에 손대는 것. **P3~P4다. Runtime이 성공한 뒤다.**
- 이름이 다르다고 이미 있는 것을 새로 만드는 것. **동등물 선언은 `ALIGNMENT-v9.md` §2에 있다.**
- 지표를 목표로 삼는 것. **지표는 목적의 프록시다**(`ADR-005`).
