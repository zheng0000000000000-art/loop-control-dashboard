# 게이트 증거 — 처분이 의존하는 판정 보고

`outbox/codex-launch-*/disposition.json`의 `gateReport`가 가리키는 파일들이다.

**여기 있는 이유**: `outputs/`는 `.gitignore`의 `outputs/*`로 제외된다.
2026-07-26에 처분 17건이 전부 `outputs/gates/`를 가리키고 있었고,
**새로 클론하면 17건 전부 `gate-report-not-found`가 났다.**
기록이 저장소와 함께 이동하지 않으면 그 기록은 내 트리에서만 참이다.

**규칙**

- `gateReport`는 **추적되는 경로**를 가리켜야 한다. `outputs/` 아래를 가리키지 마라.
- 새 보고는 `program-verify ... --out docs/handoff/gate-evidence/<launchId>.gate.json`으로 낸다.
  `--out`은 검사가 끝난 뒤에 쓴다 — 셸 리다이렉트는 자기참조 충돌을 일으킨다.
- **처분을 먼저 쓰고 보고를 내면 첫 실행이 `gate-report-not-found`로 실패한다.**
  순서는 보고 → 처분 → 다시 재서 확인이다.
- `outputs/gates/`는 임시 실행 자리다. 여기로 옮기지 않은 것은 기록이 의존하지 않는 것이다.

**지우지 마라.** 하나라도 없어지면 `launch-disposition`이 그 발사를 위반으로 센다.
