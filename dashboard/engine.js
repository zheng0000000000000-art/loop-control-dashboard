// 워크플로우 단계 상태와 전이 계산을 처리한다.
// 도메인 데이터와 표시 문구는 이 파일에서 다루지 않는다.
export const STATUS_VALUES = new Set([
  "not_started",
  "in_progress",
  "completed",
  "passed",
  "warning",
  "blocked",
  "pending_review",
  "approved",
  "failed",
]);

export const BLOCK_KIND_VALUES = new Set(["waiting", "gate_blocked", "failed_upstream"]);
export const LOOP_STATE_VALUES = new Set(["running", "paused", "halted", "aligned"]);
export const MODE_VALUES = new Set(["normal", "degraded"]);

const OVERALL_STATUS_VALUES = new Set([...STATUS_VALUES, ...BLOCK_KIND_VALUES]);
const PRIORITY_STATUS = ["failed", "failed_upstream", "gate_blocked", "warning", "pending_review", "in_progress"];
const DONE_STATUSES = new Set(["completed", "passed", "approved"]);

// JSON 호환 데이터 복사본을 만든다.
export function cloneData(value) {
  return JSON.parse(JSON.stringify(value));
}

// 단계의 현재 상태값을 반환한다.
export function getStageStatus(state, stageId) {
  return state.stages?.[stageId] ?? "not_started";
}

// 단계 정의를 ID로 찾는다.
export function getStage(definition, stageId) {
  return definition.stages.find((stage) => stage.id === stageId) ?? null;
}

// 차단 단계의 부가 정보를 반환한다.
export function getBlockInfo(state, stageId) {
  return state.blockInfo?.[stageId] ?? null;
}

// 차단 단계의 종류를 반환한다.
export function getBlockKind(state, stageId) {
  if (getStageStatus(state, stageId) !== "blocked") {
    return null;
  }

  const kind = getBlockInfo(state, stageId)?.kind;
  return BLOCK_KIND_VALUES.has(kind) ? kind : "waiting";
}

// 사람 검토가 필요한 단계를 찾는다.
export function getHumanReviewStage(definition, state = null) {
  const reviewStages = definition.stages.filter((stage) => stage.requiresHuman === true);

  if (!state) {
    return reviewStages[0] ?? null;
  }

  return (
    reviewStages.find((stage) => getStageStatus(state, stage.id) === "pending_review") ??
    reviewStages[0] ??
    null
  );
}

// 현재 단계 다음 순서의 단계를 반환한다.
export function getNextStage(definition, stageId) {
  const currentIndex = definition.stages.findIndex((stage) => stage.id === stageId);

  if (currentIndex === -1) {
    return null;
  }

  return definition.stages[currentIndex + 1] ?? null;
}

// 단계의 게이트 조건 충족 여부를 계산한다.
export function evaluateGate(definition, state, stageId) {
  const stage = getStage(definition, stageId);
  const conditions = Array.isArray(stage?.gate) ? stage.gate : [];
  const checks = conditions.map((condition) => evaluateGateCondition(state, condition));

  return {
    stageId,
    hasGate: conditions.length > 0,
    passed: checks.every((check) => check.passed),
    checks,
  };
}

// 단계 상태 enum 포함 여부를 확인한다.
export function isKnownStatus(status) {
  return STATUS_VALUES.has(status);
}

// 전체 상태 표시값 포함 여부를 확인한다.
export function isKnownOverallStatus(status) {
  return OVERALL_STATUS_VALUES.has(status);
}

// 실행 로그의 예상 비용 합계를 계산한다.
export function sumEstimatedCost(runLog) {
  return (runLog.entries ?? []).reduce((sum, entry) => {
    return sum + Number(entry.cost?.estimatedUSD ?? 0);
  }, 0);
}

// 실행 로그의 구독 호출 합계를 계산한다.
export function sumSubscriptionCalls(runLog) {
  return (runLog.entries ?? []).reduce((sum, entry) => {
    return sum + Number(entry.cost?.subscriptionCalls ?? 0);
  }, 0);
}

// 실행 로그의 구독 호출 합계를 역할별로 계산한다.
export function sumSubscriptionCallsByRole(runLog) {
  return (runLog.entries ?? []).reduce(
    (sum, entry) => {
      const role = normalizeCostRole(entry.cost?.role);
      sum[role] += Number(entry.cost?.subscriptionCalls ?? 0);
      return sum;
    },
    { runtime: 0, dev: 0 },
  );
}

// 단계 상태와 차단 종류로 전체 상태를 계산한다.
export function computeOverallStatus(definition, state) {
  const statuses = definition.stages.map((stage) => {
    const status = getStageStatus(state, stage.id);
    const blockKind = getBlockKind(state, stage.id);
    return blockKind === "waiting" ? "not_started" : blockKind ?? status;
  });
  const priority = PRIORITY_STATUS.find((status) => statuses.includes(status));

  if (priority) {
    return priority;
  }

  const effectiveStatuses = statuses.filter((status) => status !== "not_started");

  if (effectiveStatuses.length > 0 && effectiveStatuses.every((status) => DONE_STATUSES.has(status))) {
    return "completed";
  }

  return "not_started";
}

// 단일 단계 상태를 갱신한다.
export function applyStageStatus(definition, state, stageId, status) {
  if (!isKnownStatus(status)) {
    throw new Error(`Unknown status: ${status}`);
  }

  const nextState = cloneData(state);
  nextState.stages = { ...(nextState.stages ?? {}), [stageId]: status };
  nextState.blockInfo = { ...(nextState.blockInfo ?? {}) };

  if (status !== "blocked") {
    delete nextState.blockInfo[stageId];
  }

  nextState.currentStage = stageId;
  nextState.overallStatus = computeOverallStatus(definition, nextState);
  nextState.lastUpdated = new Date().toISOString();
  return nextState;
}

// 상태 변경 묶음을 적용하고 전체 상태를 갱신한다.
export function applyStatePatch(definition, state, patch) {
  const nextState = cloneData(state);

  if (patch.currentStage) {
    nextState.currentStage = patch.currentStage;
  }

  if (patch.loopIteration !== undefined) {
    nextState.loopIteration = Number(patch.loopIteration);
  }

  if (patch.loopState !== undefined && LOOP_STATE_VALUES.has(patch.loopState)) {
    nextState.loopState = patch.loopState;

    if (patch.loopState === "running" || patch.loopState === "aligned") {
      delete nextState.haltedAt;
      delete nextState.pausedAt;
      delete nextState.pausedBy;
      delete nextState.haltedBy;
      delete nextState.checkpointId;
    }
  }

  if (patch.mode !== undefined && MODE_VALUES.has(patch.mode)) {
    nextState.mode = patch.mode;
  }

  if (patch.suspendedTracks !== undefined) {
    nextState.suspendedTracks = Array.isArray(patch.suspendedTracks) ? cloneData(patch.suspendedTracks) : [];
  }

  if (patch.stageStatuses) {
    nextState.stages = { ...(nextState.stages ?? {}), ...patch.stageStatuses };
    nextState.blockInfo = { ...(nextState.blockInfo ?? {}) };

    Object.entries(patch.stageStatuses).forEach(([stageId, status]) => {
      if (status !== "blocked") {
        delete nextState.blockInfo[stageId];
      }
    });
  }

  if (patch.blockInfo) {
    nextState.blockInfo = { ...(nextState.blockInfo ?? {}) };

    Object.entries(patch.blockInfo).forEach(([stageId, info]) => {
      if (info === null) {
        delete nextState.blockInfo[stageId];
        return;
      }

      const kind = BLOCK_KIND_VALUES.has(info?.kind) ? info.kind : "waiting";
      nextState.blockInfo[stageId] = { ...info, kind };
    });
  }

  if (patch.overallStatus && isKnownOverallStatus(patch.overallStatus)) {
    nextState.overallStatus = patch.overallStatus;
  } else {
    nextState.overallStatus = computeOverallStatus(definition, nextState);
  }

  nextState.lastUpdated = new Date().toISOString();
  return nextState;
}

// 게이트 차단 상태와 차단 종류를 함께 적용한다.
export function applyGateBlockedPatch(definition, state, patch, stageId) {
  const blockedPatch = cloneData(patch ?? {});
  blockedPatch.stageStatuses = {
    ...(blockedPatch.stageStatuses ?? {}),
    [stageId]: "blocked",
  };
  blockedPatch.blockInfo = {
    ...(blockedPatch.blockInfo ?? {}),
    [stageId]: {
      ...(blockedPatch.blockInfo?.[stageId] ?? {}),
      kind: "gate_blocked",
    },
  };

  return applyStatePatch(definition, state, blockedPatch);
}

// 실행 로그 항목을 표준 형태로 추가한다.
export function appendLog(runLog, entry) {
  const nextRunLog = cloneData(runLog);
  nextRunLog.schemaVersion = 3;
  const normalizedEntry = {
    createdAt: entry.createdAt ?? new Date().toISOString(),
    event: entry.event ?? "unknown.event",
    params: entry.params ?? {},
    level: entry.level ?? "info",
    producedBy: {
      provider: entry.producedBy?.provider ?? "local-dashboard",
      model: entry.producedBy?.model ?? null,
    },
    attempt: Number(entry.attempt ?? 1),
    loopIteration: Number(entry.loopIteration ?? 0),
    cost: {
      inputTokens: Number(entry.cost?.inputTokens ?? 0),
      outputTokens: Number(entry.cost?.outputTokens ?? 0),
      estimatedUSD: Number(entry.cost?.estimatedUSD ?? 0),
      subscriptionCalls: Number(entry.cost?.subscriptionCalls ?? 0),
      role: normalizeCostRole(entry.cost?.role),
    },
  };

  nextRunLog.entries = [...(nextRunLog.entries ?? []), normalizedEntry];
  return nextRunLog;
}

// 단일 게이트 조건을 평가한다.
function evaluateGateCondition(state, condition) {
  if (condition.check === "stageStatus") {
    const actual = getStageStatus(state, condition.stage);
    const allowed = Array.isArray(condition.mustBe) ? condition.mustBe : [];

    return {
      condition,
      actual,
      passed: allowed.includes(actual),
    };
  }

  return {
    condition,
    actual: null,
    passed: false,
  };
}

// 비용 역할 값을 허용 enum으로 정규화한다.
function normalizeCostRole(role) {
  return role === "dev" ? "dev" : "runtime";
}
