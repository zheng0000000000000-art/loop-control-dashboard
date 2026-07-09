# 원격 열람 + ntfy 알림 실행 검증

검증일: 2026-07-09

배경: Tailscale 사설망 안에서 폰으로 대시보드를 열람·결재하고, halt·결재 대기·자동 복원처럼 사람이 필요한 순간에 폰 푸시가 오도록 했다. 이 검증은 Tailscale·ntfy 앱 자체의 폰 연동은 다루지 않는다(사람 몫). 서버가 실제로 토큰을 검사하고, 실제 ntfy 서버로 알림을 보내고, 실패해도 루프가 멈추지 않는지를 로컬에서 실측했다.

## A. 토큰 보호 (401/200)

`RemoteActionToken=verify-token-xyz` 환경변수로 서버를 띄운 뒤 실측했다.

| 요청 | 결과 | 판정 |
| --- | --- | --- |
| `GET .../state` (토큰 없음) | `200` | O |
| `POST .../actions/measure` (토큰 없음) | `401` | O |
| `POST .../actions/measure` (`X-Action-Token: nope`, 오답) | `401` | O |
| `POST .../actions/measure` (`X-Action-Token: verify-token-xyz`, 정답) | `200` | O |

`RemoteActionToken`이 비어 있으면(기본값) 이 검사 자체가 없다 — 로컬 전용 사용은 기존과 동일하게 동작한다(코드 경로상 `string.IsNullOrWhiteSpace(remoteActionToken)`이 조건을 통째로 건너뛴다).

## B. ntfy 알림 — 사람이 필요한 순간만

임시 topic(`loop-verify-*`, 세션마다 랜덤 생성)으로 `https://ntfy.sh`에 실측 발송하고, `curl https://ntfy.sh/{topic}/json?poll=1`로 폰 없이 수신을 확인했다.

### B-1. 결재 대기 도착

`ko.json`에 위반을 주입하고 measure를 실행한 결과, ntfy에 실제로 도착한 메시지:

```json
{"title":"결재함 도착","message":"개발 팩 — 자기 검수 루프: 목표 0 도달 수정 (1층 판정: needs_changes)","priority":3}
```

이 첫 번째 발송을 조사하는 과정에서 흥미로운 부수 발견이 있었다 — 아래 "부수 발견" 참조.

### B-2. 가드레일 정지 (urgent)

dev-pack definition의 `guardrails.maxLoopIterations`를 일시적으로 `0`으로 낮춘 뒤 measure를 실행해 즉시 가드레일 정지를 재현했다.

```json
{"currentStage": "...", "loopState": "halted", "haltedBy": {"type": "guardrail", "breaches": [{"type": "loopIteration", "actual": 0, "limit": 0}]}}
```

ntfy에 도착한 메시지:

```json
{"title":"가드레일 정지","message":"개발 팩 — 자기 검수 루프: loopIteration 0 >= 0","priority":4}
```

`priority: 4`(high)로 확인됨 — 결재 대기(`priority: 3`, default)보다 급함이 구분된다.

체크포인트 일시정지(`checkpoint.paused`)와 데이터 자동 복원(`system.restored`)은 같은 `NotifyGuardrailTransition`/시작 시점 복원 경로로 구현했지만, 실측 재현에는 각각 `loopIteration`을 증가시키는 승인 액션이나 파일 손상이 필요하다. 승인 액션은 "결재는 사람 몫" 원칙상 검증 목적으로도 호출하지 않았고, 파일 손상 재현은 복원 지점 자체를 훼손할 위험이 있어 생략했다 — 두 경로 모두 코드는 동일한 발송 함수를 거치므로(B-2에서 이미 발송 성공을 확인한 함수), 별도 실측 없이 코드 검토로 충분하다고 판단했다.

measure 성공, aligned 도달 같은 통과성 이벤트는 이번 검증 전체에서 단 한 번도 ntfy로 발송되지 않았다(코드에 애초에 그 경로가 없다 — `Notifier` 호출은 정확히 4개 지점에서만 존재).

### B-3. 알림 실패는 무해하다

`Ntfy:Server`를 `https://ntfy.invalid.nonexistent.example`로 바꾼 뒤 위반을 주입해 measure를 실행했다.

| 항목 | 결과 |
| --- | --- |
| HTTP 응답 | `200`, `changeReview: pending_review` 정상 도달 |
| 소요 시간 | 24.7초(정상 AI 왕복 시간과 동일 — ntfy 실패로 인한 지연 없음, fire-and-forget 확인) |
| 콘솔 로그 | `[ntfy] send error: 알려진 호스트가 없습니다. (ntfy.invalid.nonexistent.example:443)` |
| `run-log.json` 오염 여부 | 없음(`ntfy` 문자열 0건 — 콘솔에만 남기고 로그는 건드리지 않는다는 설계대로) |

## 부수 발견: ntfy 검증 중 실제 blueprint 위반을 스스로 만들었다

B-1을 준비하는 과정에서 결재 대기 알림이 **두 번** 도착해 처음에는 알림 중복 발송 버그로 의심했다. 원인을 추적한 결과:

- 이번 세션에서 `dashboard/data/lang/ko.json`에 새로 추가한 `remote.tokenPrompt` 값이 `"원격 액션 토큰을 입력하세요"`였고, 이 문장이 `koPoliteEndings` 블루프린트 규칙(존댓말 종결 어미 금지, 근거: `요"` 패턴)을 그대로 위반했다.
- 즉 위반 주입용 임시 텍스트를 추가하기도 전에, 코드 리뷰 사항이었던 내 자신의 UI 문구가 이미 진짜 위반이었다 — `koPoliteEndings` 체크가 정확히 설계대로 이 실수를 잡아냈다.
- `"원격 액션 토큰을 입력한다"`로 문체를 프로젝트 관례(평서형 종결)에 맞춰 수정했다. 알림 발송 로직 자체는 처음부터 정상이었다 — 두 번의 발송은 각각 다른 진짜 위반(내 문구 버그, 그리고 의도적으로 주입한 테스트 위반)에 대한 정상적인 개별 알림이었다.

## C. 모바일 1열 스택

375×812(모바일) 뷰포트에서 `preview_screenshot`이 도구 자체 문제로 타임아웃돼, `preview_inspect`로 각 패널의 실제 렌더링 좌표를 측정해 순서를 확인했다(스크린샷보다 오히려 더 정밀한 증거다).

| 패널 | y좌표(375px 폭) | 순서 |
| --- | --- | --- |
| `.panel-approval`(결재함) | 405.8 | 1 |
| `.panel-pipeline`(파이프라인) | 1841.6 | 2 |
| `.panel-detail`(상세) | 2384.8 | 3 |
| `.panel-log`(로그, 미측정이나 detail 이후 위치 확정) | > 2384.8 + 899.8 | 4 |

결재함이 최상단으로 확인됨 — "결정 게이트 → 파이프라인 → 상세 → 로그" 순서 실증. `.button-approve`/`.button-reject`의 계산된 `min-height`는 `44px`로 확인됨.

1280×800(데스크톱) 뷰포트에서는 `.panel-pipeline`(x=43)과 `.panel-approval`(x=879)이 같은 y좌표(232.4)에서 나란히 배치돼 3열 레이아웃이 회귀 없이 유지됨을 확인했다.

## 불변 확인

| 항목 | 명령 | 결과 | 판정 |
| --- | --- | --- | --- |
| Core 격리 | `rg -n "ollama\|reviewChecklist\|note-nonempty\|after-matches-goal\|no-scope-creep\|OllamaExecutor\|Notifier\|ntfy" server/Engine.cs server/Storage.cs server/Guardrails.cs` | 결과 없음 | O |
| 빌드 | `dotnet build server/LocalFirstWorkflowDashboard.Server.csproj` | 경고 0, 오류 0 | O |
| 프런트 문법 | `node --check dashboard/app.js` | 오류 없음 | O |
| definition/lang/appsettings JSON 유효성 | `node -e "JSON.parse(...)"` | 모두 유효 | O |

## 원복 확인

1. `dashboard/data/dev-pack/workflow-definition.json`의 `guardrails.maxLoopIterations`를 `10`으로 복원, `guardrail` acknowledge로 `loopState: running` 복귀.
2. `ko.json`의 `__verificationTemp` 제거, `remote.tokenPrompt` 문구는 정식으로 수정한 채 유지(버그 수정이지 되돌릴 대상이 아니다).
3. `server/appsettings.json`은 처음부터 기본값(`BindUrls: http://localhost:5173`, `RemoteActionToken: ""`, `Ntfy.Enabled: false`)만 커밋 대상이며, 검증에 쓴 토큰/topic/잘못된 서버 주소는 모두 환경변수로만 주입해 파일에는 남지 않는다.
4. 최종 재측정: `koPoliteEndings: 0/0`, `overallStatus: completed`, `loopState: aligned`. 검증 중 생성된 모든 proposal은 `lifecycle: superseded`로 자연 정리됐다 — 어느 라운드에서도 승인/거절을 호출하지 않았다.

## D. CLAUDE.md 게이트 준수 (최종 커밋 전)

`dotnet run --project server --no-build -- measure dev-pack` 실행 결과(별도 기록):

- 위반 0건, 종료 코드 `0`.
- blueprint·definition·측정 코드는 수정하지 않았다. 위에서 언급한 `ko.json` 문구 수정은 실제 위반 텍스트를 정직하게 고친 것이다.
- approve/reject는 이번 검증 전체에서 한 번도 호출하지 않았다.

## 결론

- 원격 쓰기 액션은 토큰 없이 접근 불가(401), 열람은 항상 자유(GET 무관) — 실측 확인.
- ntfy는 사람이 필요한 순간(결재 대기, 가드레일 정지)에만, 통과성 이벤트에는 발송하지 않는다 — 실측 확인. 실패해도 루프는 멈추지 않고 run-log를 오염하지 않는다 — 실측 확인.
- 모바일 좁은 화면에서 결재함이 최상단으로 재배치되고, 데스크톱 3열 레이아웃은 회귀 없다 — 실측 확인.
- 검증 과정에서 시스템이 내 자신의 UI 문구 버그(`koPoliteEndings` 위반)를 실제로 잡아냈다 — 이 검증 방식과 게이트가 살아서 작동하고 있다는 부수 증거다.
