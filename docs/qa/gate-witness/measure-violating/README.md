# `measure` 반증 픽스처

`strict-pack`은 `dev-pack`의 blueprint 사본에서 `maxFunctionLength`의 `target`을 **0**으로 바꾼 것이다.

**`target`은 상한이 아니라 일치 조건이다**(`IsMetricWithinBlueprint`: `actual == target`, `target`이
`band`보다 우선). 함수 길이가 정확히 0인 저장소는 없으므로 **항상 위반 1건**이 나온다 —
저장소가 변해도 결과가 흔들리지 않는다.

```
measure strict-pack --fixture docs/qa/gate-witness/measure-violating   → exit 1
```

**이 픽스처가 exit 0을 내기 시작하면 `measure`가 위반을 위반으로 보고하지 않는 것이다.
픽스처를 고치지 마라 — 검사를 고쳐라.**

측정은 파일을 쓰므로 `--fixture`는 `dashboard/lfwd-measure-fixture-<guid>/`에 사본을 만들어
거기서 돌리고 지운다. 저장소 안 같은 깊이에 두는 이유는 구조 지표가 프로젝트 경로에서
저장소 루트를 되짚기 때문이다. 정리에 실패하면 **stderr에 `fixtureCleanupFailed`를 남긴다** —
조용히 삼키면 잔여물이 쌓이는데도 트리가 깨끗해 보인다(2026-07-26 실제로 4개 쌓였다).
