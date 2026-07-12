# STATE-01 — WORKSTATE canonical 계약 + StateApplierCli 신설

```context-pack
{
  "diId": "STATE-01",
  "requiredInputs": [
    { "path": "docs/context/RUNTIME-INDEX.md" },
    { "path": "docs/handoff/WORKSTATE.json" },
    { "path": "server/ProjectionCli.cs" },
    { "path": "docs/plan/INTENT-DIGEST.md" }
  ]
}
```

## 목적

독립 재개 시험 FAIL 원인 제거. 원인: 상태를 전이시키는 주체가 없어서 WORKSTATE가 실제 상태와 영구적으로 불일치했다.

## 허용 파일 (allowlist)

- server/StateApplierCli.cs
- server/ProjectionCli.cs
- server/Cli/CliRouter.cs
- docs/handoff/WORKSTATE.json
- docs/context/RUNTIME-INDEX.md
- docs/handoff/HANDOFF.md
- docs/verification/state01-applier.md
- docs/directives/STATE01-applier.md

## 구현 완료 내용

1. **WORKSTATE v9 canonical 마이그레이션**: schemaVersion=3, phaseId=P00, wpId=WP-00, blockers=[] 신설
2. **ProjectionCli.cs**: blocker(단수)→blockers[](복수), L0 테이블에 blockers/nextActions 행 추가
3. **StateApplierCli.cs 신규**: WORKSTATE 유일 writer. 9가지 검증 규칙 강제.
4. **CliRouter.cs**: state-transition 명령 배선

## 검수 기준

1. build exit 0, warning 0
2. 반증 시험 9개
3. 정상 전이 1회 + RESUME-01 L0 답 가능
4. measure dev-pack violationCount 0
5. handoff-integrity exit 0
6. 파일 다 쓴 뒤 마지막에 projection 실행
