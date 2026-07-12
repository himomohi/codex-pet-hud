const bridge = window.webkit?.messageHandlers?.settings;
const $ = (id) => document.getElementById(id);
const controls = {
  scale: $("scale"),
  horizontalOffset: $("horizontalOffset"),
  verticalOffset: $("verticalOffset"),
  potionGap: $("potionGap"),
  usageAlertsEnabled: $("usageAlertsEnabled"),
  nativeNotificationsEnabled: $("nativeNotificationsEnabled"),
  alert20: $("alert20"),
  alert10: $("alert10"),
  alert5: $("alert5"),
  autoCleanup: $("autoCleanup"),
};

let state = {
  scale: 1,
  horizontalOffset: 0,
  verticalOffset: 0,
  potionGap: 10,
  usageAlertsEnabled: true,
  nativeNotificationsEnabled: false,
  alertThresholds: [20, 10, 5],
  autoCleanup: true,
  lastCleanupAt: null,
  lastFreedBytes: 0,
};
let saveTimer;
let applyingNativeState = false;
let toastTimer;

function post(message) {
  bridge?.postMessage(message);
}

function rangeFill(input) {
  const min = Number(input.min);
  const max = Number(input.max);
  const value = ((Number(input.value) - min) / (max - min)) * 100;
  input.style.setProperty("--value", `${value}%`);
}

function readControls() {
  state.scale = Number(controls.scale.value) / 100;
  state.horizontalOffset = Number(controls.horizontalOffset.value);
  state.verticalOffset = Number(controls.verticalOffset.value);
  state.potionGap = Number(controls.potionGap.value);
  state.usageAlertsEnabled = controls.usageAlertsEnabled.checked;
  state.nativeNotificationsEnabled = controls.nativeNotificationsEnabled.checked;
  state.alertThresholds = [20, 10, 5].filter((value) => controls[`alert${value}`].checked);
  state.autoCleanup = controls.autoCleanup.checked;
}

function renderPreview() {
  const hud = $("hudPreview");
  hud.style.setProperty("--scale", state.scale);
  hud.style.setProperty("--gap", `${state.potionGap}px`);
  hud.style.left = `calc(50% + ${state.horizontalOffset * .35}px)`;
  hud.style.top = `calc(48% - ${state.verticalOffset * .35}px)`;

  $("scaleValue").textContent = `${Math.round(state.scale * 100)}%`;
  $("horizontalOffsetValue").textContent = `${Math.round(state.horizontalOffset)}px`;
  $("verticalOffsetValue").textContent = `${Math.round(state.verticalOffset)}px`;
  $("potionGapValue").textContent = `${Math.round(state.potionGap)}px`;
  $("thresholdOptions").classList.toggle("disabled", !state.usageAlertsEnabled);
  document.querySelectorAll('input[type="range"]').forEach(rangeFill);
}

function scheduleSave() {
  if (applyingNativeState) return;
  readControls();
  renderPreview();
  clearTimeout(saveTimer);
  saveTimer = setTimeout(() => post({ type: "save", settings: state }), 150);
}

function formatBytes(bytes) {
  if (!bytes) return "0 B";
  const units = ["B", "KB", "MB", "GB"];
  let value = Number(bytes);
  let unit = 0;
  while (value >= 1024 && unit < units.length - 1) { value /= 1024; unit += 1; }
  return `${value >= 10 || unit === 0 ? value.toFixed(0) : value.toFixed(1)} ${units[unit]}`;
}

function formatDate(timestamp, fallback) {
  if (!timestamp) return fallback;
  return new Intl.DateTimeFormat("ko-KR", {
    month: "short", day: "numeric", hour: "numeric", minute: "2-digit"
  }).format(new Date(timestamp * 1000));
}

function renderMaintenance(cleanup = {}) {
  $("lastCleanup").textContent = formatDate(cleanup.cleanedAt, "아직 없음");
  $("freedSpace").textContent = formatBytes(cleanup.freedBytes);
  $("nextCleanup").textContent = state.autoCleanup
    ? formatDate(cleanup.nextCleanupAt, "곧 실행")
    : "자동 정리 꺼짐";
}

function showToast(message) {
  const toast = $("toast");
  toast.querySelector("p").textContent = message;
  toast.classList.add("show");
  clearTimeout(toastTimer);
  toastTimer = setTimeout(() => toast.classList.remove("show"), 2200);
}

window.applyNativeState = ({ settings, cleanup }) => {
  applyingNativeState = true;
  state = { ...state, ...settings };
  controls.scale.value = Math.round(state.scale * 100);
  controls.horizontalOffset.value = state.horizontalOffset;
  controls.verticalOffset.value = state.verticalOffset;
  controls.potionGap.value = state.potionGap;
  controls.usageAlertsEnabled.checked = state.usageAlertsEnabled;
  controls.nativeNotificationsEnabled.checked = state.nativeNotificationsEnabled;
  [20, 10, 5].forEach((value) => { controls[`alert${value}`].checked = state.alertThresholds.includes(value); });
  controls.autoCleanup.checked = state.autoCleanup;
  renderPreview();
  renderMaintenance(cleanup);
  requestAnimationFrame(() => { applyingNativeState = false; });
};

window.cleanupFinished = () => {
  const button = $("cleanupButton");
  button.classList.remove("loading");
  button.disabled = false;
  button.querySelector("span").textContent = "지금 정리";
  showToast("안전한 캐시 정리를 마쳤어요.");
};

Object.values(controls).forEach((control) => {
  control.addEventListener("input", () => {
    $("previewStage").classList.add("adjusting");
    scheduleSave();
  });
  control.addEventListener("change", () => {
    $("previewStage").classList.remove("adjusting");
    scheduleSave();
  });
});

$("cleanupButton").addEventListener("click", () => {
  const button = $("cleanupButton");
  button.classList.add("loading");
  button.disabled = true;
  button.querySelector("span").textContent = "정리 중…";
  post({ type: "cleanup" });
});

$("resetButton").addEventListener("click", () => {
  if (window.confirm("배치와 크기 설정을 기본값으로 되돌릴까요?")) {
    post({ type: "reset" });
    showToast("기본값으로 되돌렸어요.");
  }
});

$("doneButton").addEventListener("click", () => post({ type: "close" }));
post({ type: "ready" });
