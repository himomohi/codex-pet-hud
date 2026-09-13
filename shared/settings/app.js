const bridge = window.webkit?.messageHandlers?.settings;
const $ = (id) => document.getElementById(id);
const potionStyles = {
  classic: { name: "기본 포션" },
  "celestial-orb": { name: "천청 오브", top: 25, bottom: 80 },
  "rose-heart": { name: "장미 하트", top: 28, bottom: 81 },
  "amber-star": { name: "호박빛 별", top: 29, bottom: 80 },
  "lunar-crescent": { name: "보랏빛 초승달", top: 28, bottom: 83 },
  "verdant-leaf": { name: "신록 잎새", top: 29, bottom: 85 },
};
const potionStyleControls = Array.from(document.querySelectorAll('input[name="potionStyle"]'));
const normalizePotionStyle = (value) => Object.hasOwn(potionStyles, value) ? value : "classic";
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
  potionStyle: "classic",
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
  state.potionStyle = normalizePotionStyle(potionStyleControls.find((control) => control.checked)?.value);
  state.scale = Number(controls.scale.value) / 100;
  state.horizontalOffset = Number(controls.horizontalOffset.value);
  state.verticalOffset = Number(controls.verticalOffset.value);
  state.potionGap = Number(controls.potionGap.value);
  state.usageAlertsEnabled = controls.usageAlertsEnabled.checked;
  state.nativeNotificationsEnabled = controls.nativeNotificationsEnabled.checked;
  state.alertThresholds = [20, 10, 5].filter((value) => controls[`alert${value}`].checked);
  state.autoCleanup = controls.autoCleanup.checked;
}

function fitPreview() {
  const stage = $("previewStage");
  const hud = $("hudPreview");
  if (!stage.clientWidth || !stage.clientHeight || !hud.offsetWidth || !hud.offsetHeight) return;

  const padding = 24;
  const hint = stage.querySelector(".preview-hint");
  const bottom = Math.max(padding + 1, Math.min(stage.clientHeight - padding, hint.offsetTop - 16));
  const availableWidth = Math.max(1, stage.clientWidth - padding * 2);
  const availableHeight = bottom - padding;
  // 저장된 크기와 위치는 유지하고 미리보기에서만 전체 HUD가 보이도록 맞춥니다.
  const previewScale = Math.min(state.scale, availableWidth / hud.offsetWidth, availableHeight / hud.offsetHeight);
  const halfWidth = hud.offsetWidth * previewScale / 2;
  const halfHeight = hud.offsetHeight * previewScale / 2;
  const centerX = stage.clientWidth * .5 + state.horizontalOffset * .35;
  const centerY = stage.clientHeight * .48 - state.verticalOffset * .35;
  const clamp = (value, min, max) => Math.min(max, Math.max(min, value));
  hud.style.setProperty("--scale", previewScale);
  hud.style.left = `${clamp(centerX, padding + halfWidth, stage.clientWidth - padding - halfWidth)}px`;
  hud.style.top = `${clamp(centerY, padding + halfHeight, bottom - halfHeight)}px`;
}

function renderPreview() {
  const hud = $("hudPreview");
  hud.style.setProperty("--gap", `${state.potionGap}px`);

  const style = potionStyles[state.potionStyle];
  $("selectedPotionStyle").textContent = `현재 선택: ${style.name}`;
  document.querySelectorAll(".hud-preview .potion").forEach((potion, index) => {
    const remaining = index === 0 ? .93 : .99;
    potion.classList.toggle("styled", state.potionStyle !== "classic");
    potion.querySelector(".potion-glass .liquid").style.height = `${remaining * 100}%`;
    if (state.potionStyle === "classic") return;
    const mask = potion.querySelector(".potion-mask");
    const frame = potion.querySelector(".potion-frame");
    const assetPath = `potions/${state.potionStyle}`;
    frame.src = `${assetPath}-frame.png`;
    mask.style.setProperty("--potion-mask", `url("${window.potionMaskImages[state.potionStyle]}")`);
    const liquid = mask.querySelector(".liquid");
    liquid.style.bottom = `${96 - style.bottom}px`;
    liquid.style.height = `${(style.bottom - style.top) * remaining}px`;
  });

  $("scaleValue").textContent = `${Math.round(state.scale * 100)}%`;
  $("horizontalOffsetValue").textContent = `${Math.round(state.horizontalOffset)}px`;
  $("verticalOffsetValue").textContent = `${Math.round(state.verticalOffset)}px`;
  $("potionGapValue").textContent = `${Math.round(state.potionGap)}px`;
  $("thresholdOptions").classList.toggle("disabled", !state.usageAlertsEnabled);
  document.querySelectorAll('input[type="range"]').forEach(rangeFill);
  fitPreview();
}

function scheduleSave() {
  if (applyingNativeState) return;
  readControls();
  renderPreview();
  clearTimeout(saveTimer);
  saveTimer = setTimeout(flushSave, 150);
}

function flushSave() {
  if (!saveTimer) return;
  clearTimeout(saveTimer);
  saveTimer = undefined;
  post({ type: "save", settings: state });
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
  state.potionStyle = normalizePotionStyle(state.potionStyle);
  potionStyleControls.forEach((control) => { control.checked = control.value === state.potionStyle; });
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

potionStyleControls.forEach((control) => control.addEventListener("change", scheduleSave));

$("cleanupButton").addEventListener("click", () => {
  const button = $("cleanupButton");
  button.classList.add("loading");
  button.disabled = true;
  button.querySelector("span").textContent = "정리 중…";
  post({ type: "cleanup" });
});

$("resetButton").addEventListener("click", () => {
  if (window.confirm("포션 디자인과 모든 설정을 기본값으로 되돌릴까요?")) {
    clearTimeout(saveTimer);
    saveTimer = undefined;
    post({ type: "reset" });
    showToast("기본값으로 되돌렸어요.");
  }
});

$("doneButton").addEventListener("click", () => {
  flushSave();
  post({ type: "close" });
});
window.applyNativeState({ settings: state });
const previewResizeObserver = new ResizeObserver(fitPreview);
previewResizeObserver.observe($("previewStage"));
previewResizeObserver.observe($("hudPreview"));
previewResizeObserver.observe($("previewStage").querySelector(".preview-hint"));
post({ type: "ready" });
