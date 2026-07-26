```context-pack
{
  "diId": "GCLEAN-01",
  "requiredInputs": [
    { "path": "docs/directives/_header.md", "sha256": "b37a27f81792e82575a793f671839fdf463895e8ce4d1d4ccf7c5bea1213b2ee" }
  ],
  "readOrder": [
    "docs/context/RUNTIME-INDEX.md",
    "docs/directives/_header.md",
    "docs/directives/GCLEAN-01-path-must-exist.md"
  ],
  "forbiddenActions": ["git commit", "git push", "approve", "reject", "import", "spawn-executor", "edit-baseline"]
}
```

# GCLEAN-01 — 검사 대상 경로가 없으면 PASS가 아니라 입력 오류다

이 지시서는 `docs/directives/_header.md`의 불변 제약을 따른다.

## 1. 왜 — 실측된 fail-open이다

```
gate-clean nonexistent-xyz      exit 0
gate-clean server   (team-loop 저장소에서 — 그 경로가 없다)   exit 0
```

**`gate-clean <경로>`는 경로가 존재하지 않아도 PASS를 낸다.** 오타든 개명이든 **조용히 초록**이다.

**직격 지점 둘:**

1. **`POST-COMMIT` order 1이 `gate-clean server`다.** 누가 `server/`를 개명하면 **그 게이트의
   첫 검사가 아무것도 안 재고 통과**한다.
2. **인자가 없을 때의 기본값도 `server`다**(실측: `paths: ["server"]`). 융합으로 이 하네스를
   다른 저장소에 겨누는 순간 **기본 실행부터 fail-open**이다.

`--manifestt` 오타가 fixture 대신 production을 재고 exit 0을 냈던 것과 **같은 부류**다.
그때는 **옵션**이었고 옵션 검증은 `CliOptions`로 고쳤다. 이번은 **인자로 받은 경로**이고
**그 실재 여부는 아무도 보지 않는다.**

## 2. 무엇을 고치나

**검사 대상 경로가 존재하지 않으면 `exit 2`(입력 오류)다.** PASS도 FAIL도 아니다.

- 판정(0/1)은 **잴 수 있었을 때만** 낸다. 재지 못한 것을 통과로 적으면 그건 판정이 아니라 침묵이다.
- **인자 없이 실행할 때의 기본값에도 같은 규칙을 적용한다.** 기본값 `server`가 없는 저장소에서는
  `exit 2`가 나야 한다 — 지금은 `exit 0`이다.
- 여러 경로를 받으면 **하나라도 없으면** `exit 2`다. 어느 경로가 없는지 출력에 담아라.
- `--status-fixture` 경로에도 같은 규칙을 적용하라. **픽스처 파일이 없는데 PASS가 나오면
  그 반증 시험은 시험이 아니다.**

### 하지 마라

- **판정 로직(내용 더러움 vs 표현만 다름)은 건드리지 마라.** 이번 변경은 **입력 검증만**이다.
- **기본 경로 값을 `server`에서 다른 것으로 바꾸지 마라.** 기본값이 바뀌면 매니페스트의
  기존 검사들이 다른 것을 재게 된다. 이번엔 **없으면 2를 낸다**만 더한다.

## 3. 반증 시험 — 실측하고 보고에 적어라

`expectedExit`를 추정으로 적지 마라. 아래를 **직접 돌려** exit code와 출력을 보고에 남겨라.

| 명령 | 지금 | 기대 |
| --- | ---: | ---: |
| `gate-clean server` (이 저장소, 실재·깨끗) | 0 | **0** (회귀 없음) |
| `gate-clean nonexistent-xyz` | **0** | **2** |
| `gate-clean server nonexistent-xyz` (하나만 없음) | ? | **2** |
| `gate-clean --status-fixture docs/qa/gate-witness/gate-clean-dirty.status` | 1 | **1** (회귀 없음) |
| `gate-clean --status-fixture docs/qa/gate-witness/no-such-fixture.status` | ? | **2** |

**픽스처를 만들면 `docs/qa/gate-witness/` 안에 두어라.** 매니페스트 등재는 **조율자가 반입 때**
한다(매니페스트는 영토 밖이라 네가 못 쓴다). **네 몫은 기대값을 실측해 보고에 남기는 것이고,
조율자는 네가 적은 숫자를 그대로 옮긴다.**

## 4. 빌드 — 이번엔 될 것이다

앞선 두 발사(`NET8-01`·`R1`)가 격리 사본에서 빌드하지 못했다. **원인은 인증서가 아니라
`--sandbox workspace-write`에 네트워크가 없는데 worktree에 `server/obj`가 없었던 것**이고,
런처가 이제 **`server/obj`를 사본에 넣어준다**(2026-07-27 수정, 오프라인 `--no-restore` 빌드
exit 0으로 반증).

```
dotnet build server -v q --nologo --no-restore
```

**이것이 `오류 0개`를 내야 한다.** 안 되면 **그 사실을 보고 맨 위에 쓰고 산출물을 내지 마라** —
컴파일되지 않는 패치는 반입할 수 없다. `오류 0개`가 나온 출력 꼬리를 보고에 그대로 붙여라.

## 5. 허용 파일 (allowlist)

- server/Harness/GateCleanCli.cs
- docs/qa/gate-witness/**

작업 보고는 **`docs/qa/gate-witness/GCLEAN-01.md`**에 남겨라.

## 6. 검수 기준

- [ ] `dotnet build server --no-restore` **오류 0개** — 출력 꼬리를 보고에 붙였다
- [ ] `verify-behavior` → `behaviorEqual: true`
- [ ] `measure dev-pack` **violations 0**
- [ ] `scope-check` — 변경 파일이 위 allowlist 안
- [ ] §3 표의 다섯 조합을 **직접 돌려** exit code를 보고에 적었다
- [ ] `gate-clean server`가 **여전히 0**이다 (회귀 없음)
- [ ] `docs/qa/gate-witness/GCLEAN-01.md`에 주체·하네스 결과·참조 스킬·자진 신고 기록

### 목적 기준 (사람 판정)

- **재지 못한 것을 통과로 적지 않는가.** 이것이 이 지시서의 전부다.
- **판정 로직을 안 건드렸는가.** 입력 검증만 더해야 한다.
- **다른 저장소에 겨눠도 성립하는가** — 없는 경로면 2가 나야 한다.

## 7. 작업 보고에 반드시 적을 것

1. **주체** 2. **사용한 하네스와 결과**(명령·exit code) 3. **참조한 스킬**
4. **`## 지표는 만족했으나 목적은 미달인 부분`** — 없으면 "없음"과 근거.

게이트 결과는 JSON 한 줄로: `{"gate":"dev-pack","violations":0,"attempt":1}`
