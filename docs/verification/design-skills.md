# 디자인 게이트 + 스킬 1세대 실행 검증

검증일: 2026-07-09

배경: ① 디자인(색·인라인 style·터치 타겟·폰트)을 blueprint 측정·관례로 편입했다. ② 이 저장소의 verification 문서·CLAUDE.md·README에 흩어져 있던 실제 작업 노하우를 `skills/` 문서 체계로 승격했다. 이 검증 자체가 `skills/directive-writing.md`·`skills/verification.md`의 첫 자기 적용이다(아래 "스킬 자기 적용" 절 참조).

## 시행착오: 첫 주입 대상이 틀렸다 (verification 스킬의 3번 절차 그대로)

계획대로라면 D-2에서 인라인 style을 주입해 감지를 확인하면 됐지만, 그전에 **A-3(기존 코드 첫 감사)에서 이미 진짜 위반을 하나 발견했다** — 아래 참조. 새로 지어낸 함정은 아니고, 실제 measure 실행에서 나온 결과다.

## A. 디자인 지표 4종 + skillsWithoutVersion 측정 동작

`dev-pack/blueprint.json`에 5개 항목 추가, `DevPackMeasures.cs`에 대응 검사 구현. 최초 measure 실측:

| metricId | 값 | 판정 |
| --- | --- | --- |
| `hardcodedColors` | 0 | 통과 |
| `inlineStyles` | 0 | 통과 |
| `smallTouchTargets` | **2** | **위반** |
| `newFontFamilies` | 0 | 통과 |
| `skillsWithoutVersion` | 0 (당시 `skills/` 폴더 없음 → "폴더 없음"으로 0 처리) | 통과 |

### A-3. 기존 코드 첫 감사 결과 (수정하지 않음)

`smallTouchTargets = 2`, evidence:
- `dashboard/style.css:260` — `.button { min-height: 38px; ... }`
- `dashboard/style.css:271` — `.button-compact { min-height: 32px; ... }`

지시대로 **고치지 않았다.** 측정 직후 기존 흐름대로 proposal이 자동 생성돼 결재 대기로 들어갔다(`proposal-1783590959772`, title "터치 타겟 크기 수정", `qwen3:8b` 생성 → `qwen3:14b` 검토 승인 → 사람 결재 대기). 이후 검증 세션 내내 이 proposal을 승인·거절하지 않았다 — 수정 여부는 사람 몫으로 남겨둔다.

## B. 위반 주입 → 감지 → 원복 (verification 스킬 절차)

`dashboard/index.html`의 `#schemaWarning`에 `style="color: red"`를 임시로 추가했다.

| 단계 | 결과 |
| --- | --- |
| 주입 후 measure | `inlineStyles: 1`, evidence `dashboard/index.html:38` |
| 판정 | `violationCount: 2`(기존 `smallTouchTargets` 1건 + 신규 `inlineStyles` 1건) |
| **교차 검증(예상 밖의 결과, 원인 추적)** | 이 위반은 **일반 위반이 아니라 "악화"로 분류돼 직전 작업(악화 감지+롤백)의 롤백 경로로 자동 라우팅됐다.** proposal `kind: "rollback"`, title "악화 롤백 제안: inlineStyles", `createdBy: rule-engine`(Ollama 미호출), note "직전 승인 원인 미상 이후 inlineStyles이 0→1로 악화됨." — `inlineStyles`가 직전 측정에서 충족(0)이었다가 위반(1)으로 바뀌었기 때문에, 새로 만든 디자인 지표가 기존 악화 감지 시스템과 자동으로 맞물린 것이다. 별도 코드 없이 두 기능이 통합됐다. |
| 원복 후 measure | `inlineStyles: 0`, `track.resumed` 로그 발생, `violationCount: 1`(기존 `smallTouchTargets`만 남음) |

## C. 스킬 자기 적용

이 검증 자체가 `skills/directive-writing.md`의 절차를 따랐다:
1. 컨텍스트 확인(관례 파일·blueprint 게이트가 이미 동작 중임을 전제로 확인) → 완료.
2. 불변 제약 추출(코어 3파일 청결, 한국어 기능 주석, 결재는 사람 몫) → 완료, 아래 불변 확인 참조.
3. 완료 기준을 검증 명령으로 번역(이 문서의 각 절이 그 결과) → 완료.
4. `skills/verification.md`의 절차대로 실제 실행 검증(기준선 → 주입 → 감지 → 원복) → 완료, B절.
5. 관례 게이트 통과 후 커밋 → 아래 참조.

`skills/verification.md`의 "안 되면 사실대로 기록" 원칙도 그대로 지켰다 — B절의 예상 밖 결과(롤백 경로로 라우팅)를 추측 없이 run-log·proposal 필드로 추적해 실제 원인(직전 측정 대비 충족→위반 전환)을 밝혔다.

## 관례 파일 라우팅

`CLAUDE.md`·`AGENTS.md`의 "관례" 절에 스킬 라우팅 한 줄을 추가했다: 작업 시작 전 `/skills/`에서 해당 스킬을 찾아 절차를 따르고, 없으면 새 스킬 초안을 "제안"만 하고 직접 커밋하지 않는다(스킬 추가·수정은 사람 결재).

## 불변 확인

| 항목 | 명령 | 결과 | 판정 |
| --- | --- | --- | --- |
| Core 격리 | `rg -n "ollama\|reviewChecklist\|note-nonempty\|after-matches-goal\|no-scope-creep\|OllamaExecutor\|Notifier\|ntfy\|hardcodedColors\|inlineStyles\|smallTouchTargets\|skillsWithoutVersion" server/Engine.cs server/Storage.cs server/Guardrails.cs` | 결과 없음 | O |
| 빌드 | `dotnet build server/LocalFirstWorkflowDashboard.Server.csproj` | 경고 0, 오류 0 | O |
| 프런트 문법 | `node --check dashboard/app.js` | 오류 없음 | O |
| JSON 유효성 | blueprint·lang | 모두 유효 | O |
| 스킬 형식 통일 | `grep -l "버전:" skills/*.md` | 4개 파일 모두 매칭 | O |
| 스킬 수정 = 사람 결재 | `CLAUDE.md`/`AGENTS.md` 라우팅 문구에 "직접 커밋하지 말고 ... 사람 결재" 명시 | O | O |

## CLAUDE.md 게이트 준수 (최종 커밋 전 — 예외 상황 정직 기록)

`dotnet run --project server --no-build -- measure dev-pack` → `{"violationCount":1,...}`, 종료 코드 `1`.

CLAUDE.md는 원칙적으로 "위반 0"을 게이트 기준으로 삼지만, **이번 지시서가 A-3에서 명시적으로 "기존 위반을 고치지 말라"고 지정했다** — 즉 `smallTouchTargets` 1건은 알려진, 문서화된, 사람 결재 대기 중인 예외다. blueprint·definition·측정 코드는 수정하지 않았고, approve/reject는 이번 검증 전체에서 한 번도 호출하지 않았다. 남은 위반 목록은 위 A-3에 그대로 기록했다 — 숨기지 않는다.

## 결론

- 디자인 지표 4종이 실제로 동작하고, 첫 감사에서 진짜 위반(`smallTouchTargets` 2건)을 찾아냈다 — 수정하지 않고 결재함으로 넘겼다.
- `skills/` 4문서가 형식을 통일해 존재하며, 관례 파일이 이를 라우팅한다. 스킬 추가·수정은 사람 결재로 명시했다.
- 위반 주입→감지→원복 검증에서 새 디자인 지표가 기존 악화 감지 시스템과 자동으로 통합되는 것을 확인했다(설계하지 않은 긍정적 부수 효과).
- 코어 3파일은 청결하게 유지됐다.
