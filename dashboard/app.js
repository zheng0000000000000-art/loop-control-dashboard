// 대시보드 데이터 로딩과 화면 렌더링을 처리한다.
// 사용자 액션을 메모리 상태에 반영한다.
import {
  appendLog,
  applyGateBlockedPatch,
  applyStageStatus,
  applyStatePatch,
  cloneData,
  evaluateGate,
  getBlockKind,
  getStage,
  getStageStatus,
  getHumanReviewStage,
  getNextStage,
  sumEstimatedCost,
  sumSubscriptionCallsByRole,
} from "./engine.js";
import { enforceGuardrails } from "./guardrails.js";

const EXPECTED_SCHEMA_VERSION = 2;
const RUN_LOG_SCHEMA_VERSION = 3;
const SCENARIO_SCHEMA_VERSION = 3;
const POLL_INTERVAL_MS = 5000;
const PROJECTS_PATH = "./data/projects.json";
const LANGUAGE_PATHS = {
  ko: "./data/lang/ko.json",
  en: "./data/lang/en.json",
};
const PROJECT_FILE_NAMES = {
  definition: "workflow-definition.json",
  state: "workflow-state.json",
  runLog: "run-log.json",
  proposal: "patch-proposal.json",
  scenario: "scenario.json",
  reviewReport: "review-report.json",
  blueprint: "blueprint.json",
  measurement: "measurement.json",
};
const DEFAULT_LANGUAGE = "ko";
const RISK_ORDER = ["low", "medium", "high"];

const elements = {
  productEyebrow: document.querySelector("#productEyebrow"),
  projectName: document.querySelector("#projectName"),
  projectSelectLabel: document.querySelector("#projectSelectLabel"),
  projectSelect: document.querySelector("#projectSelect"),
  pipelineMinimap: document.querySelector("#pipelineMinimap"),
  schemaWarning: document.querySelector("#schemaWarning"),
  cycleSummary: document.querySelector("#cycleSummary"),
  overallStatus: document.querySelector("#overallStatus"),
  regressedBadge: document.querySelector("#regressedBadge"),
  unattendedBadge: document.querySelector("#unattendedBadge"),
  inboxMenu: document.querySelector("#inboxMenu"),
  inboxBadge: document.querySelector("#inboxBadge"),
  inboxDropdown: document.querySelector("#inboxDropdown"),
  totalCostLabel: document.querySelector("#totalCostLabel"),
  totalCost: document.querySelector("#totalCost"),
  subscriptionCallsLabel: document.querySelector("#subscriptionCallsLabel"),
  subscriptionCalls: document.querySelector("#subscriptionCalls"),
  languageToggle: document.querySelector("#languageToggle"),
  themeToggle: document.querySelector("#themeToggle"),
  replayScenario: document.querySelector("#replayScenario"),
  downloadJson: document.querySelector("#downloadJson"),
  pipelineEyebrow: document.querySelector("#pipelineEyebrow"),
  pipelineTitle: document.querySelector("#pipelineTitle"),
  stageList: document.querySelector("#stageList"),
  detailEyebrow: document.querySelector("#detailEyebrow"),
  detailTitle: document.querySelector("#detailTitle"),
  stageDetail: document.querySelector("#stageDetail"),
  approvalEyebrow: document.querySelector("#approvalEyebrow"),
  approvalTitle: document.querySelector("#approvalTitle"),
  approvalPanel: document.querySelector("#approvalPanel"),
  logEyebrow: document.querySelector("#logEyebrow"),
  logTitle: document.querySelector("#logTitle"),
  runLog: document.querySelector("#runLog"),
  logCount: document.querySelector("#logCount"),
};

let translations = {};
let language = DEFAULT_LANGUAGE;
let theme = getInitialTheme();
let projectsConfig = null;
let activeProject = null;
let definition = null;
let workflowState = null;
let runLog = null;
let proposal = null;
let scenario = null;
let reviewReport = { schemaVersion: EXPECTED_SCHEMA_VERSION, reports: [] };
let blueprint = { schemaVersion: EXPECTED_SCHEMA_VERSION, items: [] };
let measurement = null;
let cycleSummary = null;
let projectBaseline = null;
let schemaWarnings = [];
let selectedStageId = null;
let editingChangeIndex = null;
let scenarioTimer = null;
let scenarioRunning = false;
let measureRunning = false;
let reviewActionRunning = false;
let serverBacked = false;
let pollTimer = null;
let actionToken = null;
let globalInbox = { schemaVersion: EXPECTED_SCHEMA_VERSION, items: [], autoApprovals: [] };

initialize();

// 초기 데이터와 이벤트 바인딩을 준비한다.
async function initialize() {
  try {
    setTheme(theme);

    const [ko, en, loadedProjects] = await Promise.all([
      fetchJson(LANGUAGE_PATHS.ko),
      fetchJson(LANGUAGE_PATHS.en),
      fetchJson(PROJECTS_PATH),
    ]);

    translations = { ko, en };
    projectsConfig = loadedProjects;
    bindEvents();
    applyLanguage();
    populateProjectSelect();

    const initialProjectId = projectsConfig.lastOpened ?? projectsConfig.projects?.[0]?.id;
    await loadProject(initialProjectId);
    startPolling();
  } catch (error) {
    renderLoadError(error);
  }
}

// 필수 JSON 파일을 불러온다.
async function fetchJson(path) {
  const response = await fetch(path);

  if (!response.ok) {
    throw new Error(`Could not load ${path}: ${response.status}`);
  }

  return response.json();
}

// 선택 JSON 파일을 불러오고 없으면 대체값을 반환한다.
async function fetchOptionalJson(path, fallback) {
  const response = await fetch(path);

  if (!response.ok) {
    if (response.status === 404) {
      return fallback;
    }

    throw new Error(`Could not load ${path}: ${response.status}`);
  }

  const text = await response.text();

  if (!text.trim()) {
    return fallback;
  }

  return JSON.parse(text);
}

// 화면 입력 이벤트를 연결한다.
function bindEvents() {
  elements.projectSelect.addEventListener("change", (event) => {
    loadProject(event.target.value).catch(renderLoadError);
  });
  elements.languageToggle.addEventListener("click", () => {
    language = language === "ko" ? "en" : "ko";
    applyLanguage();
    render();
  });
  elements.themeToggle.addEventListener("click", () => {
    setTheme(theme === "dark" ? "light" : "dark");
    applyLanguage();
  });
  elements.replayScenario.addEventListener("click", replayScenario);
  elements.downloadJson.addEventListener("click", downloadWorkspaceJson);
  elements.regressedBadge.addEventListener("click", () => {
    elements.approvalPanel.scrollIntoView({ behavior: "smooth", block: "start" });
  });
  elements.inboxBadge.addEventListener("click", () => {
    elements.inboxDropdown.hidden = !elements.inboxDropdown.hidden;
  });
  document.addEventListener("click", (event) => {
    if (!elements.inboxMenu.contains(event.target)) {
      elements.inboxDropdown.hidden = true;
    }
  });
  document.addEventListener("keydown", handleKeyboardShortcut);
}

// 프로젝트 선택 목록을 렌더링한다.
function populateProjectSelect() {
  const projects = projectsConfig?.projects ?? [];
  const counts = getInboxCountsByProject();
  elements.projectSelect.replaceChildren(
    ...projects.map((project) => {
      const count = counts.get(project.id) ?? 0;
      return createElement("option", {
        text: count > 0 ? `${project.name} (${t("inbox.projectOptionCount", { count })})` : project.name,
        attributes: { value: project.id },
      });
    }),
  );
}

// 선택한 프로젝트의 데이터 세트를 불러온다.
async function loadProject(projectId) {
  const nextProject = getProject(projectId);

  if (!nextProject) {
    throw new Error(`Project is not registered: ${projectId}`);
  }

  stopScenarioReplay();
  activeProject = nextProject;
  editingChangeIndex = null;
  selectedStageId = null;
  schemaWarnings = [];
  elements.projectSelect.value = activeProject.id;

  const projectPath = normalizeProjectPath(activeProject.path);
  const loaded = await loadProjectData(activeProject.id, projectPath);

  ({ definition, workflowState, runLog, proposal, scenario, reviewReport, blueprint, measurement, cycleSummary } = loaded);
  if (serverBacked) {
    globalInbox = await loadGlobalInbox();
  } else {
    globalInbox = { schemaVersion: EXPECTED_SCHEMA_VERSION, items: [], autoApprovals: [] };
  }
  proposal = normalizeProposal(proposal);
  reviewReport = normalizeReviewReport(reviewReport);
  blueprint = normalizeBlueprint(blueprint);
  measurement = normalizeMeasurement(measurement);
  projectBaseline = {
    workflowState: cloneData(workflowState),
    runLog: cloneData(runLog),
    proposal: cloneData(proposal),
    measurement: cloneData(measurement),
  };
  selectedStageId = workflowState.currentStage;

  const guarded = enforceGuardrails(definition, workflowState, runLog);
  workflowState = guarded.state;
  runLog = guarded.runLog;
  ensureSelectedStage();
  render();
}

// 선택한 프로젝트 데이터를 서버 API 또는 정적 파일에서 불러온다.
async function loadProjectData(projectId, projectPath) {
  try {
    const [loadedDefinition, loadedState, loadedRunLog, loadedProposal, loadedReviewReport, loadedBlueprint, loadedMeasurement, loadedCycleSummary, loadedScenario] =
      await Promise.all([
        loadVersionedJson(apiProjectFilePath(projectId, "definition"), PROJECT_FILE_NAMES.definition),
        loadVersionedJson(apiProjectFilePath(projectId, "state"), PROJECT_FILE_NAMES.state),
        loadVersionedJson(apiProjectFilePath(projectId, "runlog"), PROJECT_FILE_NAMES.runLog),
        loadOptionalVersionedJson(apiProjectFilePath(projectId, "proposal"), PROJECT_FILE_NAMES.proposal, null),
        loadOptionalVersionedJson(apiProjectFilePath(projectId, "reviews"), PROJECT_FILE_NAMES.reviewReport, {
          schemaVersion: EXPECTED_SCHEMA_VERSION,
          reports: [],
        }),
        loadOptionalVersionedJson(apiProjectFilePath(projectId, "blueprint"), PROJECT_FILE_NAMES.blueprint, {
          schemaVersion: EXPECTED_SCHEMA_VERSION,
          items: [],
        }),
        loadOptionalVersionedJson(apiProjectFilePath(projectId, "measurement"), PROJECT_FILE_NAMES.measurement, null),
        fetchOptionalJson(`/api/projects/${encodeURIComponent(projectId)}/cycle-summary`, null),
        loadOptionalVersionedJson(projectFilePath(projectPath, PROJECT_FILE_NAMES.scenario), PROJECT_FILE_NAMES.scenario, {
          schemaVersion: SCENARIO_SCHEMA_VERSION,
          events: [],
        }),
      ]);

    serverBacked = true;
    return {
      definition: loadedDefinition,
      workflowState: loadedState,
      runLog: loadedRunLog,
      proposal: loadedProposal,
      reviewReport: loadedReviewReport,
      blueprint: loadedBlueprint,
      measurement: loadedMeasurement,
      cycleSummary: loadedCycleSummary,
      scenario: loadedScenario,
    };
  } catch (apiError) {
    const [loadedDefinition, loadedState, loadedRunLog, loadedProposal, loadedScenario, loadedReviewReport, loadedBlueprint, loadedMeasurement] =
      await Promise.all([
        loadVersionedJson(projectFilePath(projectPath, PROJECT_FILE_NAMES.definition), PROJECT_FILE_NAMES.definition),
        loadVersionedJson(projectFilePath(projectPath, PROJECT_FILE_NAMES.state), PROJECT_FILE_NAMES.state),
        loadVersionedJson(projectFilePath(projectPath, PROJECT_FILE_NAMES.runLog), PROJECT_FILE_NAMES.runLog),
        loadOptionalVersionedJson(projectFilePath(projectPath, PROJECT_FILE_NAMES.proposal), PROJECT_FILE_NAMES.proposal, null),
        loadOptionalVersionedJson(projectFilePath(projectPath, PROJECT_FILE_NAMES.scenario), PROJECT_FILE_NAMES.scenario, {
          schemaVersion: SCENARIO_SCHEMA_VERSION,
          events: [],
        }),
        loadOptionalVersionedJson(projectFilePath(projectPath, PROJECT_FILE_NAMES.reviewReport), PROJECT_FILE_NAMES.reviewReport, {
          schemaVersion: EXPECTED_SCHEMA_VERSION,
          reports: [],
        }),
        loadOptionalVersionedJson(projectFilePath(projectPath, PROJECT_FILE_NAMES.blueprint), PROJECT_FILE_NAMES.blueprint, {
          schemaVersion: EXPECTED_SCHEMA_VERSION,
          items: [],
        }),
        loadOptionalVersionedJson(projectFilePath(projectPath, PROJECT_FILE_NAMES.measurement), PROJECT_FILE_NAMES.measurement, null),
      ]);

    serverBacked = false;
    return {
      definition: loadedDefinition,
      workflowState: loadedState,
      runLog: loadedRunLog,
      proposal: loadedProposal,
      scenario: loadedScenario,
      reviewReport: loadedReviewReport,
      blueprint: loadedBlueprint,
      measurement: loadedMeasurement,
      cycleSummary: null,
    };
  }
}

// 버전 검사를 포함해 필수 JSON을 불러온다.
async function loadVersionedJson(path, label) {
  const data = await fetchJson(path);
  recordSchemaWarning(label, data);
  return data;
}

// 버전 검사를 포함해 선택 JSON을 불러온다.
async function loadOptionalVersionedJson(path, label, fallback) {
  const data = await fetchOptionalJson(path, fallback);

  if (data) {
    recordSchemaWarning(label, data);
  }

  return data;
}

// 데이터 계약 버전 경고를 기록한다.
function recordSchemaWarning(label, data) {
  const expected = expectedSchemaVersion(label);

  if (data?.schemaVersion !== expected) {
    schemaWarnings.push({
      label,
      actual: data?.schemaVersion ?? "missing",
    });
  }
}

// 파일별 기대 데이터 버전을 반환한다.
function expectedSchemaVersion(label) {
  if (label === PROJECT_FILE_NAMES.runLog) {
    return RUN_LOG_SCHEMA_VERSION;
  }

  if (label === PROJECT_FILE_NAMES.scenario) {
    return SCENARIO_SCHEMA_VERSION;
  }

  return EXPECTED_SCHEMA_VERSION;
}

// 전체 화면을 현재 상태 기준으로 다시 그린다.
function render() {
  applyLanguage();

  if (!definition || !workflowState || !runLog) {
    return;
  }

  updateDocumentTitle();
  renderSchemaWarning();
  renderCycleSummary();
  elements.projectName.textContent = workflowState.projectName ?? activeProject?.name ?? "";
  setStatusBadge(elements.overallStatus, getOverallBadgeStatus(), getOverallBadgeLabel());
  renderRegressedBadge();
  renderUnattendedBadge();
  elements.totalCost.textContent = formatCurrency(sumEstimatedCost(runLog));
  elements.subscriptionCalls.textContent = formatSubscriptionCallsByRole(runLog);
  elements.replayScenario.disabled = scenarioRunning || getScenarioEvents().length === 0;

  ensureSelectedStage();
  renderPipelineMinimap();
  renderInbox();
  renderStageList();
  renderStageDetail();
  renderApprovalPanel();
  renderRunLog();
}

// 스키마 버전 경고 배너를 표시한다.
function renderSchemaWarning() {
  if (schemaWarnings.length === 0) {
    elements.schemaWarning.hidden = true;
    elements.schemaWarning.textContent = "";
    return;
  }

  elements.schemaWarning.hidden = false;
  elements.schemaWarning.textContent = t("schema.warning", {
    files: schemaWarnings
      .map((warning) => `${warning.label}:${warning.actual}/${expectedSchemaVersion(warning.label)}`)
      .join(", "),
    expected: `${EXPECTED_SCHEMA_VERSION}/${RUN_LOG_SCHEMA_VERSION}`,
  });
}

// 헤더의 악화 배지를 렌더링한다.
// 현재 회차 시간 분해 바를 표시한다.
function renderCycleSummary() {
  const segments = cycleSummary?.segments;

  if (!segments) {
    elements.cycleSummary.hidden = true;
    elements.cycleSummary.replaceChildren();
    return;
  }

  elements.cycleSummary.hidden = false;
  elements.cycleSummary.replaceChildren(
    createElement("span", {
      className: "cycle-summary__label",
      text: t("cycleSummary.title", { iteration: cycleSummary.loopIteration ?? workflowState.loopIteration ?? 0 }),
    }),
    renderCycleSegment("measurement", segments.measurementMs),
    renderCycleSegment("generation", segments.generationMs),
    renderCycleSegment("review", segments.reviewMs),
    renderCycleSegment("humanWaiting", segments.humanWaitingMs),
    renderCycleSegment("total", segments.totalMs),
  );
}

// 시간 분해 항목 하나를 만든다.
function renderCycleSegment(key, milliseconds) {
  return createElement("span", {
    className: `cycle-segment cycle-segment-${key}`,
    text: `${t(`cycleSummary.${key}`)} ${formatDuration(milliseconds ?? 0)}`,
  });
}

// 밀리초를 짧은 표시 문자열로 바꾼다.
function formatDuration(milliseconds) {
  const totalSeconds = Math.max(0, Math.round(Number(milliseconds || 0) / 1000));

  if (totalSeconds < 60) {
    return `${totalSeconds}s`;
  }

  const minutes = Math.floor(totalSeconds / 60);
  const seconds = totalSeconds % 60;

  if (minutes < 60) {
    return seconds === 0 ? `${minutes}m` : `${minutes}m ${seconds}s`;
  }

  const hours = Math.floor(minutes / 60);
  const remainingMinutes = minutes % 60;
  return remainingMinutes === 0 ? `${hours}h` : `${hours}h ${remainingMinutes}m`;
}

// 악화 보류 배지를 표시한다.
function renderRegressedBadge() {
  const count = Array.isArray(workflowState.suspendedTracks) ? workflowState.suspendedTracks.length : 0;

  if (count === 0) {
    elements.regressedBadge.hidden = true;
    return;
  }

  elements.regressedBadge.hidden = false;
  elements.regressedBadge.textContent = t("header.regressedBadge", { count });
}

// 헤더에 무인 진행 회차 수를 표시한다.
function renderUnattendedBadge() {
  const count = countAutoApprovals(reviewReport);

  if (count === 0) {
    elements.unattendedBadge.hidden = true;
    return;
  }

  elements.unattendedBadge.hidden = false;
  elements.unattendedBadge.textContent = t("header.unattendedBadge", { count });
}

// 헤더 전역 인박스 배지와 드롭다운을 렌더링한다.
function renderInbox() {
  const items = getInboxItems();
  const autoApprovals = getAutoApprovalItems();

  populateProjectSelect();
  elements.projectSelect.value = activeProject?.id ?? "";

  if (items.length === 0 && autoApprovals.length === 0) {
    elements.inboxMenu.hidden = true;
    elements.inboxDropdown.hidden = true;
    elements.inboxDropdown.replaceChildren();
    return;
  }

  elements.inboxMenu.hidden = false;
  elements.inboxBadge.textContent = t("inbox.badge", { count: items.length });
  elements.inboxDropdown.replaceChildren(
    createElement("p", { className: "section-label", text: t("inbox.title") }),
    ...items.map((item) => renderInboxItem(item)),
    renderAutoApprovalInboxSection(autoApprovals),
  );
}

// 최근 자동 결재 감사 섹션을 렌더링한다.
function renderAutoApprovalInboxSection(items) {
  const section = createElement("section", { className: "inbox-audit" });
  section.append(createElement("p", { className: "section-label", text: t("inbox.autoApprovals", { count: items.length }) }));

  if (items.length === 0) {
    section.append(createElement("p", { className: "empty-state", text: t("inbox.noAutoApprovals") }));
    return section;
  }

  section.append(...items.map(renderAutoApprovalInboxItem));
  return section;
}

// 자동 결재 감사 항목을 렌더링한다.
function renderAutoApprovalInboxItem(item) {
  const actual = Array.isArray(item.actual)
    ? item.actual.map((metric) => `${metric.metricId}:${formatValue(metric.value)}`).join(", ")
    : t("approval.none");
  const node = createElement("div", { className: "inbox-item inbox-audit-item" });
  node.append(
    createElement("strong", { text: item.projectName ?? item.projectId }),
    createElement("span", { text: `${item.proposalId ?? ""} · ${formatRisk(item.riskAssessed ?? "low")}` }),
    createElement("span", { className: "muted", text: `${item.reason ?? ""} · ${t("inbox.actual", { actual })}` }),
  );
  return node;
}

// 인박스 항목 버튼을 렌더링한다.
function renderInboxItem(item) {
  const button = createElement("button", {
    className: "inbox-item",
    attributes: { type: "button" },
  });
  button.append(
    createElement("strong", { text: item.projectName ?? item.projectId }),
    createElement("span", { text: `${formatInboxKind(item.kind)} · ${item.title ?? ""}` }),
    createElement("span", { className: "muted", text: `${item.summary ?? ""} · ${formatWaitingSince(item.waitingSince)}` }),
  );
  button.addEventListener("click", async () => {
    elements.inboxDropdown.hidden = true;
    await loadProject(item.projectId);
    elements.approvalPanel.scrollIntoView({ behavior: "smooth", block: "start" });
  });
  return button;
}

// 헤더의 단계 미니맵을 렌더링한다.
function renderPipelineMinimap() {
  elements.pipelineMinimap.replaceChildren(
    ...definition.stages.map((stage) => {
      const status = getStageBadgeStatus(stage.id);
      return createElement("span", {
        className: [
          "minimap-dot",
          `status-dot-${status}`,
          stage.id === workflowState.currentStage ? "is-current" : "",
        ]
          .filter(Boolean)
          .join(" "),
        attributes: { title: `${stage.name}: ${formatStatus(status)}` },
      });
    }),
  );
}

// 왼쪽 단계 목록을 렌더링한다.
function renderStageList() {
  elements.stageList.replaceChildren(
    ...definition.stages.map((stage, index) => {
      const badgeStatus = getStageBadgeStatus(stage.id);
      const item = createElement("li", { className: "stage-list-entry" });
      const button = createElement("button", {
        className: [
          "stage-item",
          stage.id === workflowState.currentStage ? "is-current" : "",
          stage.id === selectedStageId ? "is-selected" : "",
        ]
          .filter(Boolean)
          .join(" "),
        attributes: {
          type: "button",
          "aria-pressed": String(stage.id === selectedStageId),
        },
      });
      const indexNode = createElement("span", {
        className: "stage-index",
        text: String(index + 1),
      });
      const body = createElement("span", { className: "stage-body" });
      const name = createElement("span", { className: "stage-name", text: stage.name });
      const badge = createStatusBadge(badgeStatus);
      const gate = renderGateList(stage);

      button.addEventListener("click", () => {
        selectedStageId = stage.id;
        editingChangeIndex = null;
        render();
      });

      body.append(name, badge);

      if (gate) {
        body.append(gate);
      }

      button.append(indexNode, body);
      item.append(button);
      return item;
    }),
  );
}

// 단계의 게이트 조건 목록을 렌더링한다.
function renderGateList(stage) {
  const gate = evaluateGate(definition, workflowState, stage.id);

  if (!gate.hasGate) {
    return null;
  }

  const list = createElement("ul", { className: "gate-list" });
  list.append(
    ...gate.checks.map((check) => {
      const marker = check.passed ? "\u2713" : "\u2717";
      const mustBe = Array.isArray(check.condition.mustBe)
        ? check.condition.mustBe.map(formatStatus).join(", ")
        : "";
      return createElement("li", {
        className: `gate-check ${check.passed ? "is-pass" : "is-fail"}`,
        text: `${marker} ${resolveStageName(check.condition.stage)}: ${formatStatus(check.actual)} / ${mustBe}`,
      });
    }),
  );
  return list;
}

// 선택된 단계 상세 패널을 렌더링한다.
function renderStageDetail() {
  const stage = getStage(definition, selectedStageId) ?? getStage(definition, workflowState.currentStage);
  const stageId = stage?.id ?? workflowState.currentStage;
  const loopInfo = getLoopInfo();
  const details = workflowState.stageDetails?.[stageId] ?? {};
  const status = getStageBadgeStatus(stageId);

  elements.detailTitle.textContent = stage?.name ?? stageId;
  elements.stageDetail.replaceChildren();

  const headerLine = createElement("div", { className: "button-row" });
  headerLine.append(
    createStatusBadge(status),
    ...(loopInfo ? [createStatusBadge(loopInfo.badgeStatus, loopInfo.badgeLabel)] : []),
    createStatusBadge(`mode_${workflowState.mode ?? "normal"}`, formatMode(workflowState.mode ?? "normal")),
    ...(Array.isArray(workflowState.suspendedTracks) && workflowState.suspendedTracks.length > 0
      ? [createStatusBadge("suspended_tracks", t("detail.suspendedTracks", { count: workflowState.suspendedTracks.length }))]
      : []),
    createElement("span", {
      className: "muted",
      text: t("detail.updated", { time: formatDateTime(workflowState.lastUpdated) }),
    }),
  );

  if (canRunMeasurement()) {
    const measureButton = createElement("button", {
      className: "button",
      text: t("buttons.measure"),
      attributes: { type: "button" },
    });
    measureButton.disabled = measureRunning;
    measureButton.addEventListener("click", runMeasurement);
    headerLine.append(measureButton);
  }

  elements.stageDetail.append(headerLine);

  if (loopInfo?.detail) {
    const haltNotice = createElement("div");
    haltNotice.append(
      createElement("p", {
        className: `halt-banner halt-${loopInfo.type}`,
        text: loopInfo.detail,
      }),
    );

    if (serverBacked && loopInfo.canAcknowledge) {
      const acknowledgeButton = createElement("button", {
        className: "button button-secondary",
        text: t("buttons.acknowledge"),
        attributes: { type: "button" },
      });
      acknowledgeButton.addEventListener("click", acknowledgeLoop);
      haltNotice.append(acknowledgeButton);
    }

    elements.stageDetail.append(haltNotice);
  }

  elements.stageDetail.append(
    createElement("p", {
      className: "detail-summary",
      text: details.summary ?? stage?.description ?? t("detail.noStageSummary"),
    }),
  );

  const metrics = details.metrics ?? [];
  if (metrics.length > 0) {
    const metricSection = createElement("section");
    metricSection.append(createElement("p", { className: "section-label", text: t("detail.metrics") }));
    const metricList = createElement("ul", { className: "metric-list" });
    metricList.append(
      ...metrics.map((metric) => {
        const item = createElement("li", { className: "metric-item" });
        item.append(
          createElement("p", { className: "metric-value", text: metric.value }),
          createElement("p", { className: "metric-label", text: metric.label }),
        );
        return item;
      }),
    );
    metricSection.append(metricList);
    elements.stageDetail.append(metricSection);
  }

  const issues = details.issues ?? [];
  const issueSection = createElement("section");
  issueSection.append(createElement("p", { className: "section-label", text: t("detail.issues") }));

  if (issues.length === 0) {
    issueSection.append(createElement("p", { className: "empty-state", text: t("detail.noActiveIssues") }));
  } else {
    const issueList = createElement("ul", { className: "issue-list" });
    issueList.append(...issues.map((issue) => createElement("li", { text: issue })));
    issueSection.append(issueList);
  }

  elements.stageDetail.append(issueSection);
}

// 변경 제안과 검토 패널을 렌더링한다.
function renderApprovalPanel() {
  const context = getReviewContext();

  elements.approvalTitle.textContent = context.reviewStage?.name ?? t("panels.approvalTitleFallback");
  elements.approvalPanel.replaceChildren();

  if (!context.reviewStage) {
    elements.approvalPanel.append(
      createElement("p", {
        className: "empty-state",
        text: t("approval.noHumanGate"),
      }),
    );
    return;
  }

  if (!context.hasPendingProposal) {
    const otherCount = getInboxItems().filter((item) => item.projectId !== activeProject?.id).length;
    const emptyTitle = createElement("div", { className: "button-row" });
    emptyTitle.append(
      createElement("h3", { className: "proposal-title", text: t("approval.noPendingTitle") }),
      createStatusBadge(context.status),
    );
    const otherLink = otherCount > 0
      ? createElement("button", {
          className: "button button-secondary",
          text: t("approval.otherProjectPending", { count: otherCount }),
          attributes: { type: "button" },
        })
      : null;
    if (otherLink) {
      otherLink.addEventListener("click", () => {
        elements.inboxMenu.hidden = false;
        elements.inboxDropdown.hidden = false;
      });
    }
    elements.approvalPanel.append(
      emptyTitle,
      createElement("p", { className: "empty-state empty-state-large", text: t("approval.noPendingBody") }),
      ...(otherLink ? [otherLink] : []),
      renderClosedProposalHistory(),
    );
    return;
  }

  const risk = assessProposalRisk(definition, proposal);
  const autoApproveTarget = isAutoApproveTarget(definition, risk.risk);
  const titleRow = createElement("div", { className: "button-row" });
  titleRow.append(
    createElement("h3", {
      className: "proposal-title",
      text: proposal.title ?? t("approval.untitledProposal"),
    }),
    createStatusBadge(context.status),
  );

  if (proposal.kind === "rollback") {
    titleRow.append(createStatusBadge("rollback", t("approval.rollbackBadge")));
  }

  if (proposal.kind === "tuning") {
    titleRow.append(createStatusBadge("tuning", t("approval.tuningBadge")));
  }

  const meta = createElement("div", { className: "proposal-meta" });
  meta.append(
    createMetaItem(t("approval.proposal"), proposal.id ?? t("approval.none")),
    createMetaItem(t("approval.revisionOf"), proposal.revisionOf ?? t("approval.none")),
    createMetaItem(t("approval.lifecycle"), formatLifecycle(proposal.lifecycle ?? "draft")),
    createMetaItem(t("approval.waiting"), formatWaitingSince(getCurrentProposalWaitingSince())),
    createMetaItem(t("approval.latestVerdict"), formatLatestVerdict(proposal.id)),
    createMetaItem(t("risk.assessedRisk"), formatRisk(risk.risk)),
    createMetaItem(t("risk.providedRisk"), risk.providedRisk ? formatRisk(risk.providedRisk) : t("approval.none")),
    createMetaItem(t("risk.changeCount"), String(risk.metrics.changeCount)),
    createMetaItem(t("risk.maxDelta"), `${formatNumber(risk.metrics.maxValueDeltaPercent)}%`),
    createMetaItem(t("approval.provider"), proposal.createdBy?.provider ?? t("approval.unknown")),
  );

  const riskMismatch = risk.mismatch
    ? createElement("p", {
        className: "risk-warning",
        text: t("risk.mismatch", {
          provided: risk.providedRisk ? formatRisk(risk.providedRisk) : t("approval.none"),
          assessed: formatRisk(risk.risk),
        }),
      })
    : null;
  const assumptions = renderProposalAssumptions(proposal.assumptions);

  const changes = createElement("section");
  changes.append(createElement("p", { className: "section-label", text: t("approval.changes") }));
  const changeList = createElement("ul", { className: "change-list" });
  const proposalChanges = Array.isArray(proposal.changes) ? proposal.changes : [];
  changeList.append(
    ...(proposalChanges.length > 0
      ? proposalChanges.map((change, index) => renderProposalChange(change, index, context.canReview))
      : [createElement("li", { text: t("approval.noChanges") })]),
  );
  changes.append(changeList);

  const impact = createElement("section");
  impact.append(createElement("p", { className: "section-label", text: t("approval.impact") }));
  const impactList = createElement("ul", { className: "change-list" });
  const proposalImpact = Array.isArray(proposal.impact) ? proposal.impact : [];
  impactList.append(
    ...(proposalImpact.length > 0
      ? proposalImpact.map((item) => createElement("li", { text: `${item.label}: ${item.value}` }))
      : [createElement("li", { text: t("approval.noImpact") })]),
  );
  impact.append(impactList);

  const predictedMetrics = Array.isArray(proposal.predictedMetrics) ? proposal.predictedMetrics : [];
  const predicted = predictedMetrics.length > 0 ? renderPredictedMetrics(predictedMetrics) : null;

  const reviewHistory = renderReviewHistory(proposal.id);

  const approveButton = createElement("button", {
    className: "button button-approve",
    text: t("buttons.approve"),
    attributes: { type: "button" },
  });
  const rejectButton = createElement("button", {
    className: "button button-reject",
    text: t("buttons.reject"),
    attributes: { type: "button" },
  });

  approveButton.disabled = !context.canReview || reviewActionRunning;
  rejectButton.disabled = !context.canReview || reviewActionRunning;
  if (reviewActionRunning) {
    approveButton.textContent = t("buttons.processing");
    rejectButton.textContent = t("buttons.processing");
  }
  approveButton.addEventListener("click", approveProposal);
  rejectButton.addEventListener("click", rejectProposal);

  const actions = createElement("div", { className: "button-row" });
  actions.append(
    approveButton,
    ...(autoApproveTarget ? [createElement("span", { className: "tag tag-ai", text: t("risk.autoApproveTarget") })] : []),
    rejectButton,
  );

  elements.approvalPanel.append(
    titleRow,
    createElement("p", {
      className: "proposal-summary",
      text: proposal.summary ?? t("detail.noStageSummary"),
    }),
    ...(assumptions ? [assumptions] : []),
    meta,
    ...(riskMismatch ? [riskMismatch] : []),
    changes,
    impact,
    ...(predicted ? [predicted] : []),
    reviewHistory,
    actions,
  );
}

// 끝난 제안의 검토 이력을 접힌 목록으로 렌더링한다.
function renderClosedProposalHistory() {
  if (!hasProposalData(proposal) || proposal.lifecycle === "submitted") {
    return createElement("section");
  }

  const details = createElement("details", { className: "closed-proposal-history" });
  details.append(
    createElement("summary", { text: t("approval.closedProposalHistory") }),
    createElement("p", { className: "proposal-summary", text: proposal.summary ?? t("detail.noStageSummary") }),
    renderReviewHistory(proposal.id),
  );
  return details;
}

// 튜닝 제안의 예측 지표(현재 → 예측)를 렌더링한다. "예측" 라벨을 항상 붙인다.
// 제안의 가정과 불확실 지점을 표시한다.
function renderProposalAssumptions(assumptions) {
  const items = Array.isArray(assumptions)
    ? assumptions.filter((item) => typeof item === "string" && item.trim().length > 0)
    : [];

  if (items.length === 0) {
    return null;
  }

  const section = createElement("section");
  const list = createElement("ul", { className: "change-list assumption-list" });
  list.append(...items.map((item) => createElement("li", { className: "change-item", text: item })));
  section.append(
    createElement("p", { className: "section-label", text: t("approval.assumptions") }),
    list,
  );
  return section;
}

// 제안의 예측 지표를 표시한다.
function renderPredictedMetrics(predictedMetrics) {
  const section = createElement("section");
  section.append(createElement("p", { className: "section-label", text: t("approval.predictedMetrics") }));

  const list = createElement("ul", { className: "change-list" });
  list.append(
    ...predictedMetrics.map((metric) => {
      const item = createElement("li", { className: "change-item predicted-metric-item" });
      item.append(
        createElement("span", { className: "tag tag-predicted", text: t("approval.predictedLabel") }),
        createElement("span", {
          text: `${metric.metricId}: ${formatNumber(metric.before)} → ${formatNumber(metric.after)} (${t("approval.predictedBand")} ${metric.band})`,
        }),
      );
      return item;
    }),
  );
  section.append(list);
  return section;
}

// 변경 항목 diff와 편집 UI를 렌더링한다.
function renderProposalChange(rawChange, index, canEditProposal) {
  const change = normalizeChange(rawChange, index);
  const item = createElement("li", { className: "change-item change-diff" });

  if (editingChangeIndex === index) {
    const path = createElement("p", { className: "change-path", text: change.path });
    const before = createElement("p", {
      className: "change-before-readonly",
      text: `${t("diff.before")}: ${formatValue(change.before)}`,
    });
    const afterInput = createElement("input", {
      className: "change-input",
      attributes: {
        type: "text",
        "aria-label": t("diff.after"),
      },
    });
    const noteInput = createElement("textarea", {
      className: "change-editor",
      attributes: {
        rows: "3",
        "aria-label": t("diff.note"),
      },
    });
    afterInput.value = formatEditableValue(change.after);
    noteInput.value = change.note ?? "";

    const saveButton = createElement("button", {
      className: "button button-compact",
      text: t("buttons.save"),
      attributes: { type: "button" },
    });
    const cancelButton = createElement("button", {
      className: "button button-secondary button-compact",
      text: t("buttons.cancel"),
      attributes: { type: "button" },
    });
    const actions = createElement("div", { className: "change-actions" });

    saveButton.addEventListener("click", () => {
      commitProposalChangeEdit(index, afterInput.value, noteInput.value);
    });
    cancelButton.addEventListener("click", () => {
      editingChangeIndex = null;
      render();
    });

    actions.append(saveButton, cancelButton);
    item.append(path, before, afterInput, noteInput, actions);
    return item;
  }

  const header = createElement("div", { className: "change-diff-header" });
  header.append(
    createElement("span", { className: "change-path", text: change.path }),
    createElement("span", { className: "change-delta", text: formatDelta(change.before, change.after) }),
  );

  const values = createElement("div", { className: "diff-values" });
  values.append(
    createElement("span", { className: "diff-before", text: formatValue(change.before) }),
    createElement("span", { className: "diff-arrow", text: "->" }),
    createElement("span", { className: "diff-after", text: formatValue(change.after) }),
  );

  item.append(
    header,
    values,
    createElement("p", { className: "change-note", text: change.note ?? t("approval.none") }),
  );

  if (canEditProposal) {
    const editButton = createElement("button", {
      className: "button button-secondary button-compact",
      text: t("buttons.edit"),
      attributes: { type: "button" },
    });
    editButton.addEventListener("click", () => {
      editingChangeIndex = index;
      render();
    });
    item.append(editButton);
  }

  return item;
}

// 검토 이력 목록을 렌더링한다.
function renderReviewHistory(proposalId) {
  const section = createElement("section");
  section.append(createElement("p", { className: "section-label", text: t("review.history") }));

  const reports = getReportsForProposal(proposalId);

  if (reports.length === 0) {
    section.append(createElement("p", { className: "empty-state", text: t("review.noHistory") }));
    return section;
  }

  const list = createElement("ol", { className: "review-history" });
  list.append(
    ...reports.map((report) => {
      const item = createElement("li", { className: "review-report" });
      const header = createElement("div", { className: "review-report-header" });
      header.append(
        createElement("span", { className: `verdict verdict-${report.verdict}`, text: formatVerdict(report.verdict) }),
        createElement("span", {
          className: "muted",
          text: `${formatReviewer(report.reviewer)} / ${formatDateTime(report.createdAt)}`,
        }),
      );
      item.append(
        header,
        createElement("p", { className: "review-reason", text: report.reason ?? t("approval.none") }),
      );

      const findings = renderReportFindings(report.findings);
      if (findings) {
        item.append(findings);
      }

      return item;
    }),
  );
  section.append(list);
  return section;
}

// 검토 리포트의 finding 목록을 렌더링한다.
function renderReportFindings(findings) {
  if (!Array.isArray(findings) || findings.length === 0) {
    return null;
  }

  const list = createElement("ul", { className: "review-findings" });
  list.append(
    ...findings.map((finding) => {
      const checkId = finding.checkId ?? finding.target ?? "";
      const answer = typeof finding.answer === "boolean" ? String(finding.answer) : t("approval.unknown");
      const note = finding.note ?? finding.comment ?? "";
      return createElement("li", {
        text: `${checkId}: ${answer} · ${note}`,
      });
    }),
  );
  return list;
}

// 실행 로그 타임라인을 렌더링한다.
function renderRunLog() {
  const entries = runLog.entries ?? [];
  elements.logCount.textContent = t("runLog.entries", { count: entries.length });
  elements.runLog.replaceChildren(
    ...entries
      .slice()
      .sort((a, b) => new Date(b.createdAt).getTime() - new Date(a.createdAt).getTime())
      .map((entry) => {
        const item = createElement("li", {
          className: `log-item log-event-${sanitizeClassName(entry.event ?? "unknown")}`,
        });
        const level = createElement("span", {
          className: `log-level log-level-${entry.level ?? "info"}`,
          attributes: { "aria-hidden": "true" },
        });
        const eventText = createElement("p", { className: "log-event-text" });
        eventText.append(level, document.createTextNode(formatEvent(entry)));
        if (entry.event === "review.approver_completed") {
          eventText.append(" ", createStatusBadge("ai_approval", t("review.aiApprovalBadge")));
        }

        item.append(
          createElement("span", { className: "log-time", text: formatDateTime(entry.createdAt) }),
          eventText,
          createElement("span", {
            className: "log-actor",
            text: formatActor(entry.producedBy),
          }),
          createElement("span", {
            className: "log-cost",
            text: formatLogMeta(entry),
          }),
        );
        return item;
      }),
  );
}

// 측정 공급자를 실행하고 서버 응답으로 화면을 갱신한다.
async function runMeasurement() {
  if (!canRunMeasurement() || measureRunning) {
    return;
  }

  measureRunning = true;
  render();
  await postProjectAction("measure", {});
  measureRunning = false;
  render();
}

// 변경 제안을 승인하고 검토 리포트를 추가한다.
async function approveProposal() {
  const context = getReviewContext();

  if (!context.canReview || reviewActionRunning) {
    return;
  }

  if (serverBacked) {
    reviewActionRunning = true;
    render();
    try {
      await postProjectAction("approve", {
        editedChanges: getEditFindings(proposal),
      });
    } finally {
      reviewActionRunning = false;
      render();
    }
    return;
  }

  const risk = assessProposalRisk(definition, proposal);
  const report = createReviewReport({
    verdict: "approved",
    reason: t("review.approvedReason"),
    riskAssessed: risk.risk,
    findings: getEditFindings(proposal),
  });
  let nextState = applyStageStatus(definition, workflowState, context.reviewStage.id, "approved");
  let nextRunLog = appendLog(runLog, {
    event: "review.approved",
    params: {
      proposalId: proposal.id ?? t("approval.proposal"),
      edited: Boolean(proposal.edited),
    },
    level: "info",
    producedBy: { provider: "human", model: null },
    loopIteration: workflowState.loopIteration ?? 0,
    cost: { inputTokens: 0, outputTokens: 0, estimatedUSD: 0, subscriptionCalls: 0, role: "runtime" },
  });
  const nextStage = getNextStage(definition, context.reviewStage.id);

  if (nextStage) {
    const transition = applyStatePatchWithGate(
      nextState,
      {
        currentStage: nextStage.id,
        stageStatuses: { [nextStage.id]: "in_progress" },
        overallStatus: "in_progress",
      },
      nextRunLog,
    );
    nextState = transition.state;
    nextRunLog = transition.runLog;
    selectedStageId = nextState.currentStage;
  } else {
    nextState = applyStatePatch(definition, nextState, {
      overallStatus: "completed",
    });
    selectedStageId = context.reviewStage.id;
  }

  const nextProposal = applyProposalPatch(proposal, {
    lifecycle: "decided",
  });
  const nextReviewReport = appendReviewReport(reviewReport, report);

  editingChangeIndex = null;
  commitState(nextState, nextRunLog, nextProposal, nextReviewReport);
}

// 변경 제안을 거절하고 검토 리포트를 추가한다.
async function rejectProposal() {
  const context = getReviewContext();

  if (!context.canReview || reviewActionRunning) {
    return;
  }

  const reason = window.prompt(t("approval.rejectionPrompt"));

  if (reason === null) {
    return;
  }

  const trimmedReason = reason.trim();

  if (serverBacked) {
    reviewActionRunning = true;
    render();
    try {
      await postProjectAction("reject", {
        reason: trimmedReason,
      });
    } finally {
      reviewActionRunning = false;
      render();
    }
    return;
  }

  const risk = assessProposalRisk(definition, proposal);
  const report = createReviewReport({
    verdict: "rejected",
    reason: trimmedReason || t("review.rejectedReason"),
    riskAssessed: risk.risk,
    findings: getEditFindings(proposal),
  });
  const nextState = applyStageStatus(definition, workflowState, context.reviewStage.id, "failed");
  const nextProposal = applyProposalPatch(proposal, {
    lifecycle: "decided",
  });
  const nextRunLog = appendLog(runLog, {
    event: "review.rejected",
    params: {
      proposalId: proposal.id ?? t("approval.proposal"),
      text: trimmedReason,
    },
    level: "warning",
    producedBy: { provider: "human", model: null },
    loopIteration: workflowState.loopIteration ?? 0,
    cost: { inputTokens: 0, outputTokens: 0, estimatedUSD: 0, subscriptionCalls: 0, role: "runtime" },
  });
  const nextReviewReport = appendReviewReport(reviewReport, report);

  selectedStageId = context.reviewStage.id;
  editingChangeIndex = null;
  commitState(nextState, nextRunLog, nextProposal, nextReviewReport);
}

// 변경 항목의 편집 결과를 메모리 상태에 반영한다.
async function commitProposalChangeEdit(index, nextAfterText, nextNoteText) {
  if (!hasProposalData(proposal) || !Array.isArray(proposal.changes)) {
    return;
  }

  const beforeChange = normalizeChange(proposal.changes[index], index);
  const nextAfter = parseEditedValue(nextAfterText, beforeChange.after);
  const nextNote = nextNoteText.trim();

  if (Object.is(beforeChange.after, nextAfter) && (beforeChange.note ?? "") === nextNote) {
    editingChangeIndex = null;
    render();
    return;
  }

  if (serverBacked) {
    const saved = await postProjectAction("edit-change", {
      changeIndex: index,
      after: nextAfter,
      note: nextNote,
    });

    if (saved) {
      editingChangeIndex = null;
    }
    return;
  }

  const nextProposal = cloneData(proposal);
  nextProposal.changes[index] = {
    ...beforeChange,
    after: nextAfter,
    note: nextNote,
  };
  nextProposal.edited = true;
  nextProposal.lastEditedAt = new Date().toISOString();
  nextProposal.editFindings = [
    ...(Array.isArray(nextProposal.editFindings) ? nextProposal.editFindings : []),
    {
      target: beforeChange.path,
      comment: t("review.editFinding", {
        before: formatValue(beforeChange.after),
        after: formatValue(nextAfter),
        note: nextNote || t("approval.none"),
      }),
      severity: "info",
    },
  ];
  const latestFinding = nextProposal.editFindings[nextProposal.editFindings.length - 1];
  const risk = assessProposalRisk(definition, nextProposal);
  const editReport = createReviewReport({
    verdict: "needs_changes",
    reason: t("review.editReason"),
    riskAssessed: risk.risk,
    findings: [latestFinding],
  });

  const nextRunLog = appendLog(runLog, {
    event: "proposal.edited",
    params: {
      target: beforeChange.path,
      before: formatValue(beforeChange.after),
      after: formatValue(nextAfter),
      text: nextNote,
    },
    level: "info",
    producedBy: { provider: "human", model: null },
    loopIteration: workflowState.loopIteration ?? 0,
    cost: { inputTokens: 0, outputTokens: 0, estimatedUSD: 0, subscriptionCalls: 0, role: "runtime" },
  });
  const nextReviewReport = appendReviewReport(reviewReport, editReport);

  editingChangeIndex = null;
  commitState(workflowState, nextRunLog, nextProposal, nextReviewReport);
}

// 시나리오 이벤트를 순차적으로 재생한다.
function replayScenario() {
  const events = getScenarioEvents();

  if (events.length === 0) {
    return;
  }

  stopScenarioReplay();
  const baseState = cloneData(projectBaseline?.workflowState ?? workflowState);
  const baseRunLog = cloneData(projectBaseline?.runLog ?? runLog);
  const baseProposal = cloneData(projectBaseline?.proposal ?? proposal);

  workflowState = baseState;
  runLog = baseRunLog;
  proposal = normalizeProposal(baseProposal);
  selectedStageId = workflowState.currentStage;
  scenarioRunning = true;
  editingChangeIndex = null;
  render();

  let index = 0;
  const intervalMs = Number(scenario?.intervalMs ?? 1000);

  scenarioTimer = window.setInterval(() => {
    const event = events[index];
    const sourceState = workflowState;
    const sourceRunLog = runLog;
    const sourceProposal = proposal;
    const transition = applyStatePatchWithGate(sourceState, event.statePatch ?? {}, sourceRunLog, event.log?.createdAt);
    const nextRunLog = event.log && !transition.blocked ? appendLog(transition.runLog, event.log) : transition.runLog;
    const nextProposal = applyProposalPatch(sourceProposal, event.proposalPatch);

    selectedStageId = transition.state.currentStage;
    commitState(transition.state, nextRunLog, nextProposal, reviewReport);
    index += 1;

    if (index >= events.length) {
      stopScenarioReplay();
      render();
    }
  }, Number.isFinite(intervalMs) && intervalMs > 0 ? intervalMs : 1000);
}

// 게이트 조건을 확인하며 상태 패치를 적용한다.
function applyStatePatchWithGate(sourceState, patch, sourceRunLog, blockedCreatedAt = null) {
  if (!patch || Object.keys(patch).length === 0) {
    return {
      state: sourceState,
      runLog: sourceRunLog,
      blocked: false,
    };
  }

  if (patch.currentStage && patch.currentStage !== sourceState.currentStage) {
    const candidatePatch = cloneData(patch);
    const targetStageId = candidatePatch.currentStage;
    delete candidatePatch.currentStage;
    delete candidatePatch.overallStatus;

    const candidateState = applyGateBlockedPatch(definition, sourceState, candidatePatch, targetStageId);
    const gate = evaluateGate(definition, candidateState, targetStageId);

    if (gate.hasGate && !gate.passed) {
      return {
        state: candidateState,
        runLog: appendLog(sourceRunLog, {
          createdAt: blockedCreatedAt ?? new Date().toISOString(),
          event: "stage.blocked",
          params: {
            stage: targetStageId,
            failedChecks: gate.checks
              .filter((check) => !check.passed)
              .map((check) => ({
                stage: check.condition.stage,
                actual: check.actual,
                mustBe: Array.isArray(check.condition.mustBe) ? check.condition.mustBe : [],
              })),
          },
          level: "warning",
          producedBy: { provider: "workflow-engine", model: null },
          loopIteration: sourceState.loopIteration ?? 0,
          cost: { inputTokens: 0, outputTokens: 0, estimatedUSD: 0, subscriptionCalls: 0, role: "runtime" },
        }),
        blocked: true,
      };
    }
  }

  return {
    state: applyStatePatch(definition, sourceState, patch),
    runLog: sourceRunLog,
    blocked: false,
  };
}

// 상태 변경을 확정하고 가드레일을 적용한다.
function commitState(nextState, nextRunLog, nextProposal = proposal, nextReviewReport = reviewReport) {
  const guarded = enforceGuardrails(definition, nextState, nextRunLog);
  workflowState = guarded.state;
  runLog = guarded.runLog;
  proposal = normalizeProposal(nextProposal);
  reviewReport = normalizeReviewReport(nextReviewReport);
  ensureSelectedStage();
  render();
}

// 진행 중인 시나리오 재생을 멈춘다.
function stopScenarioReplay() {
  window.clearInterval(scenarioTimer);
  scenarioTimer = null;
  scenarioRunning = false;
}

// 현재 메모리 상태를 JSON 파일로 내려받는다.
function downloadWorkspaceJson() {
  const payload = {
    schemaVersion: EXPECTED_SCHEMA_VERSION,
    project: activeProject,
    workflowState,
    runLog,
    proposal,
    reviewReport,
    measurement,
    downloadedAt: new Date().toISOString(),
  };
  const blob = new Blob([JSON.stringify(payload, null, 2)], {
    type: "application/json",
  });
  const url = URL.createObjectURL(blob);
  const anchor = document.createElement("a");
  anchor.href = url;
  anchor.download = `workflow-dashboard-${activeProject?.id ?? "project"}-${Date.now()}.json`;
  document.body.append(anchor);
  anchor.click();
  anchor.remove();
  URL.revokeObjectURL(url);
}

// 검토 단계와 승인 가능 여부를 계산한다.
function getReviewContext() {
  const reviewStage = getHumanReviewStage(definition, workflowState);
  const hasProposal = hasProposalData(proposal);
  const hasPendingProposal = hasProposal && proposal.lifecycle === "submitted";
  const status = reviewStage ? getStageStatus(workflowState, reviewStage.id) : "not_started";
  const gate = reviewStage ? evaluateGate(definition, workflowState, reviewStage.id) : null;

  return {
    reviewStage,
    hasProposal,
    hasPendingProposal,
    status,
    gate,
    canReview:
      status === "pending_review" &&
      hasPendingProposal &&
      isLoopInteractive() &&
      (gate === null || gate.passed),
  };
}

// 현재 루프에서 사용자가 조작 가능한지 확인한다.
function isLoopInteractive() {
  const loopState = workflowState?.loopState ?? "running";
  return loopState !== "halted";
}

// 루프 정지 또는 체크포인트 확인을 서버에 기록한다.
async function acknowledgeLoop() {
  const loopInfo = getLoopInfo();

  if (!serverBacked || !loopInfo?.canAcknowledge) {
    return;
  }

  await postProjectAction("acknowledge", {
    type: loopInfo.type === "checkpoint" ? "checkpoint" : "guardrail",
    id: loopInfo.id,
  });
}

// 검토 리포트 객체를 생성한다.
function createReviewReport({ verdict, reason, riskAssessed, findings }) {
  const createdAt = new Date().toISOString();

  return {
    id: `review-${Date.now()}`,
    proposalId: proposal.id,
    verdict,
    reviewer: { type: "human", provider: "human", model: null },
    riskAssessed,
    findings: Array.isArray(findings) ? findings : [],
    reason,
    createdAt,
  };
}

// 검토 리포트를 목록에 추가한다.
function appendReviewReport(currentReport, report) {
  const nextReport = normalizeReviewReport(currentReport);
  return {
    ...nextReport,
    reports: [...nextReport.reports, report],
  };
}

// 특정 제안의 검토 리포트를 시간순으로 반환한다.
function getReportsForProposal(proposalId) {
  return (reviewReport.reports ?? [])
    .filter((report) => report.proposalId === proposalId)
    .slice()
    .sort((a, b) => new Date(a.createdAt).getTime() - new Date(b.createdAt).getTime());
}

// 특정 제안의 최신 검토 리포트를 반환한다.
function getLatestReportForProposal(proposalId) {
  const reports = getReportsForProposal(proposalId);
  return reports[reports.length - 1] ?? null;
}

// 제안 편집에서 생성된 finding 목록을 반환한다.
function getEditFindings(currentProposal) {
  return Array.isArray(currentProposal?.editFindings) ? currentProposal.editFindings : [];
}

// 구조화된 변경 항목으로 제안 위험도를 산정한다.
function assessProposalRisk(currentDefinition, currentProposal) {
  const metrics = getProposalMetrics(currentProposal);
  const rules = currentDefinition.reviewPolicy?.riskRules ?? [];
  let assessedRisk = "high";

  for (const rule of rules) {
    if (rule.if && evaluateRiskExpression(rule.if, metrics)) {
      assessedRisk = normalizeRisk(rule.then) ?? "high";
      break;
    }

    if (rule.default) {
      assessedRisk = normalizeRisk(rule.default) ?? "high";
    }
  }

  const providedRisk = normalizeRisk(currentProposal?.riskLevel);

  return {
    risk: assessedRisk,
    providedRisk,
    mismatch: Boolean(providedRisk && providedRisk !== assessedRisk),
    metrics,
  };
}

// 제안 변경 수와 최대 증감률을 계산한다.
function getProposalMetrics(currentProposal) {
  const changes = Array.isArray(currentProposal?.changes) ? currentProposal.changes.map(normalizeChange) : [];
  const numericDeltas = changes
    .map((change) => getValueDeltaPercent(change.before, change.after))
    .filter((value) => Number.isFinite(value));

  return {
    changeCount: changes.length,
    maxValueDeltaPercent: numericDeltas.length > 0 ? Math.max(...numericDeltas) : 0,
  };
}

// 위험도 규칙 표현식을 평가한다.
function evaluateRiskExpression(expression, metrics) {
  return expression.split("&&").every((part) => {
    const match = part.trim().match(/^(changeCount|maxValueDeltaPercent)\s*(<=|>=|<|>|===|==)\s*(-?\d+(?:\.\d+)?)$/);

    if (!match) {
      return false;
    }

    const left = Number(metrics[match[1]]);
    const operator = match[2];
    const right = Number(match[3]);

    if (operator === "<=") return left <= right;
    if (operator === ">=") return left >= right;
    if (operator === "<") return left < right;
    if (operator === ">") return left > right;
    return left === right;
  });
}

// 자동화 후보 배지 표시 여부를 계산한다.
function isAutoApproveTarget(currentDefinition, risk) {
  const policy = currentDefinition.reviewPolicy?.autoApprove;

  if (!policy) {
    return false;
  }

  const riskIndex = RISK_ORDER.indexOf(risk);
  const maxRiskIndex = RISK_ORDER.indexOf(normalizeRisk(policy.maxRisk) ?? "");

  return riskIndex !== -1 && maxRiskIndex !== -1 && riskIndex <= maxRiskIndex;
}

// 위험도 값을 허용 enum으로 정규화한다.
function normalizeRisk(value) {
  const normalized = String(value ?? "").toLowerCase();
  return RISK_ORDER.includes(normalized) ? normalized : null;
}

// 제안 lifecycle 값을 허용 enum으로 정규화한다.
function normalizeLifecycle(value) {
  const normalized = String(value ?? "").toLowerCase();
  const allowed = ["draft", "candidate", "submitted", "decided", "superseded"];
  return allowed.includes(normalized) ? normalized : "draft";
}

// 이전 proposal status 값을 lifecycle 값으로 변환한다.
function legacyStatusToLifecycle(status) {
  if (status === "approved" || status === "rejected") {
    return "decided";
  }

  if (status === "pending_review") {
    return "submitted";
  }

  return status ?? "draft";
}

// 숫자 변경 항목의 증감률을 계산한다.
function getValueDeltaPercent(before, after) {
  if (typeof before !== "number" || typeof after !== "number") {
    return NaN;
  }

  if (before === 0) {
    return after === 0 ? 0 : 100;
  }

  return Math.abs((after - before) / before) * 100;
}

// 프로젝트 설정을 ID로 찾는다.
function getProject(projectId) {
  return (projectsConfig?.projects ?? []).find((project) => project.id === projectId) ?? null;
}

// 프로젝트 경로 끝에 슬래시를 보장한다.
function normalizeProjectPath(path) {
  return path.endsWith("/") ? path : `${path}/`;
}

// 프로젝트 경로와 파일명을 결합한다.
function projectFilePath(projectPath, fileName) {
  return `${projectPath}${fileName}`;
}

// 프로젝트 API 파일 경로를 만든다.
function apiProjectFilePath(projectId, fileName) {
  return `/api/projects/${encodeURIComponent(projectId)}/${fileName}`;
}

// 프로젝트 API 액션 경로를 만든다.
function apiProjectActionPath(action) {
  return `/api/projects/${encodeURIComponent(activeProject.id)}/actions/${action}`;
}

// 서버 폴링을 시작한다.
function startPolling() {
  window.clearInterval(pollTimer);
  pollTimer = window.setInterval(() => {
    refreshRuntimeData().catch(() => {});
  }, POLL_INTERVAL_MS);
}

// 서버에서 런타임 상태를 다시 읽는다.
async function refreshRuntimeData() {
  if (!serverBacked || !activeProject || scenarioRunning || editingChangeIndex !== null) {
    return;
  }

  const [nextState, nextRunLog, nextProposal, nextReviewReport, nextMeasurement, nextCycleSummary] = await Promise.all([
    loadVersionedJson(apiProjectFilePath(activeProject.id, "state"), PROJECT_FILE_NAMES.state),
    loadVersionedJson(apiProjectFilePath(activeProject.id, "runlog"), PROJECT_FILE_NAMES.runLog),
    loadOptionalVersionedJson(apiProjectFilePath(activeProject.id, "proposal"), PROJECT_FILE_NAMES.proposal, null),
    loadOptionalVersionedJson(apiProjectFilePath(activeProject.id, "reviews"), PROJECT_FILE_NAMES.reviewReport, {
      schemaVersion: EXPECTED_SCHEMA_VERSION,
      reports: [],
    }),
    loadOptionalVersionedJson(apiProjectFilePath(activeProject.id, "measurement"), PROJECT_FILE_NAMES.measurement, null),
    fetchOptionalJson(`/api/projects/${encodeURIComponent(activeProject.id)}/cycle-summary`, null),
  ]);
  globalInbox = await loadGlobalInbox();

  applyServerBundle({
    state: nextState,
    runLog: nextRunLog,
    proposal: nextProposal,
    reviewReport: nextReviewReport,
    measurement: nextMeasurement,
    cycleSummary: nextCycleSummary,
  });
}

// 전역 결재 인박스를 서버에서 읽는다.
async function loadGlobalInbox() {
  try {
    return normalizeInbox(await fetchJson("/api/inbox"));
  } catch {
    return { schemaVersion: EXPECTED_SCHEMA_VERSION, items: [] };
  }
}

// 서버 액션을 호출하고 화면 상태를 갱신한다.
async function postProjectAction(action, body) {
  let response;

  try {
    response = await fetch(apiProjectActionPath(action), {
      method: "POST",
      headers: buildActionHeaders(),
      body: JSON.stringify(body ?? {}),
    });

    if (response.status === 401) {
      actionToken = window.prompt(t("remote.tokenPrompt")) || null;

      if (!actionToken) {
        showActionError({ reason: t("remote.tokenRequired"), reasonCode: "auth.token_required" });
        return false;
      }

      response = await fetch(apiProjectActionPath(action), {
        method: "POST",
        headers: buildActionHeaders(),
        body: JSON.stringify(body ?? {}),
      });
    }
  } catch (error) {
    showActionError(error);
    return false;
  }

  const payload = await response.json().catch(() => null);

  if (!response.ok) {
    if (payload?.reasonCode === "review.already_decided") {
      window.alert(t("approval.alreadyDecided"));
      await refreshRuntimeData();
      return false;
    }

    showActionError(payload ?? { reason: response.statusText, reasonCode: response.status });
    return false;
  }

  applyServerBundle(payload);
  return true;
}

// 액션 요청 헤더를 만든다. 토큰은 메모리에만 보관하고 있으면 첨부한다.
function buildActionHeaders() {
  const headers = { "Content-Type": "application/json" };

  if (actionToken) {
    headers["X-Action-Token"] = actionToken;
  }

  return headers;
}

// 서버 응답 묶음을 메모리 상태에 적용한다.
function applyServerBundle(bundle) {
  if (!bundle) {
    return;
  }

  workflowState = bundle.state ?? workflowState;
  runLog = bundle.runLog ?? runLog;
  proposal = normalizeProposal(bundle.proposal ?? proposal);
  reviewReport = normalizeReviewReport(bundle.reviewReport ?? reviewReport);
  measurement = normalizeMeasurement(bundle.measurement ?? measurement);
  cycleSummary = bundle.cycleSummary ?? cycleSummary;
  projectBaseline = {
    workflowState: cloneData(workflowState),
    runLog: cloneData(runLog),
    proposal: cloneData(proposal),
    measurement: cloneData(measurement),
  };
  ensureSelectedStage();
  render();
}

// 액션 실패 사유를 표시한다.
function showActionError(error) {
  const reason = error?.reason ?? error?.message ?? String(error);
  const reasonCode = error?.reasonCode ? `${error.reasonCode}: ` : "";
  window.alert(`${reasonCode}${reason}`);
}

// 현재 시나리오 이벤트 목록을 반환한다.
function getScenarioEvents() {
  return Array.isArray(scenario?.events) ? scenario.events : [];
}

// 현재 프로젝트에서 서버 측정을 실행할 수 있는지 확인한다.
function canRunMeasurement() {
  return serverBacked && Boolean(definition?.measurementProvider?.id);
}

// 선택 단계가 유효하도록 보정한다.
function ensureSelectedStage() {
  if (!definition || !workflowState) {
    return;
  }

  if (!getStage(definition, selectedStageId)) {
    selectedStageId = getStage(definition, workflowState.currentStage)
      ? workflowState.currentStage
      : definition.stages[0]?.id;
  }
}

// 제안 데이터 존재 여부를 확인한다.
function hasProposalData(value) {
  return value !== null && typeof value === "object" && Boolean(value.id || value.title || Array.isArray(value.changes));
}

// 제안 데이터를 화면 계약에 맞게 정규화한다.
function normalizeProposal(value) {
  if (!hasProposalData(value)) {
    return value;
  }

  const nextProposal = cloneData(value);
  nextProposal.revisionOf = nextProposal.revisionOf ?? null;
  nextProposal.lifecycle = normalizeLifecycle(nextProposal.lifecycle ?? legacyStatusToLifecycle(nextProposal.status));
  delete nextProposal.status;
  delete nextProposal.reviewedAt;
  delete nextProposal.rejectionReason;

  if (Array.isArray(nextProposal.changes)) {
    nextProposal.changes = nextProposal.changes.map(normalizeChange);
  }

  return nextProposal;
}

// 변경 항목을 구조화된 형태로 정규화한다.
function normalizeChange(change, index = 0) {
  if (change && typeof change === "object" && "path" in change) {
    return {
      path: String(change.path),
      before: change.before,
      after: change.after,
      note: change.note ?? "",
    };
  }

  return {
    path: `changes[${index}]`,
    before: "",
    after: change ?? "",
    note: "",
  };
}

// 검토 리포트 컨테이너를 정규화한다.
function normalizeReviewReport(value) {
  if (!value || typeof value !== "object" || !Array.isArray(value.reports)) {
    return { schemaVersion: EXPECTED_SCHEMA_VERSION, reports: [] };
  }

  return cloneData(value);
}

// 블루프린트 컨테이너를 정규화한다.
function normalizeBlueprint(value) {
  if (!value || typeof value !== "object" || !Array.isArray(value.items)) {
    return { schemaVersion: EXPECTED_SCHEMA_VERSION, items: [] };
  }

  return cloneData(value);
}

// 측정 결과 컨테이너를 정규화한다.
function normalizeMeasurement(value) {
  if (!value || typeof value !== "object" || !Array.isArray(value.metrics)) {
    return null;
  }

  return cloneData(value);
}

// 인박스 데이터를 화면 계약에 맞게 정규화한다.
function normalizeInbox(value) {
  return {
    schemaVersion: value?.schemaVersion ?? EXPECTED_SCHEMA_VERSION,
    items: Array.isArray(value?.items) ? value.items : [],
    autoApprovals: Array.isArray(value?.autoApprovals) ? value.autoApprovals : [],
  };
}

// 제안 패치를 적용한다.
function applyProposalPatch(currentProposal, patch) {
  if (!patch || !hasProposalData(currentProposal)) {
    return currentProposal;
  }

  return normalizeProposal({ ...currentProposal, ...patch });
}

// 화면 테마 값을 적용한다.
function setTheme(nextTheme) {
  theme = nextTheme === "dark" ? "dark" : "light";
  document.documentElement.dataset.theme = theme;
}

// 초기 테마 값을 시스템 설정에서 읽는다.
function getInitialTheme() {
  return window.matchMedia?.("(prefers-color-scheme: dark)")?.matches ? "dark" : "light";
}

// 승인과 거절 키보드 단축키를 처리한다.
function handleKeyboardShortcut(event) {
  if (event.ctrlKey || event.metaKey || event.altKey || isTypingTarget(document.activeElement)) {
    return;
  }

  const key = event.key.toLowerCase();
  const context = definition && workflowState ? getReviewContext() : null;

  if (!context?.canReview) {
    return;
  }

  if (key === "a") {
    event.preventDefault();
    approveProposal();
  }

  if (key === "r") {
    event.preventDefault();
    rejectProposal();
  }
}

// 현재 포커스가 입력 대상인지 확인한다.
function isTypingTarget(node) {
  if (!node) {
    return false;
  }

  return ["INPUT", "TEXTAREA", "SELECT"].includes(node.tagName) || node.isContentEditable;
}

// 현재 언어 문자열을 정적 UI에 적용한다.
function applyLanguage() {
  document.documentElement.lang = language;
  elements.productEyebrow.textContent = t("header.eyebrow");
  elements.projectSelectLabel.textContent = t("header.projectSelect");
  elements.totalCostLabel.textContent = t("header.totalCost");
  elements.subscriptionCallsLabel.textContent = t("header.subscriptionCalls");
  elements.languageToggle.textContent = t("header.languageToggle");
  elements.themeToggle.textContent = theme === "dark" ? t("theme.light") : t("theme.dark");
  elements.replayScenario.textContent = t("buttons.replayScenario");
  elements.downloadJson.textContent = t("buttons.downloadJson");
  elements.pipelineEyebrow.textContent = t("panels.pipelineEyebrow");
  elements.pipelineTitle.textContent = t("panels.pipelineTitle");
  elements.detailEyebrow.textContent = t("panels.detailEyebrow");
  elements.approvalEyebrow.textContent = t("panels.approvalEyebrow");
  elements.logEyebrow.textContent = t("panels.logEyebrow");
  elements.logTitle.textContent = t("panels.logTitle");

  if (!workflowState) {
    elements.detailTitle.textContent = t("panels.detailTitleLoading");
  }
}

// 문서 제목의 승인 대기 개수를 갱신한다.
function updateDocumentTitle() {
  const baseTitle = t("documentTitle");
  const pendingCount = getInboxItems().length;
  document.title = pendingCount > 0 ? `(${pendingCount}) ${baseTitle}` : baseTitle;
}

// 언어 키를 찾아 템플릿을 치환한다.
function t(key, replacements = {}) {
  const value = key.split(".").reduce((current, part) => current?.[part], translations[language]);
  const template = typeof value === "string" ? value : key;

  return applyTemplate(template, replacements);
}

// 지정된 번역 맵에서 값을 조회한다.
function translateFromMap(mapName, key) {
  const value = translations[language]?.[mapName]?.[key];
  return typeof value === "string" ? value : key;
}

// 문자열 템플릿의 자리표시자를 치환한다.
function applyTemplate(template, replacements = {}) {
  return Object.entries(replacements).reduce((text, [name, replacement]) => {
    return text.replaceAll(`{${name}}`, String(replacement));
  }, template);
}

// 데이터 로드 실패 화면을 렌더링한다.
function renderLoadError(error) {
  applyLanguage();
  document.title = t("documentTitle");
  elements.projectName.textContent = t("load.failed");
  elements.detailTitle.textContent = t("load.dataUnavailable");
  elements.stageDetail.replaceChildren(
    createElement("p", {
      className: "empty-state",
      text: String(error),
    }),
  );
}

// 상태 배지 요소를 생성한다.
function createStatusBadge(status, label = formatStatus(status)) {
  return createElement("span", {
    className: `status-badge status-${status}`,
    text: label,
  });
}

// 기존 상태 배지 요소를 갱신한다.
function setStatusBadge(node, status, label = formatStatus(status)) {
  node.className = `status-badge status-${status}`;
  node.textContent = label;
}

// 단계의 표시용 배지 상태를 계산한다.
function getStageBadgeStatus(stageId) {
  return getBlockKind(workflowState, stageId) ?? getStageStatus(workflowState, stageId);
}

// 헤더 전체 상태 배지의 색상 키를 계산한다.
function getOverallBadgeStatus() {
  const loopInfo = getLoopInfo();

  if (loopInfo) {
    return loopInfo.badgeStatus;
  }

  if (workflowState.mode === "degraded") {
    return "mode_degraded";
  }

  return workflowState.overallStatus;
}

// 헤더 전체 상태 배지의 표시명을 계산한다.
function getOverallBadgeLabel() {
  const loopInfo = getLoopInfo();

  if (loopInfo) {
    return loopInfo.badgeLabel;
  }

  if (workflowState.mode === "degraded") {
    return formatMode(workflowState.mode);
  }

  return formatStatus(workflowState.overallStatus);
}

// 루프 정지 또는 일시정지 표시 정보를 계산한다.
function getLoopInfo() {
  const loopState = workflowState?.loopState ?? "running";

  if (loopState === "running") {
    return null;
  }

  const pauseReason = workflowState.pausedBy;
  const haltReason = workflowState.haltedBy;

  if (pauseReason?.type === "checkpoint") {
    return {
      type: "checkpoint",
      id: pauseReason.checkpointId ?? workflowState.checkpointId ?? "",
      canAcknowledge: true,
      badgeStatus: "loop_paused",
      badgeLabel: t("halt.checkpointBadge"),
      detail: t("halt.checkpointDetail", {
        checkpointId: pauseReason.checkpointId ?? workflowState.checkpointId ?? t("approval.unknown"),
        loopIteration: workflowState.loopIteration ?? 0,
      }),
    };
  }

  const breaches = Array.isArray(haltReason?.breaches) ? haltReason.breaches : [];

  return {
    type: haltReason?.type === "guardrail" ? "guardrail" : "loop",
    id: haltReason?.type === "guardrail" ? "guardrail" : "",
    canAcknowledge: haltReason?.type === "guardrail",
    badgeStatus: `loop_${loopState}`,
    badgeLabel: haltReason?.type === "guardrail" ? t("halt.guardrailBadge") : formatLoopState(loopState),
    detail:
      haltReason?.type === "guardrail"
        ? t("halt.guardrailDetail", {
            text: breaches.map((breach) => `${breach.type} ${breach.actual} >= ${breach.limit}`).join(", "),
          })
        : "",
  };
}

// 메타 정보 행을 생성한다.
function createMetaItem(label, value) {
  const item = createElement("div", { className: "meta-item" });
  item.append(
    createElement("span", { text: label }),
    createElement("strong", { text: value ?? t("approval.none") }),
  );
  return item;
}

// DOM 요소를 옵션에 맞게 생성한다.
function createElement(tagName, options = {}) {
  const node = document.createElement(tagName);

  if (options.className) {
    node.className = options.className;
  }

  if (options.text !== undefined) {
    node.textContent = options.text;
  }

  if (options.attributes) {
    Object.entries(options.attributes).forEach(([name, value]) => {
      node.setAttribute(name, value);
    });
  }

  return node;
}

// 실행 로그 이벤트를 표시 문자열로 변환한다.
function formatEvent(entry) {
  const eventKey = entry.event ?? "unknown.event";
  const template = translations[language]?.events?.[eventKey];
  const params = {
    text: "",
    failedChecks: "",
    editedSuffix: "",
    ...formatEventParams(entry.params ?? {}),
  };

  if (typeof template !== "string") {
    return `${eventKey} ${JSON.stringify(entry.params ?? {})}`;
  }

  return applyTemplate(template, params);
}

// 이벤트 파라미터를 표시 문자열로 변환한다.
function formatEventParams(params) {
  return Object.entries(params).reduce((formatted, [key, value]) => {
    if (key === "stage" || key === "targetStage") {
      formatted[key] = resolveStageName(value);
    } else if (key === "status") {
      formatted[key] = formatStatus(value);
    } else if (key === "verdict") {
      formatted[key] = formatVerdict(value);
    } else if (key === "failedChecks") {
      formatted[key] = formatFailedGateChecks(value);
    } else if (key === "edited") {
      formatted[key] = value ? "true" : "false";
      formatted.editedSuffix = value ? t("logs.editedSuffix") : "";
    } else if (Array.isArray(value) || (value && typeof value === "object")) {
      formatted[key] = JSON.stringify(value);
    } else {
      formatted[key] = value ?? "";
    }

    return formatted;
  }, {});
}

// 실패한 게이트 조건 목록을 표시 문자열로 변환한다.
function formatFailedGateChecks(checks) {
  if (!Array.isArray(checks)) {
    return "";
  }

  return checks
    .map((check) => {
      const allowed = Array.isArray(check.mustBe) ? check.mustBe.map(formatStatus).join(", ") : "";
      return `${resolveStageName(check.stage)}: ${formatStatus(check.actual)} / ${allowed}`;
    })
    .join(", ");
}

// 숫자를 USD 금액으로 표시한다.
function formatCurrency(value) {
  return new Intl.NumberFormat("en-US", {
    style: "currency",
    currency: "USD",
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  }).format(value);
}

// 실행 로그 비용과 시도 정보를 표시한다.
function formatLogMeta(entry) {
  const estimatedUSD = Number(entry.cost?.estimatedUSD ?? 0);
  const subscriptionCalls = Number(entry.cost?.subscriptionCalls ?? 0);
  const role = formatCostRole(entry.cost?.role ?? "runtime");
  const attempt = Number(entry.attempt ?? 1);
  const retryText = attempt > 1 ? ` / ${t("runLog.retry", { count: attempt })}` : "";

  return `${formatCurrency(estimatedUSD)} / ${t("runLog.subscriptionCallsShort", { count: subscriptionCalls })} / ${role}${retryText}`;
}

// 구독 호출 수를 역할별로 표시한다.
function formatSubscriptionCallsByRole(currentRunLog) {
  const calls = sumSubscriptionCallsByRole(currentRunLog);
  return t("header.subscriptionCallsByRole", {
    runtime: calls.runtime,
    dev: calls.dev,
  });
}

// 비용 역할을 표시 문자열로 변환한다.
function formatCostRole(role) {
  return translateFromMap("costRoles", role === "dev" ? "dev" : "runtime");
}

// 실행자 정보를 표시 문자열로 변환한다.
function formatActor(producedBy) {
  if (!producedBy) {
    return t("approval.unknown");
  }

  return producedBy.model ? `${producedBy.provider} / ${producedBy.model}` : producedBy.provider;
}

// 검토자 정보를 표시 문자열로 변환한다.
function formatReviewer(reviewer) {
  if (!reviewer) {
    return t("approval.unknown");
  }

  const model = reviewer.model ? ` / ${reviewer.model}` : "";
  return `${reviewer.type}: ${reviewer.provider}${model}`;
}

// 날짜 값을 현재 언어의 짧은 형식으로 표시한다.
function formatDateTime(value) {
  if (!value) {
    return t("approval.unknown");
  }

  return new Intl.DateTimeFormat(language === "ko" ? "ko-KR" : "en", {
    month: "short",
    day: "2-digit",
    hour: "2-digit",
    minute: "2-digit",
  }).format(new Date(value));
}

// 인박스 항목 종류를 표시 문자열로 변환한다.
function formatInboxKind(kind) {
  return translations[language]?.inbox?.kinds?.[kind] ?? kind;
}

// 대기 시작 시각을 경과 시간으로 표시한다.
function formatWaitingSince(value) {
  if (!value) {
    return t("approval.unknown");
  }

  const elapsedMs = Math.max(0, Date.now() - new Date(value).getTime());
  const hours = Math.floor(elapsedMs / 3600000);
  const days = Math.floor(hours / 24);

  if (days > 0) {
    return t("inbox.waitingDays", { count: days });
  }

  return t("inbox.waitingHours", { count: hours });
}

// 현재 프로젝트 제안의 전역 인박스 대기 시각을 찾는다.
function getCurrentProposalWaitingSince() {
  return getInboxItems().find((item) => item.projectId === activeProject?.id && item.proposalId === proposal?.id)?.waitingSince;
}

// 전역 인박스 항목 목록을 반환한다.
function getInboxItems() {
  return Array.isArray(globalInbox?.items) ? globalInbox.items : [];
}

// 전역 인박스의 자동 결재 감사 항목 목록을 반환한다.
function getAutoApprovalItems() {
  return Array.isArray(globalInbox?.autoApprovals) ? globalInbox.autoApprovals : [];
}

// 프로젝트별 인박스 항목 수를 계산한다.
function getInboxCountsByProject() {
  const counts = new Map();

  getInboxItems().forEach((item) => {
    counts.set(item.projectId, (counts.get(item.projectId) ?? 0) + 1);
  });

  return counts;
}

// 검토 리포트에서 상위 AI 승인 횟수를 센다.
function countAutoApprovals(currentReviewReport) {
  return (currentReviewReport?.reports ?? []).filter((report) => {
    return report.verdict === "approved" && report.reviewer?.type === "ai" && report.reviewer?.role === "approver";
  }).length;
}

// 상태 또는 차단 종류를 표시 문자열로 변환한다.
function formatStatus(status) {
  return (
    translations[language]?.statuses?.[status] ??
    translations[language]?.blockKinds?.[status] ??
    translations[language]?.loopStates?.[status] ??
    translations[language]?.modes?.[status] ??
    status
  );
}

// 검토 판정을 표시 문자열로 변환한다.
function formatVerdict(verdict) {
  return translateFromMap("verdicts", verdict);
}

// 제안 lifecycle을 표시 문자열로 변환한다.
function formatLifecycle(lifecycle) {
  return translateFromMap("lifecycles", lifecycle);
}

// 루프 상태를 표시 문자열로 변환한다.
function formatLoopState(loopState) {
  return translateFromMap("loopStates", loopState);
}

// 실행 모드를 표시 문자열로 변환한다.
function formatMode(mode) {
  return translateFromMap("modes", mode);
}

// 최신 검토 판정을 표시 문자열로 변환한다.
function formatLatestVerdict(proposalId) {
  return getLatestReportForProposal(proposalId)?.verdict
    ? formatVerdict(getLatestReportForProposal(proposalId).verdict)
    : t("approval.none");
}

// 위험도를 표시 문자열로 변환한다.
function formatRisk(risk) {
  return t(`risk.${risk}`);
}

// 단계 ID를 단계 표시명으로 변환한다.
function resolveStageName(stageId) {
  return getStage(definition, stageId)?.name ?? stageId;
}

// 값을 CSS 클래스 조각으로 사용할 수 있게 변환한다.
function sanitizeClassName(value) {
  return String(value).replace(/[^a-zA-Z0-9_-]/g, "-");
}

// diff 값을 읽기 좋은 문자열로 변환한다.
function formatValue(value) {
  if (typeof value === "number") {
    return formatNumber(value);
  }

  if (value === "") {
    return "empty";
  }

  return String(value);
}

// 편집 입력값으로 사용할 문자열을 만든다.
function formatEditableValue(value) {
  return value === null || value === undefined ? "" : String(value);
}

// 숫자를 짧은 소수 형식으로 표시한다.
function formatNumber(value) {
  return new Intl.NumberFormat("en-US", {
    maximumFractionDigits: 2,
  }).format(value);
}

// 이전 값과 이후 값의 증감률을 표시한다.
function formatDelta(before, after) {
  const delta = getValueDeltaPercent(before, after);

  if (!Number.isFinite(delta)) {
    return t("diff.changed");
  }

  const sign = typeof before === "number" && typeof after === "number" && after - before >= 0 ? "+" : "-";
  return `${sign}${formatNumber(delta)}%`;
}

// 편집 문자열을 기존 값 타입에 맞게 파싱한다.
function parseEditedValue(value, previousValue) {
  const trimmed = value.trim();

  if (typeof previousValue === "number") {
    const numeric = Number(trimmed);
    return Number.isFinite(numeric) ? numeric : previousValue;
  }

  if (typeof previousValue === "boolean") {
    return trimmed.toLowerCase() === "true";
  }

  return trimmed;
}
