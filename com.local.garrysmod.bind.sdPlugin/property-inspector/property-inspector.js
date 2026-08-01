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
  ...Array.from({ length: 24 }, (_, index) => ({
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
  { id: "PrintScreen", label: "Print Screen", vk: 0x2C, code: "PrintScreen" },
  { id: "ScrollLock", label: "Scroll Lock", vk: 0x91, code: "ScrollLock" },
  { id: "Pause", label: "Pause / Break", vk: 0x13, code: "Pause" },
  { id: "ContextMenu", label: "Контекстное меню", vk: 0x5D, code: "ContextMenu" },
  { id: "ControlLeft", label: "Левый Ctrl", vk: 0xA2, code: "ControlLeft" },
  { id: "ControlRight", label: "Правый Ctrl", vk: 0xA3, code: "ControlRight" },
  { id: "ShiftLeft", label: "Левый Shift", vk: 0xA0, code: "ShiftLeft" },
  { id: "ShiftRight", label: "Правый Shift", vk: 0xA1, code: "ShiftRight" },
  { id: "AltLeft", label: "Левый Alt", vk: 0xA4, code: "AltLeft" },
  { id: "AltRight", label: "Правый Alt", vk: 0xA5, code: "AltRight" },
  { id: "MetaLeft", label: "Левый Win", vk: 0x5B, code: "MetaLeft" },
  { id: "MetaRight", label: "Правый Win", vk: 0x5C, code: "MetaRight" },
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
  { id: "NumLock", label: "Num Lock", vk: 0x90, code: "NumLock" },
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
  { id: "Slash", label: "/ / ?", vk: 0xBF, code: "Slash" },
  { id: "BrowserBack", label: "Браузер: назад", vk: 0xA6, code: "BrowserBack" },
  { id: "BrowserForward", label: "Браузер: вперёд", vk: 0xA7, code: "BrowserForward" },
  { id: "BrowserRefresh", label: "Браузер: обновить", vk: 0xA8, code: "BrowserRefresh" },
  { id: "BrowserStop", label: "Браузер: стоп", vk: 0xA9, code: "BrowserStop" },
  { id: "BrowserSearch", label: "Браузер: поиск", vk: 0xAA, code: "BrowserSearch" },
  { id: "BrowserFavorites", label: "Браузер: избранное", vk: 0xAB, code: "BrowserFavorites" },
  { id: "BrowserHome", label: "Браузер: главная", vk: 0xAC, code: "BrowserHome" },
  { id: "AudioVolumeMute", label: "Звук: выключить", vk: 0xAD, code: "AudioVolumeMute" },
  { id: "AudioVolumeDown", label: "Звук: тише", vk: 0xAE, code: "AudioVolumeDown" },
  { id: "AudioVolumeUp", label: "Звук: громче", vk: 0xAF, code: "AudioVolumeUp" },
  { id: "MediaTrackNext", label: "Медиа: следующий трек", vk: 0xB0, code: "MediaTrackNext" },
  { id: "MediaTrackPrevious", label: "Медиа: предыдущий трек", vk: 0xB1, code: "MediaTrackPrevious" },
  { id: "MediaStop", label: "Медиа: стоп", vk: 0xB2, code: "MediaStop" },
  { id: "MediaPlayPause", label: "Медиа: play / pause", vk: 0xB3, code: "MediaPlayPause" },
  { id: "LaunchMail", label: "Запустить почту", vk: 0xB4, code: "LaunchMail" },
  { id: "LaunchMediaPlayer", label: "Запустить медиаплеер", vk: 0xB5, code: "LaunchMediaPlayer" },
  { id: "LaunchApplication1", label: "Запустить приложение 1", vk: 0xB6, code: "LaunchApplication1" },
  { id: "LaunchApplication2", label: "Запустить приложение 2", vk: 0xB7, code: "LaunchApplication2" },
  { id: "Sleep", label: "Сон", vk: 0x5F, code: "Sleep" }
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
let capturedKeyboardGroup;
let websocket;
let propertyInspectorUuid;
let context;
let settings = { ...defaults };
let capturing = false;
let statusTimer;

function byId(id) {
  return document.getElementById(id);
}

function isValidVirtualKey(value) {
  return Number.isInteger(value) && value >= 1 && value <= 0xFF;
}

function appendKeyboardOption(group, item) {
  if (!group) return;

  const option = document.createElement("option");
  option.value = `keyboard:${item.id}`;
  option.textContent = item.label;
  group.appendChild(option);
}

function registerCapturedKeyboardPreset(item) {
  const existing = codeMap.get(item.code);
  if (existing) return existing;

  keyboardPresets.push(item);
  codeMap.set(item.code, item);
  appendKeyboardOption(capturedKeyboardGroup, item);
  return item;
}

function virtualKeyFromEvent(event) {
  const value = Number(event.keyCode || event.which || 0);
  return isValidVirtualKey(value) ? value : 0;
}

function keyLabelFromEvent(event) {
  const key = typeof event.key === "string" ? event.key.trim() : "";
  if (key && key !== "Unidentified" && key !== "Dead") {
    return key.length === 1 ? key.toUpperCase() : key;
  }

  if (event.code && event.code !== "Unidentified") {
    return event.code;
  }

  const vk = virtualKeyFromEvent(event);
  return `VK 0x${vk.toString(16).toUpperCase().padStart(2, "0")}`;
}

function keyboardPresetFromEvent(event) {
  const known = codeMap.get(event.code);
  if (known) return known;

  const vk = virtualKeyFromEvent(event);
  if (!vk) return null;

  const code = event.code && event.code !== "Unidentified"
    ? event.code
    : `VirtualKey${vk}`;

  return registerCapturedKeyboardPreset({
    id: code,
    label: keyLabelFromEvent(event),
    vk,
    code
  });
}

function virtualKeyPreset(value, label = "") {
  const vk = Number(value);
  if (!isValidVirtualKey(vk)) return null;

  const code = `VirtualKey${vk}`;
  return registerCapturedKeyboardPreset({
    id: code,
    label: label || `VK 0x${vk.toString(16).toUpperCase().padStart(2, "0")}`,
    vk,
    code
  });
}

function ensureCurrentKeyboardPreset() {
  if (settings.kind !== "keyboard" || codeMap.has(settings.code)) return;

  const vk = Number(settings.vk);
  if (!isValidVirtualKey(vk)) return;

  const code = settings.code && !settings.code.includes(":")
    ? settings.code
    : `VirtualKey${vk}`;
  const item = registerCapturedKeyboardPreset({
    id: code,
    label: settings.keyLabel || `VK 0x${vk.toString(16).toUpperCase().padStart(2, "0")}`,
    vk,
    code
  });
  settings.code = item.code;
}

function buildPresetOptions() {
  const keyboardGroup = document.createElement("optgroup");
  keyboardGroup.label = "Клавиатура";
  keyboardPresets.forEach(item => appendKeyboardOption(keyboardGroup, item));

  capturedKeyboardGroup = document.createElement("optgroup");
  capturedKeyboardGroup.label = "Захваченные и VK-коды";

  const mouseGroup = document.createElement("optgroup");
  mouseGroup.label = "Мышь";
  mousePresets.forEach(item => {
    const option = document.createElement("option");
    option.value = `mouse:${item.id}`;
    option.textContent = item.label;
    mouseGroup.appendChild(option);
  });

  dom.preset.append(keyboardGroup, capturedKeyboardGroup, mouseGroup);
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

  ensureCurrentKeyboardPreset();

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

  const item = keyboardPresetFromEvent(event);
  if (!item) {
    showStatus("Не удалось определить VK-код. Введите его вручную ниже.");
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

  const applyVirtualKey = () => {
    const item = virtualKeyPreset(Number(dom.virtualKey.value));
    if (!item) {
      showStatus("VK-код должен быть целым числом от 1 до 255");
      return;
    }

    setKeyboardPreset(item);
    dom.virtualKey.value = "";
    saveSettings();
  };

  dom.applyVirtualKey.addEventListener("click", applyVirtualKey);
  dom.virtualKey.addEventListener("keydown", event => {
    if (event.key === "Enter") {
      event.preventDefault();
      applyVirtualKey();
    }
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
  dom.virtualKey = byId("virtual-key");
  dom.applyVirtualKey = byId("apply-virtual-key");
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
