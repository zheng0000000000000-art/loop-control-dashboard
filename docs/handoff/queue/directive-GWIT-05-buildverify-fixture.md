```context-pack
{
  "diId": "GWIT-05",
  "requiredInputs": [
    { "path": "docs/directives/_header.md", "sha256": "b37a27f81792e82575a793f671839fdf463895e8ce4d1d4ccf7c5bea1213b2ee" },
    { "path": "docs/verification/_template.md", "sha256": "15f1b6dbdb703c94d6d7259b9417e17f438c980fad25b50b7ed96bc4da354b69" }
  ],
  "readOrder": [
    "docs/context/RUNTIME-INDEX.md",
    "docs/directives/_header.md",
    "docs/handoff/queue/directive-GWIT-05-buildverify-fixture.md",
    "docs/handoff/queue/directive-GWIT-02-fixture-modes.md",
    "server/Harness/BuildVerifyCli.cs"
  ],
  "forbiddenActions": ["commit", "push", "state-transition", "pass", "product-code-edit", "approve", "reject", "import", "edit-baseline"]
}
```

이 지시서는 `docs/directives/_header.md`의 불변 제약을 따른다.
**DI 유형: `harness`** → 반증 시험 필수.

---

# GWIT-05 — `build-verify`에 픽스처 모드를 넣는다 (마지막 남은 코덱스 영역 witness)

- actor: **HARNESS_EXECUTOR (codex)** — `server/Harness/**`는 `ADR-002` 코덱스 배타 영역이다.
- 선행: `GWIT-02`(같은 방식의 선례). 병행: `measure`·`verify-behavior`는 **조율자 몫**(§5).

---

## 0. 문제 (실측 2026-07-26)

반증 없는 검사가 세션 시작 17에서 **5**까지 왔다. 남은 5건은 **세 종류뿐**이다.

```
POST-EXECUTOR  3   build-verify · verify-behavior · measure
LAND           2   build-verify · measure
```

셋 다 **픽스처 모드가 없어** 반증 witness를 선언할 수 없다. `gate-clean`·`doc-integrity`가
같은 처지였고 `GWIT-02`가 픽스처 모드로 풀었다 — **같은 방식을 쓴다.**

**이 지시서는 셋 중 `build-verify` 하나만 다룬다.** 나머지 둘은 구현이 `server/` 루트에 있어
`ADR-002`상 코덱스 영역이 아니다(§5).

## 1. 무엇을 하는가

`build-verify`에 **빌드에 실패하는 픽스처를 지정할 수 있는 모드**를 넣는다.

현재 `BuildVerifyCli.Run`은 `args[1]`로 프로젝트 이름을 받고(기본 `server`) 임시 경로에 복사해
빌드한다. **그 경로에 픽스처 프로젝트를 줄 수 있게 한다.**

```
build-verify --fixture <디렉터리>
  빌드 성공 → exit 0
  빌드 실패 → exit 1   ← 반증 witness가 쓸 자리
  디렉터리가 없거나 프로젝트가 없으면 → exit 2 (fail-closed)
```

- **production 판정 경로를 바꾸지 마라.** 인자가 없거나 기존 형태(`build-verify server`)면
  지금과 똑같이 동작해야 한다. `GWIT-02`가 같은 제약을 걸었고 그대로 지켰다.
- **실제로 빌드해야 한다.** 파일 내용을 읽어 "에러가 있을 것 같다"고 판정하지 마라.

## 1-A. 픽스처를 `docs/qa/gate-witness/`에 만든다

```
build-verify-broken/    컴파일되지 않는 최소 프로젝트 (witness, exit 1 기대)
build-verify-ok/        컴파일되는 최소 프로젝트 (positive, exit 0 기대)
```

- **최소로 만들어라.** 게이트가 매번 도는 검사이므로 빌드가 오래 걸리면 안 된다.
  `.csproj` 하나 + `.cs` 하나면 충분하다.
- 각 픽스처에 **"이 픽스처가 반대 결과를 내기 시작하면 그 검사가 죽은 것이다. 고치지 마라"**를
  README나 주석으로 적어라. `docs/qa/gate-witness/stale-pin-directive.md`가 그 형식의 선례다.

## 2. 하지 않을 일 (하면 반려)

- production 판정 경로 변경.
- `server/` 루트 파일 수정 — **영역 밖.** `measure`·`verify-behavior`는 조율자가 한다.
- `GATE-MANIFEST.json` 수정 — 영역 밖. 조율자 후속이다.
- 빌드를 실제로 돌리지 않고 파일 내용으로 판정하는 것.
- **`docs/qa/gate-witness/`의 기존 파일 수정.** `stale-pin-directive.md`·`gate-clean-*.status`·
  `doc-integrity-mismatch/`·`require-failure-witness.json`은 이미 다른 검사의 witness다.
  **새 디렉터리 2개만 추가하라.** allowlist가 디렉터리 단위라 기술적으로는 열려 있지만 범위 밖이다.
- 깨진 픽스처를 `server/` 빌드에 끼워 넣는 것. **본 빌드가 깨지면 안 된다** — 픽스처는
  격리된 경로에서만 빌드돼야 한다.

## 3. 완료 조건

### 지표 기준 (기계 판정)

- [ ] `build-verify` (인자 없음) **exit 0** — 회귀 없음
- [ ] `build-verify --fixture docs/qa/gate-witness/build-verify-broken` **exit 1**
- [ ] `build-verify --fixture docs/qa/gate-witness/build-verify-ok` **exit 0**
- [ ] `build-verify --fixture <없는 경로>` **exit 2**
- [ ] 픽스처 실행이 본 저장소 빌드 산출물을 오염시키지 않는다(`git status` 변경 0)

### 목적 기준 (사람 판정)

**"이 검사가 빌드 실패를 실패로 보고한다는 것을 매니페스트로 증명할 수 있다."**

지표만 만족시키는 우회로: 픽스처 경로 이름이 `broken`이면 1을 내는 것. **입력을 보지 않으면
아무것도 증명하지 못한다.** 그래서 §4 시험 3을 둔다.

## 4. 반증 시험 (전부 실측. **코드 검토로 갈음하지 마라**)

| # | 시험 | 기대 |
| --- | --- | --- |
| 1 | broken 픽스처 | **exit 1** |
| 2 | ok 픽스처 | **exit 0** |
| 3 | **broken 픽스처의 소스를 고쳐 컴파일되게 만든다(이름은 그대로)** | **exit 0** — 내용을 본다는 증명 |
| 4 | 없는 경로 | **exit 2** |
| 5 | 인자 없이 실행 | production과 동일 |
| 6 | 픽스처 실행 후 `git status --porcelain` | **변경 0** |

**시험 3이 이 지시서의 목적 자체다.** `GWIT-02`의 시험 3과 같은 성질이며, 그때 실측으로
통과했다(픽스처 이름은 그대로 두고 내용만 비우니 exit가 뒤집혔다). **출력 원문을 붙여라.**

## 5. 이 지시서가 다루지 않는 둘 — 조율자 몫

```
measure           server/Cli/CliRouter.cs (RunMeasureCli)   ← server/ 루트
verify-behavior   server/BehaviorSnapshotCli.cs             ← server/ 루트
```

**`ADR-002`상 코덱스 영역이 아니므로 `CodexHarnessLauncher`가 거절한다**
(`allowed-paths-outside-codex-territory`). 같은 지시서에 넣으면 오늘 `CODEX-GATE-04`가
그랬듯 **어느 실행자도 수행할 수 없는 지시서**가 된다. 그래서 나눴다.

조율자가 같은 방식(픽스처 모드 + `docs/qa/gate-witness/` 픽스처)으로 별도 수행한다.

## 6. 후속 (이 지시서가 하지 않는다)

조율자가 ①`POST-EXECUTOR`·`LAND`에 `build-verify` witness를 등재하고 ②`measure`·`verify-behavior`를
직접 처리한 뒤 ③`LAND`에 `requireFailureWitness`를 켠다. **①②가 끝나기 전에 ③을 하지 마라** —
그 순간 영구히 빨개진다(`FAIL-2026-010`).

## 착륙 (두 걸음 — `skills/common/directive-authoring.md` §7)

착륙 전 `crossDirectivePinCollisions`를 확인하라. **소유 지시서를 가정하지 말고 stale 경로를
직접 grep해서 찾아라** — 2026-07-26에 가정했다가 한 번 틀렸다.

## 허용 파일 (allowlist)

- server/Harness/BuildVerifyCli.cs
- docs/qa/gate-witness/

> 검증 문서는 영역 밖이라 조율자가 쓴다.
