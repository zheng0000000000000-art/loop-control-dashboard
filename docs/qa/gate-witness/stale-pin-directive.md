```context-pack
{
  "diId": "WITNESS-STALE",
  "requiredInputs": [
    { "path": "docs/directives/_header.md", "sha256": "0000000000000000000000000000000000000000000000000000000000000000" }
  ],
  "readOrder": ["docs/directives/_header.md"],
  "forbiddenActions": ["commit", "push"]
}
```

# WITNESS-STALE — `context-pack-integrity`의 반증 witness (지시서가 아니다)

**이 파일은 실행 대상이 아니다.** `GWIT-01`이 요구하는 반증 witness다 —
`context-pack-integrity`가 stale한 pin을 실제로 **실패로 보고하는지** 증명하기 위해
sha256을 일부러 0으로 채웠다.

이 파일이 통과하기 시작하면 그 검사가 죽은 것이다. **고치지 마라.**

## 허용 파일 (allowlist)

- docs/qa/gate-witness/stale-pin-directive.md
