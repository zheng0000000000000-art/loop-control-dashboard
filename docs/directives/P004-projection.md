# P0-04 — Projection 생성기 + WORKSTATE 해시 스탬핑 (보관본)

> 원본: `docs/handoff/queue/directive-P004-projection.md`  
> 보관 날짜: 2026-07-11  
> 상태: 완료(done)

이 파일은 지시서 보관 목적으로 allowlist에 포함된 파일이다.
지시서 원문은 `docs/handoff/queue/directive-P004-projection.md`를 참조한다.

## 요약

- **목표**: `dotnet run --project server -c Release -- handoff-integrity` exit 0
- **수단**: WORKSTATE.json changedFiles sha256 스탬핑 + RUNTIME-INDEX.md/HANDOFF.md 생성 CLI
- **완료 기준**: handoff-integrity PASS, measure dev-pack 비악화, idempotent

## 산출물

- `server/ProjectionCli.cs` — 신규. sha256 스탬핑 + RUNTIME-INDEX.md, HANDOFF.md 생성.
- `server/Cli/CliRouter.cs` — `projection` 분기 추가.
- `docs/context/RUNTIME-INDEX.md` — 생성됨(GENERATED 경고 포함).
- `docs/handoff/HANDOFF.md` — 생성됨(GENERATED 경고 포함).
- `docs/handoff/WORKSTATE.json` — changedFiles sha256 채워짐.
- `docs/verification/p004-projection.md` — 검증 문서.
- `docs/directives/P004-projection.md` — 이 파일(보관).
