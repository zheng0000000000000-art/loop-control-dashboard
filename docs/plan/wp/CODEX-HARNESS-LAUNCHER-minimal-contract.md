# Codex / GPT Harness Launcher — 최소 계약 (contract only)

- 상태: **계약 초안. 구현은 clean baseline(TRUSTED_BASELINE) 이후 착수.**
- 근거: 인프라 변경이 크므로 clean state가 서기 전에는 구현하지 않는다(사람 결정, 2026-07-13).
- 선행: 후속 지시서 5·6 land + clean replay 통과 → `TRUSTED_BASELINE`.
- 이 문서는 "무엇을 만들지"가 아니라 **"만들 때 지켜야 할 경계"**를 고정한다.

---

## 0. 목적

Codex/GPT를 **하네스·fixture·검증 자산을 만드는 실행 통로**로만 연결한다. 상태 판정은 계속
프로그램이 한다. 기존 dispatch/outbox 모델(AGENT-GUIDE: `executor: "codex"`, 임시 사본 실행,
diff를 outbox에 산출, 반입은 사람)을 대체하지 않고 그 위에 역할 제약을 씌운다.

---

## 1. 입력 (Launch Request 최소 필드)

```json
{
  "launchId": "LAUNCH-...",
  "actorRole": "HARNESS_EXECUTOR",
  "diId": "DI-...",
  "contextPackPath": "runtime/context/CTX-....json",
  "evidencePaths": ["outputs/.../*.json"],
  "allowedPaths": ["server/Harness/**", "docs/qa/**"],
  "forbiddenActions": ["commit", "push", "state-transition", "pass", "product-code-edit"],
  "expectedArtifacts": ["candidate.patch", "execution-report.json"],
  "timeoutSeconds": 1800
}
```

## 2. 동작 (transport + 기록만)

```text
1. actorRole == HARNESS_EXECUTOR 인지 확인. 아니면 즉시 거부.
2. allowedPaths를 server/Harness/ (+ 승인된 fixture 경로)로 제한. 밖의 변경은 scope-check로 FAIL.
3. 임시 사본에서 codex 실행 (기존 dispatch 경로 재사용).
4. stdout / stderr / exit code / 산출 artifact의 sha256을 evidence로 기록.
5. evidence를 **종료 이벤트로 제출**한다 — Launcher의 책임은 여기까지.
6. Program Verifier가 **독립 실행**해 판정하고, 통과 시에만 **승인된 transition request**를 생성한다.
   canonical state 변경은 그 request가 사람 결재 + StateApplier 전이를 통과할 때만 일어난다.
```

Launcher는 transport와 기록만 한다. 역할 선택·성공 판정·상태 전이·fallback은 Launcher의 책임이 아니다.

## 3. Codex/GPT가 갖지 않는 권한 (금지)

```text
- actor(역할) 선택
- PASS / VERIFIED 선언
- canonical state(WORKSTATE) 직접 수정
- commit / push
- 일반 제품 코드 수정 (server/Harness/ 밖)
- 자신이 만든 하네스에 맞춘 제품 코드 수정
- 실패 후 임의 fallback
```

## 4. 상태 반영 경계 (재발명 금지)

- Codex 산출물은 **outbox의 candidate**로만 남는다. 반입은 사람.
- canonical state 변경은 오직 `StateApplierCli` 전이(sha256·AllowedTransitions·candidate 재검증·
  StateApplier apply integrity pipeline)을 통과해야 한다. Launcher가 state를 직접 쓰지 않는다.
- 즉 "검증 결과만 반영"은 **Program Verifier 통과 → 전이 request 생성 → 사람 결재/승인 경로 →
  StateApplier 전이**를 뜻한다. Launcher는 이 사슬의 transport 한 칸일 뿐이다.

## 5. Dispatcher 전체를 지금 만들지 않는다

이벤트 Outbox·Dispatcher·Watchdog 전체(P00-B1)는 이 계약의 범위가 아니다. 최초 연결은
**단일 CodexHarnessLauncher + Program Verifier 결속**까지만이다. 그 이상은 clean baseline 이후
실측 데이터를 보고 별도 DI로 확장한다.

---

## 6. 착수 전 사람 확인 항목

1. `TRUSTED_BASELINE` 선언이 끝났는가 (WP-STATE-INTEGRITY land + clean replay).
2. `executor: "codex"` dispatch 경로의 현재 토큰·권한 설정(AGENT-GUIDE ②)이 위 금지선과 충돌하지 않는가.
3. Program Verifier가 P00-B0 최소 검사(scope·preimage·build·target oracle·regression·evidence)를
   실제로 실행 가능한 상태인가.

**활성화 조건:** Launcher **구현 착수**는 `TRUSTED_BASELINE` 이후 가능하다. 그러나 실제 **자동 발사**는
`WP-STATE-LAUNCH-GATE`(06L) 통과 후 `AUTOMATED_EXECUTION_READY`에서만 허용한다 — 그 전에는 수동 dispatch만.

## 7. 구현 지시서로 전환 시 추가 (검토 반영)

계약→구현으로 넘어갈 때 Launch Request에 다음을 추가한다.

```json
{
  "schemaVersion": "1",
  "transport": "codex-cli",
  "modelProfileId": "…",
  "credentialRef": "…",
  "baselineCommit": "…",
  "directivePath": "…",
  "directiveSha256": "…",
  "contextPackSha256": "…",
  "attempt": 1,
  "maxAttempts": 2,
  "dedupKey": "…",
  "workingCopyMode": "isolated-clean"
}
```

`credentialRef`는 자격 증명 참조만 담고 실제 값은 담지 않는다. `directiveSha256`·`contextPackSha256`은
transport 무결성(ADR-010 계열)과 결속한다.
