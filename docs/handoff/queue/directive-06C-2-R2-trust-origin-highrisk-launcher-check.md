```context-pack
{
  "diId": "06C-2-R2",
  "requiredInputs": [
    { "path": "docs/directives/_header.md", "sha256": "b37a27f81792e82575a793f671839fdf463895e8ce4d1d4ccf7c5bea1213b2ee" },
    { "path": "docs/verification/_template.md", "sha256": "15f1b6dbdb703c94d6d7259b9417e17f438c980fad25b50b7ed96bc4da354b69" },
    { "path": "docs/plan/wp/TRUST-ORIGIN-BOOTSTRAP.md", "sha256": "fae8ff3b6b649019409c16762b137010e74c8118234ff0bcbae36d610ab41703" },
    { "path": "docs/handoff/queue/directive-06C-2-R1-trust-origin-production-truth.md", "sha256": "a93b349e9d8d4ba1b14021036a3cb066db8511064000a91867c3e16f3e7df775" },
    { "path": "docs/verification/06c2-r1-trust-origin-production-truth.md", "sha256": "f5ffeeff29edf81704f7f48fe4bc0f2343f0be9dd59171960b83603bc3e51014" },
    { "path": "outputs/review/06C-2-R1.codex.md", "sha256": "86d2a854cb491eb96adfacad837d485161169879452ae437226443a9a39351d0" },
    { "path": "server/StateApplierCli.cs", "sha256": "1b7eddf74f9b92e55d749531700cd48d4bfb9358453d5183012f5ff246ae0ce8" }
  ],
  "readOrder": [
    "docs/context/RUNTIME-INDEX.md",
    "outputs/review/06C-2-R1.codex.md",
    "docs/handoff/queue/directive-06C-2-R2-trust-origin-highrisk-launcher-check.md",
    "server/TrustOriginCli.cs",
    "server/StateApplierCli.cs",
    "docs/directives/_header.md"
  ],
  "forbiddenActions": ["git push", "approve", "reject", "import", "spawn-executor", "edit-baseline", "create-trust-origin-record-in-canonical"]
}
```

# 06C-2-R2 — high-risk check and launcher check truth

이 지시서는 docs/directives/_header.md의 불변 제약을 따른다.

- actor: CORE_INFRA_EXECUTOR (sonnet)
- 배경: 06C-2-R1은 R1의 세 반려 조건 대부분을 닫았지만, 독립 Codex 검수에서 두 결함이 남았다.
- 목표: `trust-origin declare`의 선행조건 9·10 검사가 **실제로 검사한 것만 통과**하게 만든다.

## 반려 사유

정본: `outputs/review/06C-2-R1.codex.md`

1. `CheckHighRiskClosed`가 malformed envelope를 넣어 exit 2로 죽는다. 따라서 `trusted-human-receipt-required`에 도달하지 못한다.
2. `CheckAutoLauncherOff`가 malformed `.claude/settings*.json`을 catch 후 skip한다. 확인 불가가 launcher off로 바뀐다.

## 고칠 것

### A. high-risk 세 종류를 실제 full envelope로 확인하라

`CheckHighRiskClosed`는 최소 `PHASE_CHANGE`, `RECOVERY`, `REPLAY` 세 종류를 모두 검사한다.

각 kind마다:

- 임시 request JSON을 만든다.
- 현재 WORKSTATE hash와 request hash를 계산한다.
- `state-transition prepare --transition-id <id> --request <request>`를 통해 정상 envelope/candidate를 만들거나, `StateApplierCli`가 요구하는 필수 필드를 모두 갖춘 full envelope를 만든다.
- 그 envelope의 `transitionKind`를 해당 high-risk kind로 둔다.
- `state-transition apply --envelope <envelope>`가 exit 1을 내고, 오류 내용이 `trusted-human-receipt-required`임을 확인한다.

금지:

- 필수 필드가 빠진 malformed envelope로 exit 2를 받는 것을 성공으로 세지 마라.
- `PHASE_CHANGE` 하나만 검사하지 마라.

### B. launcher settings 파싱 불가를 fail-closed로 바꿔라

`CheckAutoLauncherOff`는 검사 대상 settings 파일이 존재하는데 JSON 파싱이 실패하면 false를 반환해야 한다.

규칙:

- settings 파일 없음 → 그 파일은 skip 가능.
- settings 파일 있음 + valid JSON + hooks 없음 → 그 파일은 통과.
- settings 파일 있음 + valid JSON + hooks 있음 → false.
- settings 파일 있음 + malformed JSON → false.

### C. self-test를 추가하라

추가 필수 case:

| case | 기대 |
| --- | --- |
| `high-risk-full-envelope-phase-change` | full envelope로 PHASE_CHANGE apply → exit 1, trusted-human-receipt-required |
| `high-risk-full-envelope-recovery` | full envelope로 RECOVERY apply → exit 1, trusted-human-receipt-required |
| `high-risk-full-envelope-replay` | full envelope로 REPLAY apply → exit 1, trusted-human-receipt-required |
| `launcher-settings-malformed` | malformed settings가 있으면 `CheckAutoLauncherOff` false 또는 declare exit 1 |

기존 R1 self-test 9개는 유지한다.

## 완료 기준

1. `dotnet build server -c Release -nologo` → exit 0
2. `dotnet run --project server -c Release -- trust-origin --self-test` → exit 0
3. self-test에 high-risk 3종 full envelope case가 있고 모두 PASS
4. malformed launcher settings case가 PASS
5. extra known exception / unlisted failure / ready flags / build-verdict-not-forged R1 회귀 case 유지
6. canonical 저장소에 `docs/handoff/trust-origin/TO-2026-001.json` 생성 없음
7. `WORKSTATE.json`·`WORKSTATE.applier-log.jsonl` blob이 HEAD와 동일
8. `dotnet run --project server -c Release -- measure dev-pack` → violations 0

## 허용 파일 (allowlist)

- server/TrustOriginCli.cs
- docs/verification/06c2-r2-trust-origin-highrisk-launcher-check.md

## 금지

- `server/StateApplierCli.cs` 수정 금지. 읽기만.
- canonical trust-origin record 생성 금지.
- `WORKSTATE.json`·`WORKSTATE.applier-log.jsonl` 수정 금지.
- 기준 파일·측정 코드 수정 금지.
- git commit/push/tag 금지.
- approve/reject/import 금지.

## 보고

`docs/verification/06c2-r2-trust-origin-highrisk-launcher-check.md`를 작성한다.

반드시 적을 것:

- Codex R1 반려 사유 2개가 어떻게 닫혔는지.
- high-risk 3종 full envelope self-test 출력.
- malformed launcher settings self-test 출력.
- R1 회귀 case 유지 여부.
- canonical record 미생성과 WORKSTATE/applier-log 무결성 확인.
- `## 지표는 만족했으나 목적은 미달인 부분`.
