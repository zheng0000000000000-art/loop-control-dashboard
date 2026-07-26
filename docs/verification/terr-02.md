# TERR-02 반입 검증 — 면제를 **그 커밋의 반입**에 묶는다 (2026-07-27)

## 주체

- **구현**: 코덱스(HARNESS_EXECUTOR), `LAUNCH-TERR-02`, exit 0, 5분 12초.
- **발사 결정**: 사람. `--manual` 없이는 `automated-execution-not-ready`.
- **검증·반입**: 조율 세션(Claude Opus 5), 사람 지시.

## 무엇이 문제였나

TERR-01 지시서를 **내가 경로 기준으로 썼다** — *"어떤 outbox 패치든 그 경로를 담고 있으면 반입"*.
그래서 **코덱스가 한 번이라도 만진 파일은 그 뒤로 조율자가 영원히 자유롭게 고칠 수 있었다.**
하네스는 지시서대로 만들어졌다. 고칠 것은 규칙이었다.

## 새 규칙

| | 조건 |
| --- | --- |
| **(가) 자기 증명** | 그 커밋이 **추가한** `candidate.patch`가 그 경로를 담는다 |
| **(나) 처분 결속** | `imported`이고 `importCommit == 그 커밋`인 처분의 패치가 담는다 |
| **(다) 사람 승인** | ledger에 사유와 함께 있다 |

**둘 다 필요한 이유**: 반입은 두 커밋에 걸친다. `importCommit`을 미리 알 수 없어 반입
커밋에서는 처분이 `pending`이고 다음 커밋에서 `imported`가 된다. **(가)만 두면 재적용 커밋이
막히고, (나)만 두면 반입 커밋 자신이 막힌다.** TERR-01 반입에서 직접 겪은 구조다.

## ★ 핵심 반증 — exit code로는 안 보인다

```
territory-check --commit 7feeb44d22039f3d92f934ca2dc6ba69bc0dd8f5
  TERR-01:  territoryPaths 3 | coveredByOutbox 1 | violations 2
  TERR-02:  territoryPaths 3 | coveredByOutbox 0 | violations 3
                                                   + server/Harness/GateWitnessCheckCli.cs
```

**exit code는 수정 전후 모두 1이다.** 이 저장소 규칙은 "판정은 exit code로"인데, 이 수정은
**그 규칙으로 증명할 수 없는 종류**다. 지시서에 그 점을 명시했고 수치로 판정했다.

면제 근거였던 것은 `LAUNCH-GWIT-01-R2`·`LAUNCH-GWIT-06` — `7feeb44`와 **무관한 예전 발사들**이다.

## 옳게 통과하는가 — 막는 것보다 이게 어렵다

| 대상 | 결과 |
| --- | --- |
| HEAD(반입 커밋 `49a833e`) | **exit 0**, coveredByOutbox **4/4** — (가)로 통과 |
| TERR-01 반입 커밋 `ea52a91` | **exit 0**, coveredByOutbox 4/4 |
| 처분 기록 커밋 `774c349` | **exit 0** (영토 안 건드림) |
| stale ledger | **exit 1**, violations 4 |
| 오타 옵션 | **exit 2** |

## 거부된 처분 — 짝을 지어 갈랐다

코덱스가 `--dispositions` 옵션과 `territory-non-imported/` 픽스처(`state: rejected`)를 만들었다.
**보고만으로는 "rejected라서 제외"인지 "그 디렉터리를 아예 안 읽는지" 구분되지 않는다.**
그래서 **`state` 한 필드만 뒤집었다**:

| state | coveredByOutbox | violations |
| --- | --- | --- |
| `rejected` | `[]` | 3 |
| `imported` (한 글자만 다름) | `[server/Harness/GateWitnessCheckCli.cs]` | 2 |

**읽기는 하고, `rejected`라서 제외하는 것이 맞다.**

## 매니페스트 배선

- **order 20 note 갱신**: `violations 2` → **3**. 안 고치면 note가 실재와 어긋난다.
- **order 22 신설**: `--dispositions <rejected 픽스처> --commit 7feeb44…` → expectedExit **1**.
  등재 전에 실측했다(exit 1, coveredByOutbox 0, violations 3).

## 참조한 스킬

`skills/common/` 전부. 변경 경로와 맞는 `skills/domains/` 트리거 없음.

## 지표는 만족했으나 목적은 미달인 부분

1. **`imported` 쌍 픽스처를 등재하지 못했다.** order 22는 `rejected`가 **면제되지 않는다**만
   기계로 잰다. *"읽기는 한다"* 는 위 §의 **수동 짝 시험으로만** 확인됐다.
   쌍 픽스처를 만들려면 `docs/qa/`에 파일을 추가해야 하는데 **그건 코덱스 영토라 조율자가 만들면
   territory-check 자신이 잡는다.** 하네스가 제대로 도는 증거이기도 하지만, **결과적으로 이
   한 축은 게이트가 아니라 문서다.** 다음 발사 때 묶어야 한다.
2. **`build-verify`가 코덱스 격리 사본에서 실패했다**(NuGet `NU1301`). 코덱스가 숨기지 않고
   대체 실행과 구분해 공개했고, 조율자 클론에서 exit 0으로 환경 탓임을 확인했다.
   **그러나 코덱스 쪽 격리 사본에서 원문 게이트가 완주하지 못하는 상태는 그대로다.**
3. **게이트 증거 보고는 처분 기록 커밋에서 잰 것**이라 반입 시점 판정이 아니다(TERR-01과 동일).
   보고를 만드는 순간에는 보고 파일이 없어 `launch-disposition` 한 건이 실패로 남는다.
