const tokenKey = "pocketmc.remote.deviceToken";
const instanceKey = "pocketmc.remote.instanceId";
const viewKey = "pocketmc.remote.currentView"; // "instances" or "devices"

const els = {
  connectionLabel: document.querySelector("#connectionLabel"),
  refreshButton: document.querySelector("#refreshButton"),
  notice: document.querySelector("#notice"),
  
  pairView: document.querySelector("#pairView"),
  pairTitle: document.querySelector("#pairTitle"),
  pairMessage: document.querySelector("#pairMessage"),
  pairButton: document.querySelector("#pairBrowserButton"),
  copyPairLinkButton: document.querySelector("#copyPairLinkButton"),
  
  appView: document.querySelector("#appView"),
  devicesView: document.querySelector("#devicesView"),
  emptyView: document.querySelector("#emptyView"),
  emptyRefreshButton: document.querySelector("#emptyRefreshButton"),
  errorView: document.querySelector("#errorView"),
  errorMessage: document.querySelector("#errorMessage"),
  retryButton: document.querySelector("#retryButton"),
  clearTokenButton: document.querySelector("#clearTokenButton"),
  
  instanceListSidebar: document.querySelector("#instanceListSidebar"),
  navDevices: document.querySelector("#navDevices"),
  devicesListContainer: document.querySelector("#devicesListContainer"),
  
  serverName: document.querySelector("#serverName"),
  serverType: document.querySelector("#serverType"),
  serverIconImage: document.querySelector("#serverIconImage"),
  statusPill: document.querySelector("#statusPill"),
  playerCount: document.querySelector("#playerCount"),
  ramUsage: document.querySelector("#ramUsage"),
  cpuUsage: document.querySelector("#cpuUsage"),
  uptime: document.querySelector("#uptime"),
  
  startButton: document.querySelector("#startButton"),
  stopButton: document.querySelector("#stopButton"),
  restartButton: document.querySelector("#restartButton"),
  
  consoleState: document.querySelector("#consoleState"),
  consoleOutput: document.getElementById("consoleOutput"),
  filterInfo: document.getElementById("filterInfo"),
  filterWarn: document.getElementById("filterWarn"),
  filterError: document.getElementById("filterError"),
  commandForm: document.getElementById("commandForm"),
  commandInput: document.querySelector("#commandInput"),
  commandDisabled: document.querySelector("#commandDisabled"),
  
  playersState: document.querySelector("#playersState"),
  playerList: document.querySelector("#playerList"),
  
  tabs: document.querySelectorAll(".tab-button"),
  tabContents: document.querySelectorAll(".tab-content"),
  serverIpsContainer: document.querySelector("#serverIpsContainer"),
  serverIpsList: document.querySelector("#serverIpsList"),
  
  playerActionModal: document.querySelector("#playerActionModal"),
  playerActionModalTitle: document.querySelector("#playerActionModalTitle"),
  playerActionModalName: document.querySelector("#playerActionModalName"),
  playerActionButtons: document.querySelector("#playerActionButtons"),
  reasonModalForm: document.querySelector("#reasonModalForm"),
  reasonModalInput: document.querySelector("#reasonModalInput"),
  reasonModalCancel: document.querySelector("#reasonModalCancel"),
  playerActionModalClose: document.querySelector("#playerActionModalClose"),

  btnMakeOp: document.querySelector("#btnMakeOp"),
  btnDeop: document.querySelector("#btnDeop"),
  btnKick: document.querySelector("#btnKick"),
  btnBan: document.querySelector("#btnBan"),
  btnUnban: document.querySelector("#btnUnban"),
  
  offlinePlayerInput: document.querySelector("#offlinePlayerInput"),
  btnOfflineManage: document.querySelector("#btnOfflineManage"),
  offlinePlayerForm: document.querySelector("#offlinePlayerForm")
};

let deviceToken = localStorage.getItem(tokenKey);
let selectedInstanceId = localStorage.getItem(instanceKey);
let currentAppView = localStorage.getItem(viewKey) || "instances"; // instances, devices
let socket = null;
let statusTimer = null;
let historyLoadedForInstance = null;
let lastInstanceStatus = null;
let remoteStatusGlobal = null;

// Modal state
let modalActionTarget = null; // { name: string, action: string, requireReason: bool }

function pairingTokenFromUrl() {
  return new URLSearchParams(window.location.search).get("token");
}

function setVisible(view) {
  for (const item of [els.pairView, els.appView, els.emptyView, els.errorView, els.devicesView]) {
    if (item) item.hidden = item !== view;
  }
}

function showNotice(message) {
  els.notice.textContent = message;
  els.notice.hidden = false;
  clearTimeout(showNotice.timer);
  showNotice.timer = setTimeout(() => {
    els.notice.hidden = true;
  }, 3600);
}

async function api(path, options = {}) {
  const headers = new Headers(options.headers || {});
  if (deviceToken) {
    headers.set("Authorization", `Bearer ${deviceToken}`);
  }
  if (options.body && !headers.has("Content-Type")) {
    headers.set("Content-Type", "application/json");
  }

  const response = await fetch(path, { ...options, headers });
  if (response.status === 401 || response.status === 403) {
    localStorage.removeItem(tokenKey);
    deviceToken = null;
    throw new Error("Session expired, revoked, or permission denied. Please pair again.");
  }

  if (!response.ok) {
    const text = await response.text();
    let msg = `Request failed (${response.status})`;
    try {
        const json = JSON.parse(text);
        if (json.error) msg = json.error;
    } catch { msg = text || msg; }
    throw new Error(msg);
  }

  const contentType = response.headers.get("Content-Type") || "";
  return contentType.includes("application/json") ? response.json() : response.text();
}

async function start() {
  clearInterval(statusTimer);
  closeSocket();

  if (deviceToken) {
    if (pairingTokenFromUrl()) {
      history.replaceState({}, "", "/remote/index.html");
    }
    await openDashboard();
    return;
  }

  showPairPrompt();
}

function showPairPrompt() {
  closeSocket();
  clearInterval(statusTimer);
  const token = pairingTokenFromUrl();
  els.connectionLabel.textContent = token ? "Ready to pair" : "Not paired";
  els.pairTitle.textContent = token ? "Pair this browser" : "Pairing link needed";
  els.pairMessage.textContent = token
    ? "Open this link before it expires. It can pair one browser only."
    : "Create a Pair Device link in PocketMC Desktop, then open it here.";
  els.pairButton.disabled = !token;
  els.copyPairLinkButton.disabled = !token;
  setVisible(els.pairView);
}

async function pairDevice() {
  const pairingToken = pairingTokenFromUrl();
  if (!pairingToken) {
    showPairPrompt();
    return;
  }

  els.pairButton.disabled = true;
  els.pairButton.querySelector("span:last-child").textContent = "Pairing...";
  try {
    const response = await fetch("/api/pairing/exchange", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        pairingToken,
        deviceName: navigator.userAgentData?.platform || navigator.platform || "Browser"
      })
    });

    if (!response.ok) {
      throw new Error("Pairing link expired. Create a new one in PocketMC Desktop.");
    }

    const payload = await response.json();
    localStorage.setItem(tokenKey, payload.deviceToken);
    deviceToken = payload.deviceToken;
    history.replaceState({}, "", "/remote/index.html");
    showNotice("This browser is paired.");
    await openDashboard();
  } catch (error) {
    els.pairMessage.textContent = error.message;
  } finally {
    els.pairButton.disabled = false;
    els.pairButton.querySelector("span:last-child").textContent = "Pair Browser";
  }
}

function showError(msg) {
  els.errorMessage.textContent = msg;
  setVisible(els.errorView);
  els.connectionLabel.textContent = "Disconnected";
  els.connectionLabel.className = "connection-pill offline";
}

async function openDashboard() {
  try {
    await refreshEverything({ reconnectConsole: true });
    statusTimer = setInterval(() => refreshEverything({ reconnectConsole: false }), 3000);
  } catch (error) {
    if (!deviceToken) {
      showPairPrompt();
    } else {
      showError(error.message);
    }
  }
}

async function refreshEverything({ reconnectConsole = false } = {}) {
  const instances = await api("/api/instances");
  remoteStatusGlobal = await api("/api/status");
  
  els.connectionLabel.textContent = remoteStatusGlobal.publicUrl || remoteStatusGlobal.localUrls?.[0] || "Connected";
  els.connectionLabel.className = "connection-pill online";

  renderSidebar(instances);

  if (currentAppView === "devices") {
      setVisible(els.devicesView);
      await renderDevices();
      return;
  }

  if (instances.length === 0) {
    historyLoadedForInstance = null;
    lastInstanceStatus = null;
    closeSocket();
    setVisible(els.emptyView);
    return;
  }

  if (!selectedInstanceId || !instances.some((i) => i.id === selectedInstanceId)) {
    selectedInstanceId = instances[0].id;
    localStorage.setItem(instanceKey, selectedInstanceId);
    renderSidebar(instances); // re-render to update active
  }

  setVisible(els.appView);

  const instanceStatus = await api(`/api/instances/${selectedInstanceId}/status`);
  lastInstanceStatus = instanceStatus;
  
  renderStatus(remoteStatusGlobal, instanceStatus);

  if (reconnectConsole) {
    historyLoadedForInstance = null;
    closeSocket();
  }
  await ensureConsoleConnection(instanceStatus);
}

function getStatusColor(status) {
  switch (status.toLowerCase()) {
    case "online": return "var(--success-strong)";
    case "offline": return "var(--text-muted)";
    default: return "var(--warning-strong)";
  }
}

function getServerIcon(serverType) {
  if (!serverType) return "/remote/icon.png";
  const type = serverType.toLowerCase();
  if (type.includes("fabric")) return "/remote/icons/fabric.png";
  if (type.includes("forge")) return "/remote/icons/forge.png";
  if (type.includes("paper") || type.includes("purpur")) return "/remote/icons/papermc.png";
  if (type.includes("bedrock") || type.includes("bds")) return "/remote/icons/bds.png";
  if (type.includes("pocketmine")) return "/remote/icons/pocketmine-mp.png";
  return "/remote/icons/vanilla.png";
}

function renderSidebar(instances) {
  els.instanceListSidebar.innerHTML = "";
  for (const instance of instances) {
    const li = document.createElement("li");
    const btn = document.createElement("button");
    btn.className = "sidebar-item";
    if (currentAppView === "instances" && instance.id === selectedInstanceId) {
        btn.classList.add("active");
    }
    
    const iconPath = getServerIcon(instance.serverType);
    const icon = `<img src="${iconPath}" alt="" class="sidebar-item-icon" />`;
    btn.innerHTML = `${icon}<span>${escapeHtml(instance.name)}</span>`;
    
    btn.addEventListener("click", () => {
        currentAppView = "instances";
        localStorage.setItem(viewKey, "instances");
        selectedInstanceId = instance.id;
        localStorage.setItem(instanceKey, selectedInstanceId);
        openDashboard();
    });
    li.appendChild(btn);
    els.instanceListSidebar.appendChild(li);
  }
  
  if (currentAppView === "devices") {
      els.navDevices.classList.add("active");
  } else {
      els.navDevices.classList.remove("active");
  }
}

async function renderDevices() {
    try {
        const result = await api("/api/devices");
        els.devicesListContainer.innerHTML = "";
        
        for (const device of result.devices) {
            const div = document.createElement("div");
            div.className = "device-item";
            
            const info = document.createElement("div");
            info.className = "device-info";
            info.innerHTML = `<h3>${escapeHtml(device.name)} ${device.isCurrent ? '<span class="current-badge">Current</span>' : ''}</h3>
                              <p class="muted">Last seen: ${new Date(device.lastSeenAtUtc).toLocaleString()}</p>`;
                              
            const actions = document.createElement("div");
            if (!device.isCurrent) {
                const revokeBtn = document.createElement("button");
                revokeBtn.className = "danger-button";
                revokeBtn.innerHTML = `<span>Revoke</span>`;
                revokeBtn.addEventListener("click", async () => {
                    try {
                        revokeBtn.disabled = true;
                        await api("/api/devices/revoke", { method: "POST", body: JSON.stringify({ deviceId: device.id }) });
                        showNotice(`Revoked ${device.name}`);
                        renderDevices();
                    } catch(err) {
                        showNotice(err.message);
                        revokeBtn.disabled = false;
                    }
                });
                actions.appendChild(revokeBtn);
            }
            
            div.appendChild(info);
            div.appendChild(actions);
            els.devicesListContainer.appendChild(div);
        }
    } catch(err) {
        els.devicesListContainer.innerHTML = `<p class="muted">Failed to load devices: ${err.message}</p>`;
    }
}

function renderStatus(remoteStatus, instanceStatus) {
  els.serverName.textContent = instanceStatus.name;
  els.serverType.textContent = instanceStatus.serverType;
  
  if (els.serverIconImage) {
      els.serverIconImage.src = getServerIcon(instanceStatus.serverType);
  }

  setStatusPill(instanceStatus);
  els.playerCount.textContent = `${instanceStatus.playerCount} / ${instanceStatus.maxPlayers}`;
  els.ramUsage.textContent = `${(instanceStatus.ramUsageMb / 1024).toFixed(1)} GB`;
  els.cpuUsage.textContent = `${instanceStatus.cpuUsage.toFixed(0)}%`;
  els.uptime.textContent = formatUptime(instanceStatus.uptimeSeconds);
  
  els.startButton.disabled = instanceStatus.isRunning;
  els.stopButton.disabled = !instanceStatus.isRunning;
  els.restartButton.disabled = !instanceStatus.isRunning;
  
  const canSendCommands = remoteStatus.allowRemoteConsoleCommands && instanceStatus.isRunning;
  els.commandForm.hidden = !canSendCommands;
  els.commandDisabled.hidden = remoteStatusGlobal.allowRemoteConsoleCommands && instanceStatus.isRunning;
  
  renderPlayers(instanceStatus.onlinePlayers || [], remoteStatus.allowRemotePlayerActions);

  if (instanceStatus.serverIps && instanceStatus.serverIps.length > 0) {
    els.serverIpsContainer.hidden = false;
    els.serverIpsList.innerHTML = "";
    
    // Filter redundant LAN IPs: Prefer IPV4 over IPV6, keep only 1 for each port type if possible
    let filteredIps = instanceStatus.serverIps.filter(ip => ip.label.toLowerCase().includes("tunnel"));
    let lanIps = instanceStatus.serverIps.filter(ip => !ip.label.toLowerCase().includes("tunnel"));
    
    // Naive filter for LAN: group by port
    let lanGroups = {};
    for (const lip of lanIps) {
        const parts = lip.address.split(":");
        const port = parts[parts.length-1];
        if (!lanGroups[port]) lanGroups[port] = [];
        lanGroups[port].push(lip);
    }
    for (const port in lanGroups) {
        // Pick IPv4 first
        let best = lanGroups[port].find(x => x.address.includes(".")) || lanGroups[port][0];
        filteredIps.push(best);
    }

    for (const ip of filteredIps) {
      const badge = document.createElement("div");
      badge.className = "ip-item";
      badge.innerHTML = `
        <div class="ip-info">
          <span class="ip-label">${escapeHtml(ip.label)}</span>
          <span class="ip-value">${escapeHtml(ip.address)}</span>
        </div>
      `;
      
      const copyBtn = document.createElement("button");
      copyBtn.className = "icon-button copy-btn";
      copyBtn.type = "button";
      copyBtn.title = "Copy to clipboard";
      copyBtn.innerHTML = `<svg viewBox="0 0 24 24"><path d="M16 4h2a2 2 0 0 1 2 2v14a2 2 0 0 1-2 2H6a2 2 0 0 1-2-2V6a2 2 0 0 1 2-2h2M15 2H9a1 1 0 0 0-1 1v2a1 1 0 0 0 1 1h6a1 1 0 0 0 1-1V3a1 1 0 0 0-1-1z"/></svg>`;
      copyBtn.addEventListener("click", () => {
        navigator.clipboard.writeText(ip.address);
        showNotice("IP copied to clipboard!");
      });
      badge.appendChild(copyBtn);
      els.serverIpsList.append(badge);
    }
  } else {
    els.serverIpsContainer.hidden = true;
  }
}

function setStatusPill(instanceStatus) {
  const state = (instanceStatus.state || "").toLowerCase();
  const isBusy = state.includes("start") || state.includes("stop") || state.includes("restart");
  els.statusPill.textContent = instanceStatus.state || (instanceStatus.isRunning ? "Online" : "Offline");
  els.statusPill.className = "status-pill";
  els.statusPill.classList.add(instanceStatus.isRunning ? "online" : (isBusy ? "busy" : "offline"));
}

function formatUptime(seconds) {
  if (seconds < 0) return "0m";
  const h = Math.floor(seconds / 3600);
  const m = Math.floor((seconds % 3600) / 60);
  if (h > 0) return `${h}h ${m}m`;
  return `${m}m`;
}

// ---------------------------------------------------------
// Console
// ---------------------------------------------------------
async function ensureConsoleConnection(instanceStatus) {
  if (!instanceStatus.isRunning) {
    closeSocket();
    els.consoleState.textContent = "Offline";
    els.consoleState.className = "state-label offline";
    if (!els.consoleOutput.hasChildNodes() || els.consoleOutput.textContent === "Server is offline.") {
      els.consoleOutput.innerHTML = "Server is offline.";
    }
    return;
  }

  if (historyLoadedForInstance !== selectedInstanceId) {
    await loadConsoleHistory();
  }

  if (!socket || socket.readyState === WebSocket.CLOSED || socket.readyState === WebSocket.CLOSING) {
    openConsoleSocket();
  }
}

async function loadConsoleHistory() {
  if (!selectedInstanceId) return;
  try {
    const lines = await api(`/api/instances/${selectedInstanceId}/console/history`);
    els.consoleOutput.innerHTML = "";
    if (lines.length > 0) {
      lines.forEach(appendConsole);
    } else {
      els.consoleOutput.textContent = "Waiting for console output...";
    }
    historyLoadedForInstance = selectedInstanceId;
    scrollConsole();
  } catch (err) {
    els.consoleOutput.innerHTML = "Console history is not available yet.";
  }
}

async function openConsoleSocket() {
  if (!selectedInstanceId || !deviceToken) return;
  closeSocket();
  
  els.consoleState.textContent = "Connecting";
  els.consoleState.className = "state-label busy";

  let ticket = null;
  try {
    const response = await fetch(`/api/instances/${selectedInstanceId}/console/ticket`, {
        method: "POST",
        headers: { "Authorization": `Bearer ${deviceToken}` }
    });
    if (!response.ok) throw new Error("Could not get WS ticket");
    const data = await response.json();
    ticket = data.ticket;
  } catch (err) {
    els.consoleState.textContent = "Offline";
    return;
  }

  const protocol = window.location.protocol === "https:" ? "wss:" : "ws:";
  socket = new WebSocket(`${protocol}//${window.location.host}/ws/instances/${selectedInstanceId}/console?ticket=${encodeURIComponent(ticket)}`);
  
  els.consoleState.textContent = "Connecting";
  els.consoleState.className = "state-label busy";

  socket.addEventListener("open", () => {
    els.consoleState.textContent = "Live";
    els.consoleState.className = "state-label live";
  });

  socket.addEventListener("message", (event) => {
    const payload = JSON.parse(event.data);
    if (payload.type === "history" && historyLoadedForInstance === selectedInstanceId) return;
    appendConsole(payload.line);
  });

  socket.addEventListener("close", () => {
    socket = null;
    if (lastInstanceStatus?.isRunning) {
      els.consoleState.textContent = "Reconnecting";
      setTimeout(() => ensureConsoleConnection(lastInstanceStatus), 1200);
    } else {
      els.consoleState.textContent = "Offline";
    }
  });
}

function closeSocket() {
  if (socket) {
    const s = socket;
    socket = null;
    s.close();
  }
}

function appendConsole(line) {
  if (["Waiting for console output...", "Server is offline.", "Console history is not available yet."].includes(els.consoleOutput.textContent)) {
    els.consoleOutput.innerHTML = "";
  }

  const p = document.createElement("p");
  
  let level = "info";
  if (line.includes("WARN")) level = "warn";
  if (line.includes("ERROR") || line.includes("Exception") || line.includes("Failed")) level = "error";
  p.classList.add(`log-${level}`);
  
  let formatted = escapeHtml(line);
  // Colorize log pieces
  formatted = formatted.replace(/^(\[[0-9:]+\])\s*/, '<span class="log-time">$1</span> ');
  formatted = formatted.replace(/(\[.*?\/.*?\])\s*:\s*/, '<span class="log-source">$1:</span> ');
  formatted = formatted.replace(/(\[INFO\]|\[WARN\]|\[ERROR\])\s*/, '<span class="log-level">$1</span> ');

  p.innerHTML = formatted;
  els.consoleOutput.append(p);

  while (els.consoleOutput.childNodes.length > 500) {
    els.consoleOutput.firstChild.remove();
  }
  
  applyConsoleFiltersToLine(p);
  scrollConsole();
}

function applyConsoleFilters() {
  const showInfo = els.filterInfo.checked;
  const showWarn = els.filterWarn.checked;
  const showError = els.filterError.checked;
  
  for (const lineEl of els.consoleOutput.children) {
    applyConsoleFiltersToLine(lineEl);
  }
  scrollConsole();
}

function applyConsoleFiltersToLine(lineEl) {
  const showInfo = els.filterInfo.checked;
  const showWarn = els.filterWarn.checked;
  const showError = els.filterError.checked;

  let show = true;
  if (lineEl.classList.contains("log-info") && !showInfo) show = false;
  if (lineEl.classList.contains("log-warn") && !showWarn) show = false;
  if (lineEl.classList.contains("log-error") && !showError) show = false;
  lineEl.style.display = show ? "block" : "none";
}

function scrollConsole() {
  els.consoleOutput.scrollTop = els.consoleOutput.scrollHeight;
}

// ---------------------------------------------------------
// Players
// ---------------------------------------------------------
function renderPlayers(players, allowActions) {
  els.playersState.textContent = `${players.length} online`;
  els.playersState.className = "state-label " + (players.length > 0 ? "online" : "");
  els.playerList.innerHTML = "";

  if (players.length === 0) {
    els.playerList.innerHTML = '<p class="muted">No players online.</p>';
    return;
  }

  for (const player of players) {
    const item = document.createElement("div");
    item.className = "player-item";
    
    item.innerHTML = `
      <div class="player-name">
        <span>${escapeHtml(player.name)}</span>
      </div>
    `;

    if (allowActions) {
        const manageBtn = document.createElement("button");
        manageBtn.className = "secondary-button";
        manageBtn.innerHTML = `<span>Manage</span>`;
        manageBtn.addEventListener("click", () => openPlayerModal(player.name));
        item.appendChild(manageBtn);
    }
    
    els.playerList.append(item);
  }
}

function openPlayerModal(playerName) {
    els.playerActionModalName.textContent = playerName;

    let isOp = false;
    let isBanned = false;
    let isOnline = false;

    if (lastInstanceStatus) {
        if (lastInstanceStatus.oppedPlayers) {
            isOp = lastInstanceStatus.oppedPlayers.some(p => p.toLowerCase() === playerName.toLowerCase());
        }
        if (lastInstanceStatus.bannedPlayers) {
            isBanned = lastInstanceStatus.bannedPlayers.some(p => p.toLowerCase() === playerName.toLowerCase());
        }
        if (lastInstanceStatus.onlinePlayers) {
            isOnline = lastInstanceStatus.onlinePlayers.some(p => p.name.toLowerCase() === playerName.toLowerCase());
        }
    }

    els.btnMakeOp.hidden = isOp;
    els.btnDeop.hidden = !isOp;
    els.btnBan.hidden = isBanned;
    els.btnUnban.hidden = !isBanned;
    els.btnKick.hidden = !isOnline || isBanned;

    els.playerActionButtons.hidden = false;
    els.reasonModalForm.hidden = true;
    els.playerActionModal.hidden = false;
    modalActionTarget = { name: playerName, action: null };
}

async function performPlayerAction(action, reason = null) {
    if (!modalActionTarget || !modalActionTarget.name) return;
    try {
        const body = reason ? JSON.stringify({ reason }) : "{}";
        await api(`/api/instances/${selectedInstanceId}/players/${encodeURIComponent(modalActionTarget.name)}/${action}`, {
            method: "POST",
            body
        });
        showNotice(`Action '${action}' successful on ${modalActionTarget.name}`);
        els.playerActionModal.hidden = true;
        refreshEverything();
    } catch(err) {
        showNotice(err.message);
    }
}

// ---------------------------------------------------------
// Events
// ---------------------------------------------------------
els.tabs.forEach(tab => {
  tab.addEventListener("click", () => {
    els.tabs.forEach(t => t.classList.remove("active"));
    els.tabContents.forEach(c => { c.classList.remove("active"); c.hidden = true; });
    tab.classList.add("active");
    const content = document.getElementById(`tab-${tab.dataset.tab}`);
    content.classList.add("active");
    content.hidden = false;
    if (tab.dataset.tab === "console") scrollConsole();
  });
});

els.navDevices.addEventListener("click", () => {
    currentAppView = "devices";
    localStorage.setItem(viewKey, "devices");
    openDashboard();
});

els.pairButton.addEventListener("click", pairDevice);
els.copyPairLinkButton.addEventListener("click", () => {
  navigator.clipboard.writeText(window.location.href);
  showNotice("Pairing link copied");
});

els.refreshButton.addEventListener("click", () => refreshEverything());
els.emptyRefreshButton.addEventListener("click", () => refreshEverything());
els.retryButton.addEventListener("click", () => refreshEverything());

els.clearTokenButton.addEventListener("click", () => {
  localStorage.removeItem(tokenKey);
  deviceToken = null;
  history.replaceState({}, "", "/remote/index.html");
  showPairPrompt();
});

const bindInstanceAction = (btn, action) => {
  btn.addEventListener("click", async () => {
    if (!selectedInstanceId) return;
    try {
      btn.disabled = true;
      await api(`/api/instances/${selectedInstanceId}/${action}`, { method: "POST" });
      refreshEverything();
    } catch (error) {
      showNotice(error.message);
      btn.disabled = false;
    }
  });
};
bindInstanceAction(els.startButton, "start");
bindInstanceAction(els.stopButton, "stop");
bindInstanceAction(els.restartButton, "restart");

[els.filterInfo, els.filterWarn, els.filterError].forEach(cb => cb.addEventListener("change", applyConsoleFilters));

els.commandForm.addEventListener("submit", async (e) => {
  e.preventDefault();
  const command = els.commandInput.value.trim();
  if (!command || !selectedInstanceId) return;
  els.commandInput.disabled = true;
  try {
    await api(`/api/instances/${selectedInstanceId}/console/command`, {
      method: "POST",
      body: JSON.stringify({ command })
    });
    els.commandInput.value = "";
  } catch (error) {
    showNotice(error.message);
  } finally {
    els.commandInput.disabled = false;
    els.commandInput.focus();
  }
});

function escapeHtml(unsafe) {
  return (unsafe||"").replace(/&/g, "&amp;")
    .replace(/</g, "&lt;").replace(/>/g, "&gt;")
    .replace(/"/g, "&quot;").replace(/'/g, "&#039;");
}

// Modal events
els.playerActionButtons.addEventListener("click", (e) => {
    if (e.target.tagName === "BUTTON") {
        const action = e.target.dataset.action;
        modalActionTarget.action = action;
        
        if (action === "kick" || action === "ban") {
            els.playerActionButtons.hidden = true;
            els.reasonModalForm.hidden = false;
            els.reasonModalInput.value = "";
            els.reasonModalInput.focus();
        } else {
            performPlayerAction(action, null);
        }
    }
});
els.playerActionModalClose.addEventListener("click", () => {
    els.playerActionModal.hidden = true;
});
els.reasonModalCancel.addEventListener("click", () => {
    els.reasonModalForm.hidden = true;
    els.playerActionButtons.hidden = false;
});
els.reasonModalForm.addEventListener("submit", (e) => {
    e.preventDefault();
    performPlayerAction(modalActionTarget.action, els.reasonModalInput.value.trim() || null);
});
els.offlinePlayerForm.addEventListener("submit", (e) => {
    e.preventDefault();
    const name = els.offlinePlayerInput.value.trim();
    if (!name) return;
    openPlayerModal(name);
    els.offlinePlayerInput.value = "";
});

// Init
start();

// Mobile menu toggle
const mobileMenuToggle = document.getElementById('mobileMenuToggle');
const appSidebar = document.getElementById('appSidebar');
const sidebarOverlay = document.getElementById('sidebarOverlay');
const hamburgerIcon = document.getElementById('hamburgerIcon');
const closeIcon = document.getElementById('closeIcon');

function toggleMobileMenu() {
    const isOpen = appSidebar.classList.toggle('open');
    sidebarOverlay.classList.toggle('open');
    if (isOpen) {
        hamburgerIcon.hidden = true;
        closeIcon.hidden = false;
    } else {
        hamburgerIcon.hidden = false;
        closeIcon.hidden = true;
    }
}

if (mobileMenuToggle) {
    mobileMenuToggle.addEventListener('click', toggleMobileMenu);
}
if (sidebarOverlay) {
    sidebarOverlay.addEventListener('click', toggleMobileMenu);
}

// Close sidebar when clicking a navigation item on mobile
document.addEventListener('click', (e) => {
    if (window.innerWidth <= 768 && appSidebar.classList.contains('open')) {
        if (e.target.closest('.sidebar-item') || e.target.closest('.instance-item')) {
            toggleMobileMenu();
        }
    }
});
