// 가드레일과 체크포인트 정지 조건을 계산한다.
// 상태 변경과 로그 추가만 처리한다.
import { appendLog, cloneData, computeOverallStatus, sumEstimatedCost, sumSubscriptionCalls } from "./engine.js";

// 반복 횟수와 비용 한도 초과 여부를 평가한다.
export function evaluateGuardrails(definition, state, runLog) {
  const guardrails = definition.guardrails ?? {};
  const maxLoopIterations = Number.isFinite(Number(guardrails.maxLoopIterations))
    ? Number(guardrails.maxLoopIterations)
    : Infinity;
  const maxEstimatedCost = Number.isFinite(Number(guardrails.maxEstimatedCost))
    ? Number(guardrails.maxEstimatedCost)
    : Infinity;
  const maxSubscriptionCalls = Number.isFinite(Number(guardrails.maxSubscriptionCalls))
    ? Number(guardrails.maxSubscriptionCalls)
    : Infinity;
  const totalCost = sumEstimatedCost(runLog);
  const subscriptionCalls = sumSubscriptionCalls(runLog);
  const breaches = [];

  if (Number(state.loopIteration ?? 0) >= maxLoopIterations) {
    breaches.push({
      type: "loopIteration",
      actual: Number(state.loopIteration ?? 0),
      limit: maxLoopIterations,
    });
  }

  if (totalCost >= maxEstimatedCost) {
    breaches.push({
      type: "estimatedCost",
      actual: totalCost,
      limit: maxEstimatedCost,
    });
  }

  if (subscriptionCalls >= maxSubscriptionCalls) {
    breaches.push({
      type: "subscriptionCalls",
      actual: subscriptionCalls,
      limit: maxSubscriptionCalls,
    });
  }

  return {
    breached: breaches.length > 0,
    breaches,
    totalCost,
    subscriptionCalls,
  };
}

// 체크포인트 발동 여부를 평가한다.
export function evaluateCheckpoints(definition, state) {
  const checkpoints = Array.isArray(definition.checkpoints) ? definition.checkpoints : [];

  for (const checkpoint of checkpoints) {
    if (checkpoint.on === "loopIteration" && Number(checkpoint.every) > 0) {
      const loopIteration = Number(state.loopIteration ?? 0);

      if (loopIteration > 0 && loopIteration % Number(checkpoint.every) === 0) {
        return {
          triggered: true,
          checkpoint,
          loopIteration,
        };
      }
    }
  }

  return { triggered: false, checkpoint: null, loopIteration: Number(state.loopIteration ?? 0) };
}

// 가드레일과 체크포인트 결과를 상태에 반영한다.
export function enforceGuardrails(definition, state, runLog) {
  const guardrailEvaluation = evaluateGuardrails(definition, state, runLog);

  if (guardrailEvaluation.breached) {
    return haltForGuardrail(definition, state, runLog, guardrailEvaluation);
  }

  const checkpointEvaluation = evaluateCheckpoints(definition, state);

  if (checkpointEvaluation.triggered) {
    return pauseForCheckpoint(definition, state, runLog, checkpointEvaluation);
  }

  return {
    state,
    runLog,
    evaluation: {
      guardrails: guardrailEvaluation,
      checkpoint: checkpointEvaluation,
    },
    changed: false,
  };
}

// 가드레일 초과 상태를 만든다.
function haltForGuardrail(definition, state, runLog, evaluation) {
  const nextState = cloneData(state);
  const alreadyHalted = nextState.loopState === "halted" && nextState.haltedBy?.type === "guardrail";

  nextState.loopState = "halted";
  nextState.haltedAt = new Date().toISOString();
  nextState.haltedBy = {
    type: "guardrail",
    breaches: evaluation.breaches,
  };
  delete nextState.pausedAt;
  delete nextState.pausedBy;
  nextState.overallStatus = computeOverallStatus(definition, nextState);
  nextState.lastUpdated = new Date().toISOString();

  if (alreadyHalted) {
    return { state: nextState, runLog, evaluation, changed: false };
  }

  return {
    state: nextState,
    runLog: appendLog(runLog, {
      event: "guardrail.halted",
      params: {
        text: evaluation.breaches
          .map((breach) => `${breach.type} ${formatValue(breach.actual)} >= ${formatValue(breach.limit)}`)
          .join(", "),
      },
      level: "warning",
      producedBy: { provider: "guardrails", model: null },
      loopIteration: nextState.loopIteration ?? 0,
      cost: { inputTokens: 0, outputTokens: 0, estimatedUSD: 0, subscriptionCalls: 0, role: "runtime" },
    }),
    evaluation,
    changed: true,
  };
}

// 체크포인트 일시정지 상태를 만든다.
function pauseForCheckpoint(definition, state, runLog, evaluation) {
  const nextState = cloneData(state);
  const checkpointId = evaluation.checkpoint.id;
  const alreadyPaused =
    nextState.loopState === "paused" &&
    nextState.pausedBy?.type === "checkpoint" &&
    nextState.pausedBy?.checkpointId === checkpointId;

  nextState.loopState = "paused";
  nextState.pausedAt = new Date().toISOString();
  nextState.pausedBy = {
    type: "checkpoint",
    checkpointId,
  };
  delete nextState.haltedAt;
  delete nextState.haltedBy;
  nextState.checkpointId = checkpointId;
  nextState.overallStatus = computeOverallStatus(definition, nextState);
  nextState.lastUpdated = new Date().toISOString();

  if (alreadyPaused) {
    return { state: nextState, runLog, evaluation, changed: false };
  }

  return {
    state: nextState,
    runLog: appendLog(runLog, {
      event: "checkpoint.paused",
      params: {
        checkpointId,
        loopIteration: evaluation.loopIteration,
      },
      level: "warning",
      producedBy: { provider: "checkpoints", model: null },
      loopIteration: nextState.loopIteration ?? 0,
      cost: { inputTokens: 0, outputTokens: 0, estimatedUSD: 0, subscriptionCalls: 0, role: "runtime" },
    }),
    evaluation,
    changed: true,
  };
}

// 숫자 표시값을 짧게 만든다.
function formatValue(value) {
  if (!Number.isFinite(value)) {
    return String(value);
  }

  return Number(value.toFixed(4)).toString();
}
