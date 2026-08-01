"use strict";

const defaults = {
  kind: "keyboard",
  vk: 119,
  code: "F8",
  keyLabel: "F8",
  mouseAction: "",
  ctrl: false,
  shift: false,
  alt: false,
  win: false,
  behavior: "hold",
  tapDurationMs: 40,
  gmodOnly: true,
  showLabel: true
};

const keyboardPresets = [
  ...Array.from({ length: 12 }, (_, index) => ({
    id: `F${index + 1}`,
    label: `F${index + 1}`,
    vk: 0x70 + index,
    code: `F${index + 1}`
  })),
  ...Array.from("ABCDEFGHIJKLMNOPQRSTUVWXYZ", letter => ({
    id: `Key${letter}`,
    label: letter,
    vk: letter.charCodeAt(0),
    code: `Key${letter}`
  })),
  ...Array.from("0123456789", digit => ({
    id: `Digit${digit}`,
    label: digit,
    vk: digit.charCodeAt(0),
    code: `Digit${digit}`
  })),
  { id: "Space", label: "Space", vk: 0x20, code: "Space" },
  { id: "Tab", label: "Tab", vk: 0x09, code: "Tab" },
  { id: "Enter", label: "Enter", vk: 0x0D, code: "Enter" },
  { id: "Escape", label: "Escape", vk: 0x1B, code: "Escape" },
  { id: "Backspace", label: "Backspace", vk: 0x08, code: "Backspace" },
  { id: "CapsLock", label: "Caps Lock", vk: 0x14, code: "CapsLock" },
  { id: "ArrowUp", label: "Стрелка вверх", vk: 0x26, code: "ArrowUp" },
  { id: "ArrowDown", label: "Стрелка вниз", vk: 0x28, code: "ArrowDown" },
  { id: "ArrowLeft", label: "Стрелка влево", vk: 0x25, code: "ArrowLeft" },
  { id: "ArrowRight", label: "Стрелка вправо", vk: 0x27, code: "ArrowRight" },
  { id: "Insert", label: "Insert", vk: 0x2D, code: "Insert" },
  { id: "Delete", label: "Delete", vk: 0x2E, code: "Delete" },
  { id: "Home", label: "Home", vk: 0x24, code: "Home" },
  { id: "End", label: "End", vk: 0x23, code: "End" },
  { id: "PageUp", label: "Page Up", vk: 0x21, code: "PageUp" },
  { id: "PageDown", label: "Page Down", vk: 0x22, code: "PageDown" },
  { id: "Numpad0", label: "Num 0", vk: 0x60, code: "Numpad0" },
  { id: "Numpad1", label: "Num 1", vk: 0x61, code: "Numpad1" },
  { id: "Numpad2", label: "Num 2", vk: 0x62, code: "Numpad2" },
  { id: "Numpad3", label: "Num 3", vk: 0x63, code: "Numpad3" },
  { id: "Numpad4", label: "Num 4", vk: 0x64, code: "Numpad4" },
  { id: "Numpad5", label: "Num 5", vk: 0x65, code: "Numpad5" },
  { id: "Numpad6", label: "Num 6", vk: 0x66, code: "Numpad6" },
  { id: "Numpad7", label: "Num 7", vk: 0x67, code: "Numpad7" },
  { id: "Numpad8", label: "Num 8", vk: 0x68, code: "Numpad8" },
  { id: "Numpad9", label: "Num 9", vk: 0x69, code: "Numpad9" },
  { id: "NumpadMultiply", label: "Num *", vk: 0x6A, code: "NumpadMultiply" },
  { id: "NumpadAdd", label: "Num +", vk: 0x6B, code: "NumpadAdd" },
  { id: "NumpadSubtract", label: "Num -", vk: 0x6D, code: "NumpadSubtract" },
  { id: "NumpadDecimal", label: "Num .", vk: 0x6E, code: "NumpadDecimal" },
  { id: "NumpadDivide", label: "Num /", vk: 0x6F, code: "NumpadDivide" },
  { id: "Backquote", label: "` / ~", vk: 0xC0, code: "Backquote" },
  { id: "Minus", label: "- / _", vk: 0xBD, code: "Minus" },
  { id: "Equal", label: "= / +", vk: 0xBB, code: "Equal" },
  { id: "BracketLeft", label: "[ / {", vk: 0xDB, code: "BracketLeft" },
  { id: "BracketRight", label: "] / }", vk: 0xDD, code: "BracketRight" },
  { id: "Backslash", label: "\\ / |", vk: 0xDC, code: "Backslash" },
  { id: "Semicolon", label: "; / :", vk: 0xBA, code: "Semicolon" },
  { id: "Quote", label: "' / \"", vk: 0xDE, code: "Quote" },
  { id: "Comma", label: ", / <", vk: 0xBC, code: "Comma" },
  { id: "Period", label: ". / >", vk: 0xBE, code: "Period" },
  { id: "Slash", label: "/ / ?", vk: 0xBF, code: "Slash" }
];

const mousePresets = [
  { id: "mouse1", label: "Mouse 1 (левая)", action: "mouse1" },
  { id: "mouse2", label: "Mouse 2 (правая)", action: "mouse2" },
  { id: "mouse3", label: "Mouse 3 (колесо)", action: "mouse3" },
  { id: "mouse4", label: "Mouse 4", action: "mouse4" },
  { id: "mouse5", label: "Mouse 5", action: "mouse5" },
  { id: "wheelup", label: "Колесо вверх", action: "wheelup" },
  { id: "wheeldown", label: "Колесо вниз", action: "wheeldown" }
];

const codeMap = new Map(keyboardPresets.map(item => [item.code, item]));
const dom = {};
let websocket;
let propertyInspectorUuid;
let context;
let settings = { ...defaults };
let capturing = false;
let statusTimer;

function byId(id) {
  return document.getElementById(id);
}

function buildPresetOptions() {
  const keyboardGroup = document.createElement("optgroup");
  keyboardGroup.label = "Клавиатура";
  keyboardPresets.forEach(item => {
    const option = document.createElement("option");
    option.value = `keyboard:${item.id}`;
    option.textContent = item.label;
    keyboardGroup.appendChild(option);
  });

  const mouseGroup = document.createElement("optgroup");
  mouseGroup.label = "Мышь";
  mousePresets.forEach(item => {
    const option = document.createElement("option");
    option.value = `mouse:${item.id}`;
    option.textContent = item.label;
    mouseGroup.appendChild(option);
  });

  dom.preset.append(keyboardGroup, mouseGroup);
}

function modifierLabel() {
  const parts = [];
  if (settings.ctrl) parts.push("Ctrl");
  if (settings.shift) parts.push("Shift");
  if (settings.alt) parts.push("Alt");
  if (settings.win) parts.push("Win");
  parts.push(settings.keyLabel || "F8");
  return parts.join(" + ");
}

function render() {
  if (!dom.preset) return;

  const presetValue = settings.kind === "mouse"
    ? `mouse:${settings.mouseAction}`
    : `keyboard:${settings.code}`;

  if ([...dom.preset.options].some(option => option.value === presetValue)) {
    dom.preset.value = presetValue;
  }

  dom.ctrl.checked = Boolean(settings.ctrl);
  dom.shift.checked = Boolean(settings.shift);
  dom.alt.checked = Boolean(settings.alt);
  dom.win.checked = Boolean(settings.win);
  dom.behavior.value = settings.behavior === "tap" ? "tap" : "hold";
  dom.tapDuration.value = Number(settings.tapDurationMs) || 40;
  dom.gmodOnly.checked = settings.gmodOnly !== false;
  dom.showLabel.checked = settings.showLabel !== false;
  dom.current.textContent = `Текущая: ${modifierLabel()}`;

  const isWheel = settings.kind === "mouse" &&
    (settings.mouseAction === "wheelup" || settings.mouseAction === "wheeldown");
  dom.behavior.disabled = isWheel;
  dom.tapDurationRow.classList.toggle(
    "disabled",
    settings.behavior !== "tap" || isWheel
  );
}

function showStatus(message) {
  dom.status.textContent = message;
  clearTimeout(statusTimer);
  statusTimer = setTimeout(() => {
    dom.status.textContent = "";
  }, 1800);
}

function saveSettings() {
  render();
  if (!websocket || websocket.readyState !== WebSocket.OPEN || !context) {
    return;
  }

  websocket.send(JSON.stringify({
    event: "setSettings",
    context,
    payload: settings
  }));
  showStatus("Настройки сохранены");
}

function setKeyboardPreset(item, event) {
  settings.kind = "keyboard";
  settings.vk = item.vk;
  settings.code = item.code;
  settings.keyLabel = item.label;
  settings.mouseAction = "";

  if (event) {
    const mainIs = name => item.code === name || item.code.startsWith(name);
    settings.ctrl = event.ctrlKey && !mainIs("Control");
    settings.shift = event.shiftKey && !mainIs("Shift");
    settings.alt = event.altKey && !mainIs("Alt");
    settings.win = event.metaKey && !mainIs("Meta");
  }
}

function setMousePreset(item) {
  settings.kind = "mouse";
  settings.vk = 0;
  settings.code = "";
  settings.keyLabel = item.id.toUpperCase();
  settings.mouseAction = item.action;
  if (item.action === "wheelup" || item.action === "wheeldown") {
    settings.behavior = "tap";
  }
}

function stopCapture() {
  capturing = false;
  dom.capture.classList.remove("capturing");
  dom.capture.textContent = "Нажать клавишу для захвата";
}

function startCapture() {
  capturing = true;
  dom.capture.classList.add("capturing");
  dom.capture.textContent = "Нажмите нужную клавишу…";
}

function handleCapturedKey(event) {
  if (!capturing) return;
  event.preventDefault();
  event.stopPropagation();

  if (event.code === "Escape") {
    stopCapture();
    return;
  }

  const item = codeMap.get(event.code);
  if (!item) {
    showStatus(`Клавиша ${event.code || event.key} не поддерживается`);
    return;
  }

  setKeyboardPreset(item, event);
  stopCapture();
  saveSettings();
}

function wireUi() {
  dom.preset.addEventListener("change", event => {
    const [kind, id] = event.target.value.split(":");
    if (kind === "keyboard") {
      const item = keyboardPresets.find(entry => entry.id === id);
      if (item) setKeyboardPreset(item);
    } else {
      const item = mousePresets.find(entry => entry.id === id);
      if (item) setMousePreset(item);
    }
    saveSettings();
  });

  dom.capture.addEventListener("click", () => {
    if (capturing) stopCapture();
    else startCapture();
  });

  ["ctrl", "shift", "alt", "win"].forEach(name => {
    dom[name].addEventListener("change", () => {
      settings[name] = dom[name].checked;
      saveSettings();
    });
  });

  dom.behavior.addEventListener("change", () => {
    settings.behavior = dom.behavior.value;
    saveSettings();
  });

  dom.tapDuration.addEventListener("change", () => {
    const value = Math.max(10, Math.min(500, Number(dom.tapDuration.value) || 40));
    settings.tapDurationMs = value;
    saveSettings();
  });

  dom.gmodOnly.addEventListener("change", () => {
    settings.gmodOnly = dom.gmodOnly.checked;
    saveSettings();
  });

  dom.showLabel.addEventListener("change", () => {
    settings.showLabel = dom.showLabel.checked;
    saveSettings();
  });

  document.addEventListener("keydown", handleCapturedKey, true);
}

document.addEventListener("DOMContentLoaded", () => {
  dom.preset = byId("preset");
  dom.capture = byId("capture");
  dom.current = byId("current");
  dom.modifiers = byId("modifiers");
  dom.ctrl = byId("ctrl");
  dom.shift = byId("shift");
  dom.alt = byId("alt");
  dom.win = byId("win");
  dom.behavior = byId("behavior");
  dom.tapDurationRow = byId("tap-duration-row");
  dom.tapDuration = byId("tap-duration");
  dom.gmodOnly = byId("gmod-only");
  dom.showLabel = byId("show-label");
  dom.status = byId("status");

  buildPresetOptions();
  wireUi();
  render();
});

function connectElgatoStreamDeckSocket(
  inPort,
  inPropertyInspectorUuid,
  inRegisterEvent,
  inInfo,
  inActionInfo
) {
  propertyInspectorUuid = inPropertyInspectorUuid;
  const actionInfo = JSON.parse(inActionInfo);
  context = actionInfo.context;
  settings = { ...defaults, ...(actionInfo.payload?.settings || {}) };
  render();

  websocket = new WebSocket(`ws://127.0.0.1:${inPort}`);
  websocket.onopen = () => {
    websocket.send(JSON.stringify({
      event: inRegisterEvent,
      uuid: propertyInspectorUuid
    }));
  };
  websocket.onmessage = event => {
    const message = JSON.parse(event.data);
    if (message.event === "didReceiveSettings" && message.payload?.settings) {
      settings = { ...defaults, ...message.payload.settings };
      render();
    }
  };
}
