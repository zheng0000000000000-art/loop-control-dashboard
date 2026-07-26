# GWIT-01 검증 — 성공만 확인하는 게이트를 드러낸다

- **주체(actor)**: 산출은 **코덱스**(`LAUNCH-GWIT-01-R2`). 검증·반입 집행은 **조율 세션**,
  결재는 **사람**. 검증 문서는 코덱스 영역 밖이라 조율자가 쓴다.
- **날짜**: 2026-07-26

## 반증 시험 — 4종 전부 실측

| # | 시험 | 기대 | 실측 |
| --- | --- | --- | --- |
| 1 | 현재 매니페스트 | `totalUnwitnessed 17`, 6/4/7 | **17, 6/4/7 정확히 일치** ✅ |
| 2 | LAND의 비0 기대값 검사 제거 | 숫자가 **증가**한다 | **LAND 7 → 9, 합계 17 → 19** ✅ |
| 3 | `POST-COMMIT`에 `requireFailureWitness: true` | **exit 1** + 목록 | **exit 1**, 4건 이름 나옴 ✅ |
| 4 | 반증 짝을 갖춘 게이트 + 플래그 | exit 0 | **exit 0** ✅ |

**시험 2가 이 하네스의 핵심 증명이다.** 상수를 돌려주는 구현이면 픽스처를 바꿔도 숫자가 안 움직인다.
7 → 9로 움직였으므로 **실제로 매니페스트를 읽는다.**

시험 3 출력:

```
POST-COMMIT 없음: 4 | 목록: ['gate-clean', 'handoff-integrity', 'context-pack-integrity', 'doc-integrity']
```

## ★ 첫 발사가 드러낸 것 — **런처의 결함이었다**

1차 발사(`LAUNCH-GWIT-01`)의 후보를 반입하니 **빌드가 깨졌다**(`error CS0103`).

```
changedPaths           3개 (HarnessRegistry.cs · docs/qa/gate-witness/ · GateWitnessCheckCli.cs)
candidate.patch 안     1개 (HarnessRegistry.cs)
```

`CodexHarnessLauncher`가 패치를 `git --no-pager diff`로 만들었는데 **그것은 untracked 새 파일을
포함하지 않는다.** 새 클래스가 빠진 채 그것을 참조하는 수정만 실렸다.

**조율자가 오늘 만든 결함이고, 같은 함정을 같은 날 이미 한 번 만났다** — team-loop에서 리뷰어에게
`git diff HEAD~1`을 시켰더니 untracked 새 테스트 파일이 안 보여 *"테스트가 없다"*고 정확히 거절했던
그것이다. **같은 원인을 두 저장소에서 두 번 만들었다.**

고친 뒤(`129d44d`) 재발사한 결과 `pathsMissingFromPatch: []`이고 새 파일이 정상 포함됐다.
고치는 것으로 끝내지 않고 **어긋남 자체를 탐지**하게 했다 — `changedPaths`에 있는데 패치에 없으면
exit 1이다.

**이 결함은 앞선 세 발사(`CG04A`·`CG04A-R1`·`HREG-01`)에서 드러나지 않았다.** 셋 다 기존 파일만
고쳤기 때문이다. **세 번 통과한 뒤 네 번째에 나왔다.**

## 착륙 (두 걸음)

1. **①** 패치 적용 → `context-pack-integrity` exit 1 (`HarnessRegistry.cs` stale)
2. **②** pin 갱신 `f5dcb5050458…` → `5306efebe79b…`

②의 소유자는 `GATE-TRUTH-01`이다. **이번에는 가정하지 않고 먼저 grep으로 찾았다** —
지난 착륙(`HREG-01`)에서 `DLINT-01`을 가정했다가 실패한 것에서 배웠다.

## 사용한 하네스

`codex-launch validate/launch` 0 · `build-verify` 0 · `gate-witness-check` 0 ·
`context-pack-integrity` 0(②이후) · `measure dev-pack` 위반 0 · `doc-integrity` 0 ·
`handoff-integrity` 0.

## 지표는 만족했으나 목적은 미달인 부분

1. **세는 것만으로는 아무것도 막지 않는다.** 어느 게이트에도 `requireFailureWitness`를 켜지
   않았다(지시서 §1-C가 그렇게 지시했다). 켜지 않으면 이 하네스도 *"기록만 하고 아무 일도
   일어나지 않는"* 것이 된다 — 이 세션이 내내 잡아온 그 모양이다. **켜는 판단은 사람 몫이다.**
2. **`gate-witness-check` 자신이 아직 게이트에 등재되지 않았다.** 등재는 조율자 후속이며,
   등재하면 지시서 §5대로 자기 반증 witness가 필요하다. `docs/qa/gate-witness/`의 픽스처가
   그 자리에 쓰인다.
3. **17건에 반증 짝을 붙이는 일은 시작도 안 했다.** 이 하네스는 드러낼 뿐이다.
4. `internalNegativeCases` 경로(§1-C의 주장 검증)를 시험하지 않았다. 현재 매니페스트에 그 필드를
   쓰는 검사가 없기 때문이다.
