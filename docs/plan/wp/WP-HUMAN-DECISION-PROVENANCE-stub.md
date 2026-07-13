# WP-HUMAN-DECISION-PROVENANCE (후속 P0 — 스텁)

> WP-STATE-INTEGRITY가 fail-closed로 막아둔 high-risk 전이(PHASE_CHANGE/RECOVERY/REPLAY)를 활성화하는 WP.
> 실측 결론: 현재 저장소엔 StateApplier가 신뢰할 수 있는 human approval receipt가 **없다**.

## 왜 필요한가 (실측)

- **ACTOR-01은 provenance가 아니다.** 승인 요청 body의 `actorType:"human"`을 기록만 하고, 위조 방지는
  별도 과제로 남아 있다. "누가 했다고 주장했는지"지 "사람이 했는지"가 아니다.
- **`X-Action-Token`은 사람 신원 증명이 아니다.** 승인 API 호출 권한일 뿐, 토큰 쥔 프로세스가 사람인지
  AI인지 구분 못 한다.
- **`human_decision_*.md`는 receipt가 아니다.** "결정 필요/추천 조치"를 담은 판단 요청 패킷이다.

→ 사용자가 작성한 임의 JSON을 승인으로 믿으면 AI/실행자가 자기 승인을 위조할 수 있다. 서버 발행 receipt가 필요.

## 결정된 구조 — (가)+(다) 결합형

```text
사람 전용 승인 경로 → 서버가 receipt 발행(가) → append-only ledger 저장(다) → StateApplier가 receipt 대조
```

## 조각

1. **승인 요청.** envelope 등이 `{decisionRequestId, purpose, transitionKind, transitionId, envelopeSha256,
   requestSha256, expectedPreStateSha256, expectedPostStateSha256, effectiveAt, expiresAt}` 생성.
   정본 결속 키는 `decisionPayloadSha256`.
2. **사람 전용 승인 경로(기존 `X-Action-Token`과 분리).** P0 현실안: 사람이 대시보드 로그인/승인 →
   서버가 challenge nonce 발급 → 인증 세션이 nonce+payload hash 승인 → receipt 발행. **AI 실행자에게 이
   credential을 주지 않는 것**이 핵심. (PKI/서명은 후속.)
3. **서버가 receipt 발행(사용자 JSON 신뢰 금지).** `{schemaVersion, receiptId, decisionRequestId,
   decisionPayloadSha256, purpose, transitionKind, transitionId, approved, approvedBy(인증 세션에서 결정 —
   body 복사 금지), issuedBy, issuedAt, expiresAt, nonce, oneShot}`.
4. **append-only ledger.** `docs/handoff/human-decision-ledger/HDRC-*.json` + `index.jsonl`. receiptId별
   atomic create · 본문 수정 금지 · 승인/거절 모두 기록 · 중복 nonce 거부 · 만료 거부 · 소비는 별도
   append-only event(`RECEIPT_CONSUMED`)로.
5. **StateApplier는 receipt-id만 받음.** `state-transition apply --envelope … --human-receipt-id HDRC-…`.
   검증: ledger 존재·서버 발행 형식·approved·미만료·미소비·purpose/kind/transitionId 일치·
   decisionPayloadSha256 일치·envelope와 pre/post/request hash 일치. 불일치 → exit 1 `trusted-human-receipt-required`.
   임의 경로 `--human-decision file.json`은 폐기.

## 신뢰 등급 (섞지 않기)

```text
actorType/actorId/actorPath  → CLAIMED_ACTOR (UI·감사·커밋·outbox meta)
서버 발행 receipt            → VERIFIED_HUMAN_APPROVAL
```

## 완료 후

```text
VERIFIED_HUMAN_APPROVAL_READY = true
→ RECOVERY_APPLY_READY / PHASE_CHANGE_READY / REPLAY_READY = true
```

trust epoch ≥1에서의 새 trust-origin 선언도 이 receipt를 요구한다(부트스트랩 예외는 epoch 0 1회뿐).
