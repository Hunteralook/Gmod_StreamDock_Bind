"use strict";

const actions = {
  "com.local.netswitch.happ": {
    key: "happ",
    title: "Happ",
    text: "Запускает и закрывает Happ или открывает ссылку happ://.",
    defaults: { mode: "toggle", happPath: "", happLink: "", resetProxy: true, showStatus: true }
  },
  "com.local.netswitch.server": {
    key: "server",
    title: "Сервер Happ",
    text: "Показывает название и пинг одного сервера из подписки.",
    defaults: {
      subscriptionUrl: "",
      serverName: "",
      serverHost: "",
      serverPort: "",
      serverUdp: false,
      serverLabel: ""
    }
  },
  "com.local.netswitch.subscription": {
    key: "subscription",
    title: "Подписка Happ",
    text: "Скачивает подписку и показывает, сколько осталось дней и трафика.",
    defaults: { subscriptionUrl: "" }
  },
  "com.local.netswitch.zapret": {
    key: "zapret",
    title: "zapret",
    text: "Запускает и останавливает обход блокировок (winws.exe).",
    defaults: {
      mode: "toggle",
      zapretDir: "",
      zapretBat: "",
      zapretRun: "bat",
      serviceName: "zapret",
      elevation: "uac",
      showStatus: true
    }
  }
};

let websocket;
let context;
let actionUuid;
let definition;
let settings = {};
let info = {};
let statusTimer;

function byId(id) {
  return document.getElementById(id);
}

function fields() {
  return [...document.querySelectorAll("[data-setting]")];
}

function showStatus(message) {
  const status = byId("status");
  status.textContent = message;
  clearTimeout(statusTimer);
  statusTimer = setTimeout(() => {
    status.textContent = "";
  }, 2500);
}

function send(event, payload) {
  if (!websocket || websocket.readyState !== WebSocket.OPEN || !context) return false;
  websocket.send(JSON.stringify({ event, action: actionUuid, context, payload }));
  return true;
}

function requestInfo(request = "info") {
  byId("detected").textContent = "Проверка…";
  send("sendToPlugin", { request, settings });
}

function saveSettings(refresh) {
  if (send("setSettings", settings)) showStatus("Настройки сохранены");
  render();
  if (refresh) requestInfo();
}

function fillStrategies() {
  const select = byId("zapret-bat");
  const names = Array.isArray(info.strategies) ? [...info.strategies] : [];
  if (settings.zapretBat && !names.includes(settings.zapretBat)) names.unshift(settings.zapretBat);

  select.replaceChildren();
  if (names.length === 0) {
    const option = document.createElement("option");
    option.value = "";
    option.textContent = "Укажите папку zapret";
    select.appendChild(option);
    return;
  }

  names.forEach(name => {
    const option = document.createElement("option");
    option.value = name;
    option.textContent = name;
    select.appendChild(option);
  });
}

function fillServers() {
  const select = byId("server-select");
  const servers = Array.isArray(info.servers) ? info.servers : [];
  select.replaceChildren();

  const placeholder = document.createElement("option");
  placeholder.value = "";
  placeholder.textContent = servers.length > 0
    ? "Выберите сервер"
    : (settings.subscriptionUrl ? "Серверы не загружены" : "Укажите ссылку на подписку");
  select.appendChild(placeholder);

  let selected = "";
  servers.forEach((server, index) => {
    const option = document.createElement("option");
    option.value = String(index);
    option.textContent = server.name;
    select.appendChild(option);
    if (server.host === settings.serverHost && String(server.port) === String(settings.serverPort) &&
        (selected === "" || server.name === settings.serverName)) {
      selected = String(index);
    }
  });

  // Keep showing a server that is no longer in the subscription instead of silently dropping it.
  if (selected === "" && settings.serverHost) {
    const option = document.createElement("option");
    option.value = "saved";
    option.textContent = `${settings.serverName || settings.serverHost} (нет в подписке)`;
    select.appendChild(option);
    selected = "saved";
  }
  select.value = selected;
}

function line(label, value, good) {
  const row = document.createElement("div");
  const name = document.createElement("b");
  name.textContent = `${label}: `;
  const text = document.createElement("span");
  text.textContent = value;
  if (good !== undefined) text.className = good ? "ok" : "bad";
  row.append(name, text);
  return row;
}

function renderDetected() {
  const box = byId("detected");
  if (info.type !== "info") return;

  const rows = [line("На кнопке", info.state || "?")];
  if (info.error) rows.push(line("Ошибка", info.error, false));
  if (definition.key === "server" && settings.serverHost) {
    rows.push(line("Адрес", `${settings.serverHost}:${settings.serverPort}${settings.serverUdp ? " (UDP)" : ""}`));
  } else if (definition.key === "subscription" && settings.subscriptionUrl) {
    if (info.title) rows.push(line("Подписка", info.title));
    rows.push(line("Осталось", `${info.daysLeft || "?"}, ${info.trafficLeft || "?"}`));
    rows.push(line("Использовано", info.used || "?"));
    rows.push(line("Серверов", String((info.servers || []).length)));
  } else if (definition.key === "happ") {
    rows.push(line("Happ", info.happExe || "не найден", Boolean(info.happExe)));
  } else if (definition.key === "zapret") {
    if (settings.zapretRun === "service") {
      rows.push(line(`Служба «${settings.serviceName || "zapret"}»`, info.serviceFound ? "установлена" : "не найдена", Boolean(info.serviceFound)));
    } else {
      rows.push(line("winws.exe в папке", info.winwsFound ? "найден" : "не найден", Boolean(info.winwsFound)));
    }
    if (settings.elevation === "task") {
      rows.push(line("Задачи планировщика", info.tasksInstalled ? "созданы" : "не созданы", Boolean(info.tasksInstalled)));
    }
  }
  box.replaceChildren(...rows);
}

function render() {
  if (!definition) return;

  fields().forEach(field => {
    const value = settings[field.dataset.setting];
    if (field.type === "checkbox") field.checked = value !== false;
    else if (field.id !== "zapret-bat") field.value = value ?? "";
  });

  document.querySelectorAll("[data-section]").forEach(section => {
    section.hidden = !section.dataset.section.split(" ").includes(definition.key);
  });
  document.querySelectorAll("[data-only-section]").forEach(element => {
    element.hidden = element.dataset.onlySection !== definition.key;
  });
  const switchable = definition.key === "happ" || definition.key === "zapret";
  byId("mode-row").hidden = !switchable;
  byId("show-status-row").hidden = !switchable;
  document.querySelectorAll("[data-only]").forEach(option => {
    option.hidden = option.dataset.only !== definition.key;
    option.disabled = option.hidden;
  });
  document.querySelectorAll("[data-run]").forEach(element => {
    element.hidden = element.dataset.run !== (settings.zapretRun || "bat");
  });
  document.querySelectorAll("[data-elevation]").forEach(element => {
    element.hidden = element.dataset.elevation !== settings.elevation;
  });

  fillStrategies();
  byId("zapret-bat").value = settings.zapretBat || "";
  fillServers();
  renderDetected();
}

function wireUi() {
  fields().forEach(field => {
    const name = field.dataset.setting;
    field.addEventListener("change", () => {
      settings[name] = field.type === "checkbox" ? field.checked : field.value.trim();
      saveSettings(true);
    });
  });

  byId("refresh").addEventListener("click", () => requestInfo());
  byId("reload").addEventListener("click", () => requestInfo("reload"));
  byId("server-select").addEventListener("change", event => {
    const server = (info.servers || [])[Number(event.target.value)];
    if (!server || event.target.value === "") return;
    settings.serverName = server.name;
    settings.serverHost = server.host;
    settings.serverPort = String(server.port);
    settings.serverUdp = Boolean(server.udp);
    saveSettings(true);
  });
  byId("install-tasks").addEventListener("click", () => {
    showStatus("Подтвердите запрос UAC…");
    requestInfo("installTasks");
  });
  byId("remove-tasks").addEventListener("click", () => {
    showStatus("Подтвердите запрос UAC…");
    requestInfo("removeTasks");
  });
}

document.addEventListener("DOMContentLoaded", wireUi);

function connectElgatoStreamDeckSocket(inPort, inPropertyInspectorUuid, inRegisterEvent, inInfo, inActionInfo) {
  const actionInfo = JSON.parse(inActionInfo);
  context = actionInfo.context;
  actionUuid = actionInfo.action;
  definition = actions[actionUuid] || actions["com.local.netswitch.happ"];
  settings = { ...definition.defaults, ...(actionInfo.payload?.settings || {}) };

  byId("hero-icon").src = `../images/${definition.key}-on.svg`;
  byId("hero-title").textContent = definition.title;
  byId("hero-text").textContent = definition.text;
  render();

  websocket = new WebSocket(`ws://127.0.0.1:${inPort}`);
  websocket.onopen = () => {
    websocket.send(JSON.stringify({ event: inRegisterEvent, uuid: inPropertyInspectorUuid }));
    requestInfo();
  };
  websocket.onmessage = event => {
    const message = JSON.parse(event.data);
    if (message.event === "didReceiveSettings" && message.payload?.settings) {
      settings = { ...definition.defaults, ...message.payload.settings };
      render();
    } else if (message.event === "sendToPropertyInspector" && message.payload?.type === "info") {
      info = message.payload;
      if (info.notice) showStatus(info.notice);

      if (!settings.subscriptionUrl && info.knownUrl && "subscriptionUrl" in definition.defaults) {
        settings.subscriptionUrl = info.knownUrl;
        saveSettings(true);
        return;
      }

      const strategies = Array.isArray(info.strategies) ? info.strategies : [];
      if (definition.key === "zapret" && !settings.zapretBat && strategies.length > 0) {
        settings.zapretBat = strategies.includes("general.bat") ? "general.bat" : strategies[0];
        saveSettings(false);
        return;
      }
      render();
    }
  };
}
