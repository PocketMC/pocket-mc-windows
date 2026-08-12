const instanceKey = "pocketmc.remote.instanceId";
const viewKey = "pocketmc.remote.currentView"; // "instances"

const els = {
  connectionLabel: document.querySelector("#connectionLabel"),
  refreshButton: document.querySelector("#refreshButton"),
  notice: document.querySelector("#notice"),

  appView: document.querySelector("#appView"),
  emptyView: document.querySelector("#emptyView"),
  emptyRefreshButton: document.querySelector("#emptyRefreshButton"),
  errorView: document.querySelector("#errorView"),
  errorMessage: document.querySelector("#errorMessage"),
  retryButton: document.querySelector("#retryButton"),

  loginView: document.querySelector("#loginView"),
  loginForm: document.querySelector("#loginForm"),
  loginPasswordInput: document.querySelector("#loginPasswordInput"),
  loginSubmitButton: document.querySelector("#loginSubmitButton"),
  loginError: document.querySelector("#loginError"),

  instanceListSidebar: document.querySelector("#instanceListSidebar"),

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
  quickCommandsBar: document.querySelector("#quickCommandsBar"),

  playersState: document.querySelector("#playersState"),
  playerList: document.querySelector("#playerList"),
  offlinePlayerManage: document.querySelector("#offlinePlayerManage"),
  playersDisabled: document.querySelector("#playersDisabled"),

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
  playerActionCloseRow: document.querySelector("#playerActionCloseRow"),

  btnMakeOp: document.querySelector("#btnMakeOp"),
  btnDeop: document.querySelector("#btnDeop"),
  btnKick: document.querySelector("#btnKick"),
  btnBan: document.querySelector("#btnBan"),
  btnUnban: document.querySelector("#btnUnban"),

  offlinePlayerInput: document.querySelector("#offlinePlayerInput"),
  btnOfflineManage: document.querySelector("#btnOfflineManage"),
  offlinePlayerForm: document.querySelector("#offlinePlayerForm"),
  welcomeScreen: document.querySelector("#welcomeScreen"),
  getStartedButton: document.querySelector("#getStartedButton"),
  instanceSelectionView: document.querySelector("#instanceSelectionView"),
  instancesGrid: document.querySelector("#instancesGrid"),
  instanceSearchInput: document.querySelector("#instanceSearchInput"),
  backToSelectorButton: document.querySelector("#backToSelectorButton"),
  serverVersionBadge: document.querySelector("#serverVersionBadge"),
  serverIpAddress: document.querySelector("#serverIpAddress"),
  cpuProgressBar: document.querySelector("#cpuProgressBar"),
  ramProgressBar: document.querySelector("#ramProgressBar"),
  playersAvatarList: document.querySelector("#playersAvatarList"),
  themeToggle: document.querySelector("#themeToggle"),
  pinnedBadge: document.querySelector("#pinnedBadge")
};


// Global UI initialization based on current page
const isLoginPage = window.location.pathname.includes('login.html');
const isServerPage = window.location.pathname.includes('server.html');
const urlParams = new URLSearchParams(window.location.search);
let selectedInstanceId = isServerPage ? urlParams.get('id') : localStorage.getItem(instanceKey);

if (isServerPage && !selectedInstanceId) {
  window.location.href = '/remote/index.html';
}

let currentView = selectedInstanceId ? "details" : "selection";
let searchQuery = "";
let cachedInstances = [];
let socket = null;
let statusTimer = null;
let historyLoadedForInstance = null;
let lastInstanceStatus = null;
let remoteStatusGlobal = null;

// Modal state
let modalActionTarget = null; // { name: string, action: string, requireReason: bool }

function setVisible(view) {
  for (const item of [els.appView, els.emptyView, els.errorView, els.instanceSelectionView, els.loginView]) {
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
  if (options.body && !headers.has("Content-Type")) {
    headers.set("Content-Type", "application/json");
  }

  const response = await fetch(path, { ...options, headers });

  if (!response.ok) {
    if (response.status === 401 && path !== "/api/login") {
      closeSocket();
      clearInterval(statusTimer);
      setVisible(els.loginView);
      throw new Error("Unauthorized");
    }

    const text = await response.text();
    let msg = `Request failed (${response.status})`;
    try {
      const json = JSON.parse(text);
      if (json.error) msg = json.error;
    } catch {
      if (text && (text.trim().startsWith("<") || text.includes("Cloudflare"))) {
        msg = `Remote tunnel is offline (HTTP ${response.status})`;
      } else {
        msg = text || msg;
      }
    }
    throw new Error(msg);
  }

  const contentType = response.headers.get("Content-Type") || "";
  return contentType.includes("application/json") ? response.json() : response.text();
}


if (els.getStartedButton && els.welcomeScreen) {
  if (els.getStartedButton) els.getStartedButton.addEventListener("click", () => {
    localStorage.setItem("pocketmc.remote.welcomeSeen", "true");
    els.welcomeScreen.classList.add("welcome-fade-out");
    setTimeout(() => {
      els.welcomeScreen.hidden = true;
    }, 300);
  });
}

// Init
async function start() {
  const welcomeSeen = localStorage.getItem("pocketmc.remote.welcomeSeen");
  if (welcomeSeen !== "true" && els.welcomeScreen) {
    els.welcomeScreen.hidden = false;
  }

  clearInterval(statusTimer);
  closeSocket();
  await openDashboard();
}




function formatFriendlyError(msg) {
  if (!msg) return "An unexpected error occurred.";
  const text = typeof msg === "string" ? msg : (msg.message || String(msg));
  const lower = text.toLowerCase();

  if (
    lower.includes("failed to fetch") ||
    lower.includes("networkerror") ||
    lower.includes("load failed") ||
    lower.includes("typeerror") ||
    lower.includes("connection refused") ||
    lower.includes("net::err")
  ) {
    return "Connection lost. PocketMC Desktop or local network host is currently unreachable.";
  }

  if (
    lower.includes("cloudflare") ||
    lower.includes("tunnel") ||
    lower.includes("<!doctype") ||
    lower.includes("<html")
  ) {
    return "Remote tunnel connection closed. Remote Access was stopped or the tunnel URL expired.";
  }

  if (lower.includes("unauthorized") || lower.includes("401")) {
    return "Session expired or invalid login. Please re-authenticate.";
  }

  if (lower.includes("404") || lower.includes("notfound")) {
    return "The requested server instance was not found.";
  }

  return text;
}

function renderInstancesSkeleton() {
  if (!els.instancesGrid) return;
  els.instancesGrid.innerHTML = Array(3).fill(0).map(() => `
    <div class="skeleton-card">
      <div class="skeleton-icon skeleton-box"></div>
      <div class="skeleton-body">
        <div class="skeleton-line title skeleton-box"></div>
        <div class="skeleton-line sub skeleton-box"></div>
      </div>
    </div>
  `).join("");
}

function renderAddonsSkeleton() {
  const grid = document.getElementById("addonsGrid");
  if (!grid) return;
  grid.innerHTML = Array(4).fill(0).map(() => `
    <div class="addon-card" style="min-height: 100px;">
      <div class="skeleton-body">
        <div class="skeleton-line title skeleton-box" style="width: 60%;"></div>
        <div class="skeleton-line sub skeleton-box" style="width: 35%;"></div>
      </div>
    </div>
  `).join("");
}

function showError(msg) {
  if (msg === "Unauthorized") return;
  const friendlyMsg = formatFriendlyError(msg);
  els.errorMessage.textContent = friendlyMsg;
  setVisible(els.errorView);
  if (els.connectionLabel) {
    els.connectionLabel.textContent = "Disconnected";
    els.connectionLabel.className = "connection-pill offline";
  }
}

async function openDashboard() {
  try {
    await refreshEverything({ reconnectConsole: true });
    statusTimer = setInterval(() => refreshEverything({ reconnectConsole: false }).catch(e => {
      if (e.message === "Unauthorized") clearInterval(statusTimer);
    }), 3000);
  } catch (error) {
    showError(error.message);
  }
}

async function refreshEverything({ reconnectConsole = false } = {}) {
  let instances = [];
  try {
    instances = await api("/api/instances");
    remoteStatusGlobal = await api("/api/status");
  } catch (error) {
    showError(error.message);
    throw error;
  }

  cachedInstances = instances;

  if (els.connectionLabel) {
    els.connectionLabel.textContent = remoteStatusGlobal.publicUrl || remoteStatusGlobal.localUrls?.[0] || "Connected";
    els.connectionLabel.className = "connection-pill online";
  }

  if (instances.length === 0) {
    historyLoadedForInstance = null;
    lastInstanceStatus = null;
    closeSocket();
    setVisible(els.emptyView);
    return;
  }

  if (currentView === "details" && (!selectedInstanceId || !instances.some((i) => i.id === selectedInstanceId))) {
    currentView = "selection";
  }

  if (currentView === "selection") {
    closeSocket();
    resetTabsToOverview();
    renderSelectionView(instances);
    setVisible(els.instanceSelectionView);
  } else {
    setVisible(els.appView);

    const instanceStatus = await api(`/api/instances/${selectedInstanceId}/status`);
    lastInstanceStatus = instanceStatus;

    renderStatus(remoteStatusGlobal, instanceStatus);

    if (reconnectConsole) {
      historyLoadedForInstance = null;
      closeSocket();
      loadServerSettings(selectedInstanceId);
      loadAddons(selectedInstanceId);
    }
    await ensureConsoleConnection(instanceStatus);
  }
}

function renderSelectionView(instances) {
  if (!els.instancesGrid) return;
  els.instancesGrid.innerHTML = "";

  const query = (searchQuery || "").toLowerCase().trim();
  const filtered = instances.filter(inst => {
    return inst.name.toLowerCase().includes(query) ||
      inst.serverType.toLowerCase().includes(query);
  });

  if (filtered.length === 0) {
    els.instancesGrid.innerHTML = `<p class="muted" style="text-align: center; margin: 32px 0;">No matching servers found.</p>`;
    return;
  }

  for (const inst of filtered) {
    const card = document.createElement("div");
    card.className = "instance-card";
    card.dataset.id = inst.id;

    const state = (inst.state || "").toLowerCase();
    const isOnline = inst.isRunning;
    const isBusy = ["starting", "stopping", "restarting", "settingup", "installing"].includes(state);
    const statusClass = isOnline ? "online" : (isBusy ? "busy" : "offline");
    const statusText = isBusy ? (inst.state || "").toUpperCase() : (isOnline ? "ONLINE" : "OFFLINE");

    const playerCount = inst.playerCount !== undefined ? inst.playerCount : 0;
    const maxPlayers = inst.maxPlayers !== undefined ? inst.maxPlayers : 20;
    const isPinned = !!inst.isPinned;
    const pinBadgeHtml = isPinned ? `<span class="card-pinned-icon" title="Pinned to top"><svg viewBox="0 0 24 24" width="11" height="11" fill="currentColor"><path d="M16 12V4h1V2H7v2h1v8l-2 2v2h5.2v6h1.6v-6H18v-2l-2-2z"/></svg></span>` : '';

    card.innerHTML = `
      <div class="instance-card-header">
        <div class="instance-card-icon-container">
          <img src="${getServerIcon(inst.serverType)}" alt="" class="instance-card-icon" />
        </div>
        <div class="instance-card-info">
          <h3>${escapeHtml(inst.name)}${pinBadgeHtml}</h3>
          <p>${escapeHtml(inst.serverType)} ${escapeHtml(inst.minecraftVersion || "")}</p>
        </div>
      </div>
      <hr class="instance-card-divider" />
      <div class="instance-card-footer">
        <span class="instance-status-pill ${statusClass}">
          <span class="status-dot"></span>
          ${escapeHtml(statusText)}
        </span>
        <span class="instance-player-count">
          <svg viewBox="0 0 24 24" class="player-icon-svg"><path d="M17 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2M9 11a4 4 0 1 0 0-8 4 4 0 0 0 0 8"/></svg>
          <span>${playerCount} / ${maxPlayers}</span>
        </span>
      </div>
    `;

    card.addEventListener("click", () => {
      selectedInstanceId = inst.id;
      localStorage.setItem(instanceKey, selectedInstanceId);
      currentView = "details";
      refreshEverything({ reconnectConsole: true });
    });

    els.instancesGrid.appendChild(card);
  }
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
  if (type.includes("neoforge")) return "/remote/icons/neoforge.png";
  if (type.includes("forge")) return "/remote/icons/forge.png";
  if (type.includes("paper") || type.includes("purpur")) return "/remote/icons/papermc.png";
  if (type.includes("bedrock") || type.includes("bds")) return "/remote/icons/bds.png";
  if (type.includes("pocketmine")) return "/remote/icons/pocketmine-mp.png";
  return "/remote/icons/vanilla.png";
}

function renderStatus(remoteStatus, instanceStatus) {
  els.serverName.textContent = instanceStatus.name;
  els.serverType.textContent = instanceStatus.serverType;

  if (els.pinnedBadge) {
    els.pinnedBadge.hidden = !instanceStatus.isPinned;
  }

  if (els.serverVersionBadge) {
    els.serverVersionBadge.textContent = instanceStatus.minecraftVersion ? `v${instanceStatus.minecraftVersion}` : "v1.20.1";
  }

  if (els.serverIpAddress) {
    els.serverIpAddress.hidden = true;
  }

  if (els.serverIconImage) {
    els.serverIconImage.src = getServerIcon(instanceStatus.serverType);
  }

  setStatusPill(instanceStatus);
  els.playerCount.textContent = `${instanceStatus.playerCount} / ${instanceStatus.maxPlayers}`;

  const ramUsageGb = (instanceStatus.ramUsageMb / 1024).toFixed(1);
  const maxRamGb = instanceStatus.maxRamMb ? (instanceStatus.maxRamMb / 1024).toFixed(0) : "4";
  els.ramUsage.innerHTML = `${ramUsageGb} <span class="metric-tile-unit">/ ${maxRamGb} GB</span>`;

  els.cpuUsage.textContent = `${instanceStatus.cpuUsage.toFixed(0)}`;
  els.uptime.textContent = formatUptime(instanceStatus.uptimeSeconds);

  // Update progress bars
  if (els.cpuProgressBar) {
    els.cpuProgressBar.style.width = `${instanceStatus.cpuUsage.toFixed(0)}%`;
  }
  if (els.ramProgressBar) {
    const ramPct = instanceStatus.maxRamMb > 0 ? (instanceStatus.ramUsageMb / instanceStatus.maxRamMb) * 100 : 0;
    els.ramProgressBar.style.width = `${ramPct.toFixed(0)}%`;
  }

  // Update players avatar list
  if (els.playersAvatarList) {
    els.playersAvatarList.innerHTML = "";
    const onlinePlayers = instanceStatus.onlinePlayers || [];
    if (onlinePlayers.length > 0) {
      onlinePlayers.slice(0, 5).forEach(player => {
        const circle = document.createElement("span");
        circle.className = "player-initial-circle";
        circle.textContent = player.name ? player.name.charAt(0).toUpperCase() : "?";
        circle.title = player.name;
        els.playersAvatarList.appendChild(circle);
      });
    } else {
      els.playersAvatarList.innerHTML = `<span class="muted" style="font-size: 12px; font-weight: normal;">No players online</span>`;
    }
  }

  const state = (instanceStatus.state || "").toLowerCase();
  const isBusy = ["starting", "stopping", "restarting", "settingup", "installing"].includes(state);

  // Transition state details
  const isStarting = state === "starting";
  const isStopping = state === "stopping";
  const isRestarting = state === "restarting";

  // Helpers to update button content
  const updateButtonState = (btn, isTransitioning, transitionText, defaultText) => {
    if (!btn) return;
    const icon = btn.querySelector(".button-icon");
    const spinner = btn.querySelector(".btn-spinner");
    const textEl = btn.querySelector(".btn-text");

    if (isTransitioning) {
      if (icon) icon.hidden = true;
      if (spinner) spinner.hidden = false;
      if (textEl) textEl.textContent = transitionText;
    } else {
      if (icon) icon.hidden = false;
      if (spinner) spinner.hidden = true;
      if (textEl) textEl.textContent = defaultText;
    }
  };

  updateButtonState(els.startButton, isStarting, "Starting...", "Start");
  updateButtonState(els.stopButton, isStopping, "Stopping...", "Stop");
  updateButtonState(els.restartButton, isRestarting, "Restarting...", "Restart");

  els.startButton.disabled = instanceStatus.isRunning || isBusy;
  els.stopButton.disabled = !instanceStatus.isRunning || isBusy;
  els.restartButton.disabled = !instanceStatus.isRunning || isBusy;



  const canSendCommands = remoteStatus.allowRemoteConsoleCommands && instanceStatus.isRunning;
  els.commandForm.hidden = !canSendCommands;
  if (els.quickCommandsBar) els.quickCommandsBar.hidden = !canSendCommands;
  els.commandDisabled.hidden = remoteStatus.allowRemoteConsoleCommands && instanceStatus.isRunning;

  if (els.offlinePlayerManage) {
    els.offlinePlayerManage.hidden = !remoteStatus.allowRemotePlayerActions;
  }
  if (els.playersDisabled) {
    els.playersDisabled.hidden = remoteStatus.allowRemotePlayerActions;
  }
  
  const settingsTabBtn = document.querySelector('.tab-button[data-tab="settings"]');
  if (settingsTabBtn) {
    settingsTabBtn.hidden = !remoteStatus.allowRemoteServerSettings;
    if (!remoteStatus.allowRemoteServerSettings && settingsTabBtn.classList.contains("active")) {
      resetTabsToOverview();
    }
  }

  const addonsTabBtn = document.querySelector('.tab-button[data-tab="addons"]');
  if (addonsTabBtn) {
    addonsTabBtn.hidden = !remoteStatus.allowRemoteServerAddons;
    if (!remoteStatus.allowRemoteServerAddons && addonsTabBtn.classList.contains("active")) {
      resetTabsToOverview();
    }
  }

  const fileManagerTabBtn = document.querySelector('.tab-button[data-tab="files"]');
  if (fileManagerTabBtn) {
    fileManagerTabBtn.hidden = !remoteStatus.allowRemoteFileManager;
    if (!remoteStatus.allowRemoteFileManager && fileManagerTabBtn.classList.contains("active")) {
      resetTabsToOverview();
    }
  }

  const backupsTabBtn = document.querySelector('.tab-button[data-tab="backups"]');
  if (backupsTabBtn) {
    backupsTabBtn.hidden = !remoteStatus.allowRemoteServerBackups;
    if (!remoteStatus.allowRemoteServerBackups && backupsTabBtn.classList.contains("active")) {
      resetTabsToOverview();
    }
  }

  renderPlayers(instanceStatus.onlinePlayers || [], remoteStatus.allowRemotePlayerActions);

  let filteredIps = [];
  if (state === "online") {
    filteredIps = (instanceStatus.serverIps || []).filter(ip => {
      const label = (ip.label || "").toLowerCase();

      // Keep Playit public IPs (excluding Voice)
      if (label.includes("playit") && !label.includes("voice")) {
        return true;
      }

      return false;
    });
  }

  if (filteredIps.length > 0) {
    if (els.serverIpAddress) els.serverIpAddress.hidden = true;
    els.serverIpsContainer.hidden = false;
    els.serverIpsList.innerHTML = "";

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
    if (els.serverIpAddress) {
      els.serverIpAddress.hidden = true;
    }
  }

  // Fix for iOS Safari scroll glitch when content shrinks below current scroll offset
  const mainContent = document.querySelector(".main-content");
  if (mainContent) {
    const maxScroll = Math.max(0, mainContent.scrollHeight - mainContent.clientHeight);
    if (mainContent.scrollTop > maxScroll) {
      mainContent.scrollTop = maxScroll;
    }
  }
}

function resetTabsToOverview() {
  els.tabs.forEach(t => t.classList.remove("active"));
  const allTabContents = document.querySelectorAll(".tab-content");
  allTabContents.forEach(c => { c.classList.remove("active"); c.hidden = true; });

  const defaultTabBtn = document.querySelector('.tab-button[data-tab="overview"]');
  if (defaultTabBtn) defaultTabBtn.classList.add("active");
  const overviewContent = document.getElementById("tab-overview");
  if (overviewContent) {
    overviewContent.classList.add("active");
    overviewContent.hidden = false;
  }
}

function setStatusPill(instanceStatus) {
  const state = (instanceStatus.state || "").toLowerCase();
  const isBusy = ["starting", "stopping", "restarting", "settingup", "installing"].includes(state);

  let statusText = "Offline";
  if (isBusy) {
    statusText = instanceStatus.state;
  } else if (instanceStatus.isRunning) {
    statusText = "Online";
  }

  els.statusPill.textContent = statusText;
  els.statusPill.className = "status-pill";
  els.statusPill.classList.add(isBusy ? "busy" : (instanceStatus.isRunning ? "online" : "offline"));
}

function formatUptime(seconds) {
  if (seconds < 0) return "0m";
  const d = Math.floor(seconds / 86400);
  const h = Math.floor((seconds % 86400) / 3600);
  const m = Math.floor((seconds % 3600) / 60);

  if (d > 0) return `${d}d ${h}h`;
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
    // Always clear — prevents stale logs from another instance leaking through
    if (historyLoadedForInstance !== selectedInstanceId) {
      els.consoleOutput.innerHTML = "";
      els.consoleOutput.textContent = "Server is offline.";
      historyLoadedForInstance = selectedInstanceId;
    } else if (!els.consoleOutput.hasChildNodes()) {
      els.consoleOutput.textContent = "Server is offline.";
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
    scrollConsole({ force: true });
  } catch (err) {
    els.consoleOutput.innerHTML = "Console history is not available yet.";
  }
}

let socketReconnectDelay = 1000;

async function openConsoleSocket() {
  if (!selectedInstanceId) return;
  closeSocket();

  els.consoleState.textContent = "Connecting";
  els.consoleState.className = "state-label busy";

  const protocol = window.location.protocol === "https:" ? "wss:" : "ws:";
  socket = new WebSocket(`${protocol}//${window.location.host}/ws/instances/${selectedInstanceId}/console`);

  socket.addEventListener("open", () => {
    socketReconnectDelay = 1000;
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
      els.consoleState.className = "state-label busy";
      setTimeout(() => {
        if (lastInstanceStatus?.isRunning && !socket) {
          openConsoleSocket();
        }
      }, socketReconnectDelay);
      socketReconnectDelay = Math.min(Math.round(socketReconnectDelay * 1.5), 30000);
    } else {
      els.consoleState.textContent = "Offline";
      els.consoleState.className = "state-label offline";
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

function scrollConsole({ force = false } = {}) {
  const container = els.consoleOutput;
  if (!container) return;

  const isNearBottom = container.scrollHeight - container.clientHeight - container.scrollTop < 120;

  if (force || isNearBottom) {
    container.scrollTop = container.scrollHeight;
    requestAnimationFrame(() => {
      container.scrollTop = container.scrollHeight;
    });
  }
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
    const firstChar = player.name ? player.name.charAt(0).toUpperCase() : "?";
    item.innerHTML = `
      <div class="player-name">
        <span class="player-avatar-circle">${escapeHtml(firstChar)}</span>
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
  } catch (err) {
    showNotice(err.message);
  }
}

// ---------------------------------------------------------
// Events
// ---------------------------------------------------------
els.tabs.forEach(tab => {
  tab.addEventListener("click", () => {
    els.tabs.forEach(t => t.classList.remove("active"));
    const allTabContents = document.querySelectorAll(".tab-content");
    allTabContents.forEach(c => { c.classList.remove("active"); c.hidden = true; });
    tab.classList.add("active");
    const content = document.getElementById(`tab-${tab.dataset.tab}`);
    if (content) {
      content.classList.add("active");
      content.hidden = false;
    }
    if (tab.dataset.tab === "console") scrollConsole({ force: true });
    if (tab.dataset.tab === "settings") loadServerSettings(selectedInstanceId);
    if (tab.dataset.tab === "addons") loadAddons(selectedInstanceId);
    if (tab.dataset.tab === "files") loadFiles(selectedInstanceId, currentFileDirectoryPath);
    if (tab.dataset.tab === "backups") loadBackups(selectedInstanceId);
  });
});

if (els.refreshButton) els.refreshButton.addEventListener("click", () => refreshEverything());
if (els.emptyRefreshButton) els.emptyRefreshButton.addEventListener("click", () => refreshEverything());
if (els.retryButton) els.retryButton.addEventListener("click", () => refreshEverything());



if (els.backToSelectorButton) {
  els.backToSelectorButton.addEventListener("click", () => {
    localStorage.removeItem(instanceKey);
    selectedInstanceId = null;
    currentView = "selection";
    closeSocket();
    resetTabsToOverview();
    refreshEverything();
  });
}

if (els.instanceSearchInput) {
  if (els.instanceSearchInput) els.instanceSearchInput.addEventListener("input", (e) => {
    searchQuery = e.target.value;
    renderSelectionView(cachedInstances);
  });
}




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

if (els.commandForm) els.commandForm.addEventListener("submit", async (e) => {
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
  return (unsafe || "").replace(/&/g, "&amp;")
    .replace(/</g, "&lt;").replace(/>/g, "&gt;")
    .replace(/"/g, "&quot;").replace(/'/g, "&#039;");
}

// Modal events
if (els.playerActionButtons) els.playerActionButtons.addEventListener("click", (e) => {
  if (e.target.tagName === "BUTTON") {
    const action = e.target.dataset.action;
    modalActionTarget.action = action;

    if (action === "kick" || action === "ban") {
      els.playerActionButtons.hidden = true;
      els.playerActionCloseRow.hidden = true;
      els.reasonModalForm.hidden = false;
      els.reasonModalInput.value = "";
      els.reasonModalInput.focus();
    } else {
      performPlayerAction(action, null);
    }
  }
});
if (els.playerActionModalClose) els.playerActionModalClose.addEventListener("click", () => {
  els.playerActionModal.hidden = true;
});
if (els.reasonModalCancel) els.reasonModalCancel.addEventListener("click", () => {
  els.reasonModalForm.hidden = true;
  els.playerActionButtons.hidden = false;
  els.playerActionCloseRow.hidden = false;
});
if (els.reasonModalForm) els.reasonModalForm.addEventListener("submit", (e) => {
  e.preventDefault();
  performPlayerAction(modalActionTarget.action, els.reasonModalInput.value.trim() || null);
});
if (els.offlinePlayerForm) els.offlinePlayerForm.addEventListener("submit", (e) => {
  e.preventDefault();
  const name = els.offlinePlayerInput.value.trim();
  if (!name) return;
  openPlayerModal(name);
  els.offlinePlayerInput.value = "";
});

if (els.loginForm) {
  if (els.loginForm) els.loginForm.addEventListener("submit", async (e) => {
    e.preventDefault();
    const password = els.loginPasswordInput.value;
    els.loginError.hidden = true;

    const btn = els.loginSubmitButton;
    const spinner = btn.querySelector(".btn-spinner");
    const icon = btn.querySelector(".button-icon");
    if (spinner) spinner.hidden = false;
    if (icon) icon.hidden = true;
    btn.disabled = true;

    try {
      const res = await fetch("/api/login", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ password })
      });

      if (res.ok) {
        els.loginPasswordInput.value = "";
        await openDashboard();
      } else {
        els.loginError.hidden = false;
      }
    } catch (err) {
      els.loginError.textContent = err.message || "Failed to login.";
      els.loginError.hidden = false;
    } finally {
      if (spinner) spinner.hidden = true;
      if (icon) icon.hidden = false;
      btn.disabled = false;
    }
  });
}

// Theme management
const themeKey = "pocketmc.remote.theme";
function applyTheme(theme) {
  document.documentElement.setAttribute("data-theme", theme);
  localStorage.setItem(themeKey, theme);
  const sunIcon = els.themeToggle?.querySelector(".sun-icon");
  const moonIcon = els.themeToggle?.querySelector(".moon-icon");
  if (sunIcon && moonIcon) {
    if (theme === "light") {
      sunIcon.setAttribute("hidden", "true");
      moonIcon.removeAttribute("hidden");
    } else {
      sunIcon.removeAttribute("hidden");
      moonIcon.setAttribute("hidden", "true");
    }
  }
}

function initTheme() {
  const savedTheme = localStorage.getItem(themeKey);
  const systemPrefersLight = window.matchMedia("(prefers-color-scheme: light)").matches;
  const initialTheme = savedTheme || (systemPrefersLight ? "light" : "dark");
  applyTheme(initialTheme);

  if (els.themeToggle) {
    if (els.themeToggle) els.themeToggle.addEventListener("click", () => {
      const currentTheme = document.documentElement.getAttribute("data-theme") || "dark";
      const nextTheme = currentTheme === "light" ? "dark" : "light";
      applyTheme(nextTheme);
    });
  }
}

initTheme();

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
  if (mobileMenuToggle) mobileMenuToggle.addEventListener('click', toggleMobileMenu);
}
if (sidebarOverlay) {
  if (sidebarOverlay) sidebarOverlay.addEventListener('click', toggleMobileMenu);
}

// Close sidebar when clicking a navigation item on mobile
document.addEventListener('click', (e) => {
  if (window.innerWidth <= 768 && appSidebar.classList.contains('open')) {
    if (e.target.closest('.sidebar-item') || e.target.closest('.instance-item')) {
      toggleMobileMenu();
    }
  }
});

// ---------------------------------------------------------
// Quick Commands Shortcuts
// ---------------------------------------------------------
document.addEventListener("click", async (e) => {
  const btn = e.target.closest(".quick-cmd-btn");
  if (!btn) return;
  const cmd = btn.dataset.cmd;
  if (!cmd || !selectedInstanceId) return;

  btn.disabled = true;
  try {
    await api(`/api/instances/${selectedInstanceId}/console/command`, {
      method: "POST",
      body: JSON.stringify({ command: cmd })
    });
    showNotice(`Sent command: /${cmd}`);
  } catch (err) {
    showNotice(`Failed to send command: ${err.message}`);
  } finally {
    btn.disabled = false;
  }
});

// ---------------------------------------------------------
// Settings Logic
// ---------------------------------------------------------
async function loadServerSettings(instanceId) {
  if (!instanceId) return;
  try {
    const props = await api(`/api/instances/${instanceId}/properties`);
    const setVal = (id, val) => { const el = document.getElementById(id); if (el) el.value = val !== undefined ? val : ""; };
    const setChk = (id, val) => { const el = document.getElementById(id); if (el) el.checked = !!val; };

    setVal("settingMotd", props.motd);
    setVal("settingGamemode", props.gamemode);
    setVal("settingDifficulty", props.difficulty);
    setVal("settingMaxPlayers", props.maxPlayers);
    setVal("settingViewDistance", props.viewDistance);
    setVal("settingSeed", props.seed);
    setChk("settingPvp", props.pvp);
    setChk("settingWhitelist", props.whitelist);
    setChk("settingAllowFlight", props.allowFlight);
    setChk("settingAllowCommandBlock", props.allowCommandBlock);
    setChk("settingAllowNether", props.allowNether);
  } catch (err) {
    showNotice(`Failed to load server settings: ${err.message}`);
  }
}

const settingsForm = document.getElementById("settingsForm");
if (settingsForm) {
  settingsForm.addEventListener("submit", async (e) => {
    e.preventDefault();
    if (!selectedInstanceId) return;
    const saveBtn = document.getElementById("btnSaveSettings");
    const statusMsg = document.getElementById("settingsSaveStatus");
    if (saveBtn) saveBtn.disabled = true;
    if (statusMsg) {
      statusMsg.hidden = false;
      statusMsg.className = "status-msg";
      statusMsg.textContent = "Saving settings...";
    }

    try {
      const payload = {
        motd: document.getElementById("settingMotd")?.value || "",
        gamemode: document.getElementById("settingGamemode")?.value || "survival",
        difficulty: document.getElementById("settingDifficulty")?.value || "easy",
        maxPlayers: parseInt(document.getElementById("settingMaxPlayers")?.value) || 20,
        viewDistance: document.getElementById("settingViewDistance")?.value || "10",
        seed: document.getElementById("settingSeed")?.value || "",
        pvp: document.getElementById("settingPvp")?.checked ?? true,
        whitelist: document.getElementById("settingWhitelist")?.checked ?? false,
        allowFlight: document.getElementById("settingAllowFlight")?.checked ?? false,
        allowCommandBlock: document.getElementById("settingAllowCommandBlock")?.checked ?? false,
        allowNether: document.getElementById("settingAllowNether")?.checked ?? true
      };

      await api(`/api/instances/${selectedInstanceId}/properties`, {
        method: "PUT",
        body: JSON.stringify(payload)
      });

      if (statusMsg) {
        statusMsg.textContent = "Settings saved successfully!";
        setTimeout(() => { statusMsg.hidden = true; }, 3000);
      }
      showNotice("Server properties updated.");
    } catch (err) {
      if (statusMsg) {
        statusMsg.className = "status-msg error";
        statusMsg.textContent = `Error: ${err.message}`;
      }
    } finally {
      if (saveBtn) saveBtn.disabled = false;
    }
  });
}

// ---------------------------------------------------------
// Addons Logic
// ---------------------------------------------------------
let cachedAddons = [];

async function loadAddons(instanceId) {
  if (!instanceId) return;
  const grid = document.getElementById("addonsGrid");
  if (!grid) return;
  renderAddonsSkeleton();

  try {
    cachedAddons = await api(`/api/instances/${instanceId}/addons`);
    renderAddons(cachedAddons);
  } catch (err) {
    grid.innerHTML = "";
    showNotice(formatFriendlyError(err));
  }
}

function formatFileSize(sizeKb) {
  if (!sizeKb || sizeKb <= 0) return 'Folder';
  if (sizeKb >= 1024 * 1024) {
    return (sizeKb / (1024 * 1024)).toFixed(1) + ' GB';
  }
  if (sizeKb >= 1024) {
    return (sizeKb / 1024).toFixed(1) + ' MB';
  }
  return sizeKb.toFixed(1) + ' KB';
}

function renderAddons(addons) {
  const grid = document.getElementById("addonsGrid");
  const noMsg = document.getElementById("noAddonsMsg");
  const countBadge = document.getElementById("addonsCountBadge");
  if (!grid) return;
  grid.innerHTML = "";
  if (countBadge) countBadge.textContent = `${addons.length} item${addons.length === 1 ? "" : "s"}`;

  if (!addons || addons.length === 0) {
    if (noMsg) noMsg.hidden = false;
    return;
  }
  if (noMsg) noMsg.hidden = true;

  addons.forEach(addon => {
    const card = document.createElement("div");
    card.className = "addon-card";
    const badgeClass = addon.addonType === "plugin" ? "plugin" : "pack";

    card.innerHTML = `
      <div class="addon-header">
        <div class="addon-title-group">
          <h4 class="addon-name">${escapeHtml(addon.name)}</h4>
          <span class="addon-meta">${formatFileSize(addon.sizeKb)}</span>
        </div>
        <span class="addon-badge ${badgeClass}">${escapeHtml(addon.addonType)}</span>
      </div>
      <div class="addon-actions">
        <button type="button" class="danger-button action-btn btn-uninstall-addon" data-path="${escapeHtml(addon.filePath)}">
          Uninstall
        </button>
      </div>
    `;

    card.querySelector(".btn-uninstall-addon").addEventListener("click", async (e) => {
      e.stopPropagation();
      if (!confirm(`Are you sure you want to uninstall ${addon.name}?`)) return;
      try {
        await api(`/api/instances/${selectedInstanceId}/addons/uninstall`, {
          method: "POST",
          body: JSON.stringify({ addonPathOrId: addon.filePath })
        });
        showNotice(`Uninstalled ${addon.name}`);
        loadAddons(selectedInstanceId);
      } catch (err) {
        showNotice(`Failed to uninstall: ${err.message}`);
      }
    });

    grid.appendChild(card);
  });
}

const addonSearch = document.getElementById("addonSearchInput");
if (addonSearch) {
  addonSearch.addEventListener("input", (e) => {
    const q = e.target.value.toLowerCase();
    const filtered = cachedAddons.filter(a => a.name.toLowerCase().includes(q) || a.addonType.toLowerCase().includes(q));
    renderAddons(filtered);
  });
}

// ---------------------------------------------------------
// Files Manager Logic
// ---------------------------------------------------------
let currentFileDirectoryPath = "";
let currentEditingFilePath = null;

async function loadFiles(instanceId, path = "") {
  if (!instanceId) return;
  currentFileDirectoryPath = path;
  const container = document.getElementById("filesListContainer");
  const noMsg = document.getElementById("noFilesMsg");
  if (!container) return;

  renderBreadcrumbs(path);
  container.innerHTML = `<div class="skeleton-card" style="height: 60px;"><div class="skeleton-body"><div class="skeleton-line title skeleton-box"></div></div></div>`;

  try {
    const items = await api(`/api/instances/${instanceId}/files?path=${encodeURIComponent(path)}`);
    renderFilesList(items);
  } catch (err) {
    container.innerHTML = "";
    showNotice(formatFriendlyError(err));
  }
}

function renderBreadcrumbs(path) {
  const container = document.getElementById("filesBreadcrumb");
  if (!container) return;
  container.innerHTML = "";
  container.style.display = "flex";
  container.style.alignItems = "center";
  container.style.gap = "8px";

  if (path) {
    const backBtn = document.createElement("button");
    backBtn.className = "icon-button";
    backBtn.style.padding = "4px";
    backBtn.style.marginRight = "4px";
    backBtn.innerHTML = `<svg viewBox="0 0 24 24" style="width:18px;height:18px;"><path d="M19 12H5M12 19l-7-7 7-7"/></svg>`;
    backBtn.title = "Go Back";
    backBtn.addEventListener("click", () => {
      const parentPath = path.includes("/") ? path.substring(0, path.lastIndexOf("/")) : "";
      loadFiles(selectedInstanceId, parentPath);
    });
    container.appendChild(backBtn);
  }

  const breadcrumbsWrapper = document.createElement("div");
  breadcrumbsWrapper.style.display = "flex";
  breadcrumbsWrapper.style.alignItems = "center";
  breadcrumbsWrapper.style.flexWrap = "wrap";

  const rootItem = document.createElement("span");
  rootItem.className = "breadcrumb-item";
  rootItem.textContent = "root";
  rootItem.addEventListener("click", () => loadFiles(selectedInstanceId, ""));
  breadcrumbsWrapper.appendChild(rootItem);

  if (path) {
    const parts = path.split("/").filter(Boolean);
    let acc = "";
    parts.forEach(part => {
      const sep = document.createElement("span");
      sep.className = "breadcrumb-separator";
      sep.textContent = " / ";
      breadcrumbsWrapper.appendChild(sep);

      acc = acc ? `${acc}/${part}` : part;
      const currentAcc = acc;
      const item = document.createElement("span");
      item.className = "breadcrumb-item";
      item.textContent = part;
      item.addEventListener("click", () => loadFiles(selectedInstanceId, currentAcc));
      breadcrumbsWrapper.appendChild(item);
    });
  }

  container.appendChild(breadcrumbsWrapper);
}

function isTextFile(ext) {
  if (!ext) return false;
  const cleanExt = ext.toLowerCase();
  const textExtensions = new Set([
    ".json", ".txt", ".properties", ".yml", ".yaml", ".toml",
    ".log", ".conf", ".config", ".ini", ".env", ".sh", ".bat",
    ".cmd", ".ps1", ".md", ".mcfunction", ".sk", ".xml", ".html",
    ".css", ".js", ".wlist"
  ]);
  return textExtensions.has(cleanExt);
}

function renderFilesList(items) {
  const container = document.getElementById("filesListContainer");
  const noMsg = document.getElementById("noFilesMsg");
  if (!container) return;
  container.innerHTML = "";

  if (noMsg) noMsg.hidden = !!(items && items.length > 0) || !!currentFileDirectoryPath;

  // The Directory Back (..) row has been integrated into the breadcrumbs header.

  if (!items || items.length === 0) return;

  items.forEach(item => {
    const row = document.createElement("div");
    row.className = "file-row";

    const canEdit = !item.isDirectory && isTextFile(item.extension);
    const iconSvg = item.isDirectory
      ? `<svg viewBox="0 0 24 24"><path d="M22 19a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h5l2 3h9a2 2 0 0 1 2 2z"/></svg>`
      : `<svg viewBox="0 0 24 24"><path d="M13 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V9z"></path><polyline points="13 2 13 9 20 9"></polyline></svg>`;

    row.innerHTML = `
      <div class="file-info">
        <span class="file-icon">${iconSvg}</span>
        <div>
          <div class="file-name">${escapeHtml(item.name)}</div>
          <div class="file-meta">
            ${item.isDirectory ? 'Folder' : formatFileSize(item.sizeBytes / 1024)}
            ${!item.isDirectory && !canEdit ? ' &bull; Raw File' : ''}
          </div>
        </div>
      </div>
      <div class="file-actions">
        ${canEdit ? `<button type="button" class="secondary-button btn-edit-file" style="padding: 4px 10px; font-size: 12px; margin-right: 6px;">Edit</button>` : ''}
        <button type="button" class="danger-button btn-delete-file" style="padding: 4px 10px; font-size: 12px;">Delete</button>
      </div>
    `;

    // Make card/row clickable
    row.addEventListener("click", (e) => {
      if (e.target.closest(".file-actions")) return;

      if (item.isDirectory) {
        loadFiles(selectedInstanceId, item.relativePath);
      } else if (canEdit) {
        openFileEditor(selectedInstanceId, item.relativePath);
      } else {
        showNotice(`Binary/raw file (${item.name}) cannot be edited in browser.`);
      }
    });

    const editBtn = row.querySelector(".btn-edit-file");
    if (editBtn) {
      editBtn.addEventListener("click", (e) => {
        e.stopPropagation();
        openFileEditor(selectedInstanceId, item.relativePath);
      });
    }

    row.querySelector(".btn-delete-file").addEventListener("click", async (e) => {
      e.stopPropagation();
      if (!confirm(`Are you sure you want to delete ${item.name}?`)) return;
      try {
        await api(`/api/instances/${selectedInstanceId}/files?path=${encodeURIComponent(item.relativePath)}`, { method: "DELETE" });
        showNotice(`Deleted ${item.name}`);
        loadFiles(selectedInstanceId, currentFileDirectoryPath);
      } catch (err) {
        showNotice(formatFriendlyError(err));
      }
    });

    container.appendChild(row);
  });
}

async function openFileEditor(instanceId, relativePath) {
  const modal = document.getElementById("fileEditorModal");
  const title = document.getElementById("editorFileName");
  const textarea = document.getElementById("editorTextarea");
  const saveBtn = document.getElementById("saveFileBtn");
  if (!modal || !textarea) return;

  currentEditingFilePath = relativePath;
  title.textContent = relativePath;
  textarea.value = "Loading content...";
  modal.hidden = false;
  if (saveBtn) saveBtn.hidden = false;

  try {
    const res = await api(`/api/instances/${instanceId}/files/content?path=${encodeURIComponent(relativePath)}`);
    textarea.value = res.content || "";
    const isReadOnly = !res.isText || res.isTruncated;
    textarea.readOnly = isReadOnly;
    if (saveBtn) saveBtn.hidden = isReadOnly;
  } catch (err) {
    textarea.value = `Error loading file content: ${err.message}`;
    textarea.readOnly = true;
    if (saveBtn) saveBtn.hidden = true;
  }
}

const closeEditor = () => {
  const modal = document.getElementById("fileEditorModal");
  if (modal) modal.hidden = true;
  currentEditingFilePath = null;
};
document.getElementById("closeEditorBtn")?.addEventListener("click", closeEditor);
document.getElementById("cancelEditorBtn")?.addEventListener("click", closeEditor);

document.getElementById("saveFileBtn")?.addEventListener("click", async () => {
  if (!selectedInstanceId || !currentEditingFilePath) return;
  const textarea = document.getElementById("editorTextarea");
  const saveBtn = document.getElementById("saveFileBtn");
  if (!textarea || !saveBtn) return;

  try {
    saveBtn.disabled = true;
    await api(`/api/instances/${selectedInstanceId}/files/content`, {
      method: "PUT",
      body: JSON.stringify({
        relativePath: currentEditingFilePath,
        content: textarea.value
      })
    });
    showNotice("File saved successfully.");
    closeEditor();
    loadFiles(selectedInstanceId, currentFileDirectoryPath);
  } catch (err) {
    showNotice(formatFriendlyError(err));
  } finally {
    saveBtn.disabled = false;
  }
});

const refreshFilesBtn = document.getElementById("refreshFilesBtn");
if (refreshFilesBtn) {
  refreshFilesBtn.addEventListener("click", () => loadFiles(selectedInstanceId, currentFileDirectoryPath));
}

// ---------------------------------------------------------
// Backups Manager Logic
// ---------------------------------------------------------
async function loadBackups(instanceId) {
  if (!instanceId) return;
  const container = document.getElementById("backupsListContainer");
  const noMsg = document.getElementById("noBackupsMsg");
  if (!container) return;

  container.innerHTML = `<div class="skeleton-card" style="height: 70px;"><div class="skeleton-body"><div class="skeleton-line title skeleton-box"></div></div></div>`;

  try {
    const data = await api(`/api/instances/${instanceId}/backups`);
    const isRunning = data && data.isBackupRunning === true;
    const backups = Array.isArray(data) ? data : (data.backups || []);

    renderBackupsList(backups, isRunning);

    if (isRunning) {
      setTimeout(() => {
        if (selectedInstanceId === instanceId) {
          loadBackups(instanceId);
        }
      }, 3000);
    }
  } catch (err) {
    container.innerHTML = "";
    showNotice(formatFriendlyError(err));
  }
}

function renderBackupsList(backups, isRunning = false) {
  const container = document.getElementById("backupsListContainer");
  const noMsg = document.getElementById("noBackupsMsg");
  if (!container) return;
  container.innerHTML = "";

  if (createBackupBtn) {
    createBackupBtn.disabled = isRunning;
  }

  if (isRunning) {
    const loader = document.createElement("div");
    loader.className = "backup-row";
    loader.style.pointerEvents = "none";
    loader.innerHTML = `
      <div class="backup-info" style="overflow: hidden;">
        <span class="backup-icon skeleton-box" style="width: 24px; height: 24px; border-radius: 4px; display: inline-block;"></span>
        <div style="min-width: 0; flex: 1; padding-top: 2px;">
          <div class="skeleton-box" style="height: 16px; width: 60%; margin-bottom: 6px; border-radius: 4px;"></div>
          <div class="skeleton-box" style="height: 14px; width: 40%; border-radius: 4px;"></div>
        </div>
      </div>
    `;
    container.appendChild(loader);
    if (noMsg) noMsg.hidden = true;
  } else if (!backups || backups.length === 0) {
    if (noMsg) noMsg.hidden = false;
    return;
  } else {
    if (noMsg) noMsg.hidden = true;
  }

  backups.forEach(backup => {
    const row = document.createElement("div");
    row.className = "backup-row";

    const dateStr = backup.createdAt ? new Date(backup.createdAt).toLocaleString() : "";
    let displayTitle = backup.label ? escapeHtml(backup.label) : escapeHtml(backup.fileName);
    row.innerHTML = `
      <div class="backup-info" style="overflow: hidden;">
        <span class="backup-icon">
          <svg viewBox="0 0 24 24"><path d="M19 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h11l5 5v11a2 2 0 0 1-2 2z"></path><polyline points="17 21 17 13 7 13 7 21"></polyline></svg>
        </span>
        <div>
                            <div style="min-width: 0; flex: 1;">
          <div class="backup-name" style="white-space: nowrap; overflow: hidden; text-overflow: ellipsis;" title="${escapeHtml(backup.fileName)}">
            ${displayTitle}

          </div>
          <div class="backup-meta" style="white-space: nowrap; overflow: hidden; text-overflow: ellipsis;">
            <span>${formatFileSize(backup.sizeBytes / 1024)}</span>
            
            <span>&bull;</span>
            <span>${escapeHtml(dateStr)}</span>
            
          </div>
        </div>
      </div>
    `;
    container.appendChild(row);
  });
}

const createBackupBtn = document.getElementById("createBackupBtn");
if (createBackupBtn) {
  createBackupBtn.addEventListener("click", async () => {
    if (!selectedInstanceId) return;
    try {
      createBackupBtn.disabled = true;
      showNotice("Starting background backup...");
      const result = await api(`/api/instances/${selectedInstanceId}/backups`, { method: "POST" });

      loadBackups(selectedInstanceId);
    } catch (err) {
      showNotice(formatFriendlyError(err));
      createBackupBtn.disabled = false;
    }
  });
}

