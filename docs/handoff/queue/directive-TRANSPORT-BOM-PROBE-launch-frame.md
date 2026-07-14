```context-pack
{
  "diId": "TRANSPORT-BOM-PROBE",
  "requiredInputs": [
    { "path": "docs/directives/_header.md", "sha256": "b37a27f81792e82575a793f671839fdf463895e8ce4d1d4ccf7c5bea1213b2ee" }
  ],
  "readOrder": [
    "docs/directives/_header.md",
    "docs/handoff/queue/directive-TRANSPORT-BOM-PROBE-launch-frame.md"
  ],
  "forbiddenActions": ["git push", "approve", "reject", "import", "edit-baseline"]
}
```

# TRANSPORT-BOM-PROBE — launch frame smoke

이 지시서는 docs/directives/_header.md의 불변 제약을 따른다.

- actor: CORE_INFRA_EXECUTOR (sonnet)
- 목표: launcher stdin stream-json frame이 BOM 없이 도착하는지 확인한다.

## 할 일

파일을 수정하지 말고 첫 줄에 정확히 `ACK-TRANSPORT-BOM-PROBE`를 출력한다.

## 완료 기준

- 출력에 `ACK-TRANSPORT-BOM-PROBE`가 있다.
- 파일 수정 없음.

## 허용 파일 (allowlist)

- docs/verification/transport-bom-probe-none.md

## 금지

- 파일 수정 금지.
- git commit/push/tag 금지.
- approve/reject/import 금지.
