// --------------------------------------------------------------------------
// Ragnarok Rebuild Fleet Orchestrator - Client Application Engine
// --------------------------------------------------------------------------

let ws = null;
let currentFleet = null;
let telemetryHistory = {
  timestamps: [],
  series: {} // profileName -> [expValues]
};
let perfChart = null;
let activeLogTab = 'all';
let botLogs = { 'all': [] }; // tabId -> Array of log objects
let knownProfiles = new Set();

// Initialize Dashboard
document.addEventListener('DOMContentLoaded', () => {
  initChart();
  fetchInitialFleet();
  connectWebSocket();
  setupEventListeners();
  loadInitialLogs('all');
});

function fetchInitialFleet() {
  fetch('/api/fleet')
    .then(r => r.json())
    .then(data => {
      if (data) updateDashboard(data);
    })
    .catch(() => {});
}

// WebSocket Telemetry Connection
function connectWebSocket() {
  const protocol = window.location.protocol === 'https:' ? 'wss:' : 'ws:';
  const wsUrl = `${protocol}//${window.location.host}/ws/fleet`;

  ws = new WebSocket(wsUrl);

  ws.onopen = () => {
    document.getElementById('ws-status-dot').classList.remove('offline');
    document.getElementById('ws-status-text').textContent = 'Live Connected';
  };

  ws.onmessage = (event) => {
    try {
      const msg = JSON.parse(event.data);
      if (msg.type === 'fleet_update' && msg.payload) {
        updateDashboard(msg.payload);
      } else if (msg.type === 'bot_log' && msg.payload) {
        appendBotLog(msg.payload.profile, msg.payload.line, msg.payload.timestamp);
      }
    } catch (e) {
      console.error('Error parsing WS message:', e);
    }
  };

  ws.onclose = () => {
    document.getElementById('ws-status-dot').classList.add('offline');
    document.getElementById('ws-status-text').textContent = 'Reconnecting...';
    setTimeout(connectWebSocket, 2000);
  };

  ws.onerror = () => {
    ws.close();
  };
}

// Update UI from Fleet Snapshot
function updateDashboard(fleet) {
  if (!fleet) return;
  currentFleet = fleet;

  const runningBots = fleet.runningBots ?? fleet.RunningBots ?? 0;
  const totalBots = fleet.totalBots ?? fleet.TotalBots ?? 0;
  const totalExp = fleet.totalBaseExpPerHour ?? fleet.TotalBaseExpPerHour ?? 0;
  const totalZeny = fleet.totalZeny ?? fleet.TotalZeny ?? 0;
  const totalKills = fleet.totalKills ?? fleet.TotalKills ?? 0;
  const profiles = fleet.profiles ?? fleet.Profiles ?? [];
  const monitors = fleet.monitors ?? fleet.Monitors ?? [];

  // 1. Update KPI Summary Cards
  document.getElementById('kpi-active-bots').textContent = `${runningBots} / ${totalBots}`;
  document.getElementById('kpi-fleet-exp').textContent = formatExpRate(totalExp);
  document.getElementById('kpi-fleet-zeny').textContent = formatNumber(totalZeny) + ' z';
  document.getElementById('kpi-total-kills').textContent = formatNumber(totalKills);

  // 2. Update Monitor Options
  const monitorSelect = document.getElementById('monitor-select');
  if (monitors && monitors.length > 0 && monitorSelect.options.length !== monitors.length) {
    monitorSelect.innerHTML = '';
    monitors.forEach((m) => {
      const idx = m.index ?? m.Index ?? 0;
      const name = m.deviceName ?? m.DeviceName ?? `Monitor ${idx}`;
      const w = m.width ?? m.Width ?? 1920;
      const h = m.height ?? m.Height ?? 1080;
      const isPrimary = m.isPrimary ?? m.IsPrimary ?? false;

      const opt = document.createElement('option');
      opt.value = idx;
      opt.textContent = `${name} (${w}x${h})${isPrimary ? ' [Primary]' : ''}`;
      monitorSelect.appendChild(opt);
    });
  }

  // 3. Render Fleet Cards Grid
  renderFleetGrid(profiles);

  // 4. Update Tab Bar in Terminal
  updateLogTabs(profiles);

  // 5. Update Time-Series Chart
  updateChartData(fleet);
}

// Render Bot Cards Grid with In-Place DOM Diffing (Prevents Hover/Click Flickering)
function renderFleetGrid(profiles) {
  const grid = document.getElementById('fleet-grid');
  if (!grid) return;

  if (!profiles || profiles.length === 0) {
    grid.innerHTML = `<div style="grid-column: 1/-1; text-align: center; color: var(--text-muted); padding: 40px;">No bot profiles discovered. Check accounts.json or profiles/ directory.</div>`;
    return;
  }

  const existingEmpty = grid.querySelector('div[style*="grid-column: 1/-1"]');
  if (existingEmpty) existingEmpty.remove();

  const currentNames = new Set();

  profiles.forEach((bot) => {
    const name = bot.profileName ?? bot.ProfileName ?? '';
    currentNames.add(name);

    let card = document.getElementById(`card-${name}`);
    if (!card) {
      const temp = document.createElement('div');
      temp.innerHTML = createBotCardHtml(bot);
      card = temp.firstElementChild;
      grid.appendChild(card);
    } else {
      updateBotCardDom(card, bot);
    }
  });

  // Remove any cards that no longer exist
  Array.from(grid.children).forEach((child) => {
    if (child.id && child.id.startsWith('card-')) {
      const pName = child.id.replace('card-', '');
      if (!currentNames.has(pName)) {
        child.remove();
      }
    }
  });
}

function updateBotCardDom(card, bot) {
  const isRunning = bot.isRunning ?? bot.IsRunning ?? false;
  const isWindowVisible = bot.isWindowVisible ?? bot.IsWindowVisible ?? false;
  const status = bot.status ?? bot.Status ?? {};
  const macro = bot.macroStatus ?? bot.MacroStatus ?? {};

  const name = bot.profileName ?? bot.ProfileName ?? '';
  const accountId = bot.accountId ?? bot.AccountId ?? '';
  const processId = bot.processId ?? bot.ProcessId ?? 0;
  const cpuPercent = bot.cpuPercent ?? bot.CpuPercent ?? 0;
  const ramMb = bot.ramMegabytes ?? bot.RamMegabytes ?? 0;

  const job = status.jobName ?? status.JobName ?? (isRunning ? 'Starting Up...' : 'Offline');
  const levelVal = status.level ?? status.Level ?? 0;
  const level = levelVal > 0 ? `Lv ${levelVal}` : '';
  const state = isRunning ? (status.botState ?? status.BotState ?? 'Launching') : 'Offline';

  const displayState = {
    'Launching': 'Launching',
    'Connecting': 'Connecting',
    'LoggingIn': 'Logging In',
    'SubmittingLogin': 'Submitting Credentials',
    'SelectingCharacter': 'Selecting Character',
    'AwaitingCharSelect': 'Character Select',
    'EnteringWorld': 'Entering World',
    'Reconnecting': 'Reconnecting',
    'DismissingNotice': 'Dismissing Notice',
    'TownRoutine': 'Town Routine',
    'Wandering': 'Wandering',
    'Combat': 'In Combat',
    'Looting': 'Looting',
    'Resting': 'Resting',
    'Dead': 'Dead',
    'Disabled': 'Disabled',
    'Offline': 'Offline'
  }[state] || state;

  const isTransitionState = ['Launching', 'Connecting', 'LoggingIn', 'SubmittingLogin', 'SelectingCharacter', 'AwaitingCharSelect', 'EnteringWorld', 'Reconnecting', 'DismissingNotice', 'TownRoutine'].includes(state);

  const hp = status.hp ?? status.Hp ?? 0;
  const maxHp = status.maxHp ?? status.MaxHp ?? 1;
  const hpPct = Math.min(100, Math.max(0, (hp / maxHp) * 100));

  const sp = status.sp ?? status.Sp ?? 0;
  const maxSp = status.maxSp ?? status.MaxSp ?? 1;
  const spPct = Math.min(100, Math.max(0, (sp / maxSp) * 100));

  const baseExp = status.baseExp ?? status.BaseExp ?? 0;
  const maxBaseExp = status.maxBaseExp ?? status.MaxBaseExp ?? 0;
  const expPct = maxBaseExp > 0 ? ((baseExp / maxBaseExp) * 100).toFixed(1) : 0;

  const weight = status.weight ?? status.Weight ?? 0;
  const maxWeight = status.maxWeight ?? status.MaxWeight ?? 1;
  const weightPct = maxWeight > 0 ? Math.round((weight / maxWeight) * 100) : 0;

  const expRateVal = status.baseExpPerHour ?? status.BaseExpPerHour ?? 0;
  const expRate = formatExpRate(expRateVal);
  const zeny = formatNumber(status.zeny ?? status.Zeny ?? 0) + ' z';

  const currentMap = status.currentMap ?? status.CurrentMap;
  const posX = status.positionX ?? status.PositionX ?? 0;
  const posY = status.positionY ?? status.PositionY ?? 0;
  const mapName = currentMap ? `${currentMap} (${posX}, ${posY})` : 'None';

  const kills = status.monstersKilled ?? status.MonstersKilled ?? 0;
  const cpu = (Number(cpuPercent) || 0).toFixed(1) + '%';
  const ram = Math.round(Number(ramMb) || 0) + ' MB';

  const currentMacro = status.currentMacro ?? status.CurrentMacro ?? '';
  const hasActiveMacro = macro.hasActiveMacro ?? macro.HasActiveMacro ?? false;

  const statusBadgeClass = isRunning
    ? (isTransitionState ? 'badge-amber' : (state === 'Disabled' ? 'badge-slate' : 'badge-emerald'))
    : 'badge-slate';

  // Update card classes
  card.className = `bot-card ${isRunning ? 'running' : 'offline'}`;

  // Update identity subtitle
  const subEl = card.querySelector('.bot-identity p');
  if (subEl) subEl.textContent = `${level ? `${level} ` : ''}${job}${bot.accountId ? ` · [${bot.accountId}]` : ''}`;

  // Update state badge
  const badgeEl = card.querySelector('.badge-pill');
  if (badgeEl) {
    badgeEl.className = `badge-pill ${statusBadgeClass}`;
    badgeEl.textContent = displayState;
  }

  // Update process info
  let procEl = card.querySelector('.bot-proc-info');
  if (isRunning) {
    if (!procEl) {
      const headerStatus = card.querySelector('.bot-header-status');
      if (headerStatus) {
        procEl = document.createElement('div');
        procEl.className = 'bot-proc-info';
        headerStatus.appendChild(procEl);
      }
    }
    if (procEl) {
      procEl.innerHTML = `${bot.processId ? `<span class="pid-tag">PID ${bot.processId}</span>` : ''}<span>${cpu} CPU</span><span>·</span><span>${ram} RAM</span>`;
    }
  } else if (procEl) {
    procEl.remove();
  }

  // Update vitals
  const hpLabel = card.querySelector('.vital-row:nth-child(1) .vital-labels span:nth-child(2)');
  const hpFill = card.querySelector('.vital-bar-fill.hp');
  if (hpLabel) hpLabel.textContent = `${formatNumber(hp)} / ${formatNumber(maxHp)} (${Math.round(hpPct)}%)`;
  if (hpFill) hpFill.style.width = `${hpPct}%`;

  const spLabel = card.querySelector('.vital-row:nth-child(2) .vital-labels span:nth-child(2)');
  const spFill = card.querySelector('.vital-bar-fill.sp');
  if (spLabel) spLabel.textContent = `${formatNumber(sp)} / ${formatNumber(maxSp)} (${Math.round(spPct)}%)`;
  if (spFill) spFill.style.width = `${spPct}%`;

  const expLabel = card.querySelector('.vital-row:nth-child(3) .vital-labels span:nth-child(2)');
  const expFill = card.querySelector('.vital-bar-fill.exp');
  if (expLabel) expLabel.textContent = `${expPct}%`;
  if (expFill) expFill.style.width = `${expPct}%`;

  const weightLabel = card.querySelector('.vital-row:nth-child(4) .vital-labels span:nth-child(2)');
  const weightFill = card.querySelector('.vital-bar-fill.weight');
  if (weightLabel) weightLabel.textContent = `${weightPct}%`;
  if (weightFill) weightFill.style.width = `${weightPct}%`;

  // Update Stats Matrix
  const expVal = card.querySelector('.stat-item:nth-child(1) .value');
  if (expVal) expVal.textContent = expRate;

  const zenyVal = card.querySelector('.stat-item:nth-child(2) .value');
  if (zenyVal) zenyVal.textContent = zeny;

  const mapVal = card.querySelector('.stat-item:nth-child(3) .value');
  if (mapVal) mapVal.textContent = mapName;

  const killsVal = card.querySelector('.stat-item:nth-child(4) .value');
  if (killsVal) killsVal.textContent = kills;

  // Update Macro Banner
  let banner = card.querySelector('.macro-banner');
  if (hasActiveMacro || currentMacro) {
    if (!banner) {
      banner = document.createElement('div');
      banner.className = 'macro-banner';
      const matrix = card.querySelector('.stats-matrix');
      if (matrix) matrix.insertAdjacentElement('afterend', banner);
    }
    banner.innerHTML = `<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M12 2v4M12 18v4M4.93 4.93l2.83 2.83M16.24 16.24l2.83 2.83M2 12h4M18 12h4M4.93 19.07l2.83-2.83M16.24 7.76l2.83-2.83"/></svg><span style="overflow: hidden; text-overflow: ellipsis; white-space: nowrap;">${currentMacro || 'Executing Macro Action...'}</span>`;
  } else if (banner) {
    banner.remove();
  }

  // Update Action Buttons ONLY when running or visibility state changes
  if (card.dataset.runningState !== isRunning.toString() || card.dataset.windowVisible !== isWindowVisible.toString()) {
    card.dataset.runningState = isRunning.toString();
    card.dataset.windowVisible = isWindowVisible.toString();

    const actions = card.querySelector('.card-actions');
    if (actions) {
      actions.innerHTML = `
        ${isRunning ? `
          <button class="btn btn-danger btn-sm" onclick="stopBot('${name}')">Stop</button>
          <button class="btn btn-secondary btn-sm" onclick="focusWindow('${name}')">Focus</button>
          ${isWindowVisible ? `
            <button class="btn btn-secondary btn-sm" onclick="hideWindow('${name}')" title="Hide game & console windows to background">Hide</button>
          ` : `
            <button class="btn btn-emerald btn-sm" onclick="showWindow('${name}')" title="Restore game window to desktop">Show</button>
          `}
        ` : `
          <button class="btn btn-emerald btn-sm" onclick="startBot('${name}', '${accountId}')">Start</button>
        `}
        <button class="btn btn-secondary btn-sm" onclick="openMacroModal('${name}')">Macro</button>
        <button class="btn btn-secondary btn-sm" onclick="openConfigModal('${name}')">Config</button>
      `;
    }
  }
}

function createBotCardHtml(bot) {
  const isRunning = bot.isRunning ?? bot.IsRunning ?? false;
  const isWindowVisible = bot.isWindowVisible ?? bot.IsWindowVisible ?? false;
  const status = bot.status ?? bot.Status ?? {};
  const macro = bot.macroStatus ?? bot.MacroStatus ?? {};

  const name = bot.profileName ?? bot.ProfileName ?? '';
  const accountId = bot.accountId ?? bot.AccountId ?? '';
  const processId = bot.processId ?? bot.ProcessId ?? 0;
  const cpuPercent = bot.cpuPercent ?? bot.CpuPercent ?? 0;
  const ramMb = bot.ramMegabytes ?? bot.RamMegabytes ?? 0;

  const job = status.jobName ?? status.JobName ?? (isRunning ? 'Starting Up...' : 'Offline');
  const levelVal = status.level ?? status.Level ?? 0;
  const level = levelVal > 0 ? `Lv ${levelVal}` : '';
  const state = isRunning ? (status.botState ?? status.BotState ?? 'Launching') : 'Offline';

  const displayState = {
    'Launching': 'Launching',
    'Connecting': 'Connecting',
    'LoggingIn': 'Logging In',
    'SubmittingLogin': 'Submitting Credentials',
    'SelectingCharacter': 'Selecting Character',
    'AwaitingCharSelect': 'Character Select',
    'EnteringWorld': 'Entering World',
    'Reconnecting': 'Reconnecting',
    'DismissingNotice': 'Dismissing Notice',
    'TownRoutine': 'Town Routine',
    'Wandering': 'Wandering',
    'Combat': 'In Combat',
    'Looting': 'Looting',
    'Resting': 'Resting',
    'Dead': 'Dead',
    'Disabled': 'Disabled',
    'Offline': 'Offline'
  }[state] || state;

  const isTransitionState = ['Launching', 'Connecting', 'LoggingIn', 'SubmittingLogin', 'SelectingCharacter', 'AwaitingCharSelect', 'EnteringWorld', 'Reconnecting', 'DismissingNotice', 'TownRoutine'].includes(state);

  const hp = status.hp ?? status.Hp ?? 0;
  const maxHp = status.maxHp ?? status.MaxHp ?? 1;
  const hpPct = Math.min(100, Math.max(0, (hp / maxHp) * 100));

  const sp = status.sp ?? status.Sp ?? 0;
  const maxSp = status.maxSp ?? status.MaxSp ?? 1;
  const spPct = Math.min(100, Math.max(0, (sp / maxSp) * 100));

  const baseExp = status.baseExp ?? status.BaseExp ?? 0;
  const maxBaseExp = status.maxBaseExp ?? status.MaxBaseExp ?? 0;
  const expPct = maxBaseExp > 0 ? ((baseExp / maxBaseExp) * 100).toFixed(1) : 0;

  const weight = status.weight ?? status.Weight ?? 0;
  const maxWeight = status.maxWeight ?? status.MaxWeight ?? 1;
  const weightPct = maxWeight > 0 ? Math.round((weight / maxWeight) * 100) : 0;

  const expRateVal = status.baseExpPerHour ?? status.BaseExpPerHour ?? 0;
  const expRate = formatExpRate(expRateVal);
  const zeny = formatNumber(status.zeny ?? status.Zeny ?? 0) + ' z';

  const currentMap = status.currentMap ?? status.CurrentMap;
  const posX = status.positionX ?? status.PositionX ?? 0;
  const posY = status.positionY ?? status.PositionY ?? 0;
  const mapName = currentMap ? `${currentMap} (${posX}, ${posY})` : 'None';

  const kills = status.monstersKilled ?? status.MonstersKilled ?? 0;
  const cpu = (Number(cpuPercent) || 0).toFixed(1) + '%';
  const ram = Math.round(Number(ramMb) || 0) + ' MB';

  const currentMacro = status.currentMacro ?? status.CurrentMacro ?? '';
  const hasActiveMacro = macro.hasActiveMacro ?? macro.HasActiveMacro ?? false;

  const statusBadgeClass = isRunning
    ? (isTransitionState ? 'badge-amber' : (state === 'Disabled' ? 'badge-slate' : 'badge-emerald'))
    : 'badge-slate';

  return `
    <div class="bot-card ${isRunning ? 'running' : 'offline'}" id="card-${name}" data-running-state="${isRunning}" data-window-visible="${isWindowVisible}">
      <!-- Header -->
      <div class="card-header">
        <div class="bot-identity">
          <h3>${name}</h3>
          <p>${level ? `${level} ` : ''}${job}${bot.accountId ? ` · [${bot.accountId}]` : ''}</p>
        </div>
        <div class="bot-header-status">
          <span class="badge-pill ${statusBadgeClass}">${displayState}</span>
          ${isRunning ? `<div class="bot-proc-info">${bot.processId ? `<span class="pid-tag">PID ${bot.processId}</span>` : ''}<span>${cpu} CPU</span><span>·</span><span>${ram} RAM</span></div>` : ''}
        </div>
      </div>

      <!-- Vitals (Tremor Slim Progress Bars) -->
      <div class="vitals-container">
        <!-- HP -->
        <div class="vital-row">
          <div class="vital-labels">
            <span>HP</span>
            <span>${formatNumber(hp)} / ${formatNumber(maxHp)} (${Math.round(hpPct)}%)</span>
          </div>
          <div class="vital-bar-track">
            <div class="vital-bar-fill hp" style="width: ${hpPct}%;"></div>
          </div>
        </div>

        <!-- SP -->
        <div class="vital-row">
          <div class="vital-labels">
            <span>SP</span>
            <span>${formatNumber(sp)} / ${formatNumber(maxSp)} (${Math.round(spPct)}%)</span>
          </div>
          <div class="vital-bar-track">
            <div class="vital-bar-fill sp" style="width: ${spPct}%;"></div>
          </div>
        </div>

        <!-- EXP Progress -->
        <div class="vital-row">
          <div class="vital-labels">
            <span>BASE EXP</span>
            <span>${expPct}%</span>
          </div>
          <div class="vital-bar-track">
            <div class="vital-bar-fill exp" style="width: ${expPct}%;"></div>
          </div>
        </div>

        <!-- Weight -->
        <div class="vital-row">
          <div class="vital-labels">
            <span>WEIGHT</span>
            <span>${weightPct}%</span>
          </div>
          <div class="vital-bar-track">
            <div class="vital-bar-fill weight" style="width: ${weightPct}%;"></div>
          </div>
        </div>
      </div>

      <!-- Stats Metrics Matrix -->
      <div class="stats-matrix">
        <div class="stat-item">
          <span class="label">EXP Rate</span>
          <span class="value" style="color: var(--accent-blue);">${expRate}</span>
        </div>
        <div class="stat-item">
          <span class="label">Zeny</span>
          <span class="value" style="color: var(--accent-amber);">${zeny}</span>
        </div>
        <div class="stat-item">
          <span class="label">Location</span>
          <span class="value" style="font-size: 0.8rem; overflow: hidden; text-overflow: ellipsis; white-space: nowrap;">${mapName}</span>
        </div>
        <div class="stat-item">
          <span class="label">Monster Kills</span>
          <span class="value" style="color: var(--accent-emerald);">${kills}</span>
        </div>
      </div>

      <!-- Active Macro Banner (if running) -->
      ${hasActiveMacro || currentMacro ? `
        <div class="macro-banner">
          <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M12 2v4M12 18v4M4.93 4.93l2.83 2.83M16.24 16.24l2.83 2.83M2 12h4M18 12h4M4.93 19.07l2.83-2.83M16.24 7.76l2.83-2.83"/></svg>
          <span style="overflow: hidden; text-overflow: ellipsis; white-space: nowrap;">${currentMacro || 'Executing Macro Action...'}</span>
        </div>
      ` : ''}

      <!-- Action Footer (Per-Bot Control Buttons) -->
      <div class="card-actions">
        ${isRunning ? `
          <button class="btn btn-danger btn-sm" onclick="stopBot('${name}')">Stop</button>
          <button class="btn btn-secondary btn-sm" onclick="focusWindow('${name}')">Focus</button>
          ${isWindowVisible ? `
            <button class="btn btn-secondary btn-sm" onclick="hideWindow('${name}')" title="Hide game & console windows to background">Hide</button>
          ` : `
            <button class="btn btn-emerald btn-sm" onclick="showWindow('${name}')" title="Restore game window to desktop">Show</button>
          `}
        ` : `
          <button class="btn btn-emerald btn-sm" onclick="startBot('${name}', '${accountId}')">Start</button>
        `}
        <button class="btn btn-secondary btn-sm" onclick="openMacroModal('${name}')">Macro</button>
        <button class="btn btn-secondary btn-sm" onclick="openConfigModal('${name}')">Config</button>
      </div>
    </div>
  `;
}

// Window Management Calls
function tileWindows(layoutType) {
  const monitorSelect = document.getElementById('monitor-select');
  const monitorIndex = parseInt(monitorSelect.value, 10) || 0;

  fetch('/api/windows/tile', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ layoutType, monitorIndex })
  })
    .then(r => r.json())
    .then(data => {
      if (!data.success) alert(data.error || 'Failed to tile windows');
    });
}

function hideWindow(profile) {
  fetch(`/api/windows/hide/${encodeURIComponent(profile)}`, { method: 'POST' });
}

function showWindow(profile) {
  fetch(`/api/windows/show/${encodeURIComponent(profile)}`, { method: 'POST' });
}

function hideAll() {
  fetch('/api/windows/hide-all', { method: 'POST' });
}

function showAll() {
  fetch('/api/windows/show-all', { method: 'POST' });
}

function minimizeAll() {
  fetch('/api/windows/minimize-all', { method: 'POST' });
}

function restoreAll() {
  fetch('/api/windows/restore-all', { method: 'POST' });
}

function focusWindow(profile) {
  fetch(`/api/windows/focus/${encodeURIComponent(profile)}`, { method: 'POST' });
}

// Bot Process Calls
function startBot(profileName, accountId) {
  fetch('/api/bot/start', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ profileName, accountId, lowSpec: false, hidden: true, targetFps: 30 })
  })
    .then(r => r.json())
    .then(data => {
      if (!data.success) alert(data.error || 'Failed to start bot');
    });
}

function stopBot(profileName) {
  fetch(`/api/bot/stop/${encodeURIComponent(profileName)}`, { method: 'POST' });
}

function startAll() {
  fetch('/api/bot/start-all', { method: 'POST' });
}

function stopAll() {
  if (confirm('Are you sure you want to stop all running bots?')) {
    fetch('/api/bot/stop-all', { method: 'POST' });
  }
}

// Telemetry Chart Setup (Tremor Minimal Style - 1m Intervals)
const MAX_CHART_MINUTES = 15;
let lastRecordedMinute = '';

function initChart() {
  const ctx = document.getElementById('telemetry-chart').getContext('2d');

  // Pre-populate timestamps with fixed 15-minute window to eliminate on-load resizing
  const now = new Date();
  telemetryHistory.timestamps = [];
  for (let i = MAX_CHART_MINUTES - 1; i >= 0; i--) {
    const d = new Date(now.getTime() - i * 60000);
    telemetryHistory.timestamps.push(d.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' }));
  }
  lastRecordedMinute = telemetryHistory.timestamps[telemetryHistory.timestamps.length - 1];

  perfChart = new Chart(ctx, {
    type: 'line',
    data: {
      labels: telemetryHistory.timestamps,
      datasets: []
    },
    options: {
      responsive: true,
      maintainAspectRatio: false,
      animation: false, // Disables jumpy animation reflow
      plugins: {
        legend: {
          position: 'top',
          align: 'end',
          labels: {
            color: '#94a3b8',
            font: { family: 'Inter', size: 11, weight: '600' },
            boxWidth: 12,
            boxHeight: 12
          }
        },
        tooltip: {
          backgroundColor: '#0f172a',
          borderColor: '#334155',
          borderWidth: 1,
          titleColor: '#f8fafc',
          bodyColor: '#94a3b8'
        }
      },
      scales: {
        x: {
          grid: { color: 'rgba(51, 65, 85, 0.2)' },
          ticks: {
            color: '#64748b',
            font: { family: 'Inter', size: 10 },
            maxRotation: 0,
            autoSkip: true,
            maxTicksLimit: 8
          }
        },
        y: {
          beginAtZero: true,
          grid: { color: 'rgba(51, 65, 85, 0.2)' },
          afterFit: (axis) => {
            // Hardcode 68px width so number scale changes NEVER shift the chart layout
            axis.width = 68;
          },
          ticks: {
            color: '#64748b',
            font: { family: 'Inter', size: 10 },
            maxTicksLimit: 5,
            callback: (val) => formatExpRate(val)
          }
        }
      }
    }
  });
}

function updateChartData(fleet) {
  const profiles = fleet?.profiles ?? fleet?.Profiles;
  if (!perfChart || !profiles) return;

  const now = new Date();
  const currentMinuteStr = now.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
  const isNewMinute = currentMinuteStr !== lastRecordedMinute;

  if (isNewMinute) {
    lastRecordedMinute = currentMinuteStr;
    if (telemetryHistory.timestamps.length >= MAX_CHART_MINUTES) {
      telemetryHistory.timestamps.shift();
    }
    telemetryHistory.timestamps.push(currentMinuteStr);
  }

  const colors = ['#3b82f6', '#10b981', '#f59e0b', '#8b5cf6', '#ec4899', '#06b6d4'];
  let colorIdx = 0;

  const runningProfiles = profiles.filter(p => (p.isRunning ?? p.IsRunning));
  const datasets = runningProfiles.map((p) => {
    const name = p.profileName ?? p.ProfileName ?? 'Bot';
    const status = p.status ?? p.Status;
    const rate = Math.round(status?.baseExpPerHour ?? status?.BaseExpPerHour ?? 0);

    // Initialize historical series array with zeros if new
    if (!telemetryHistory.series[name]) {
      telemetryHistory.series[name] = new Array(MAX_CHART_MINUTES).fill(0);
    }

    const arr = telemetryHistory.series[name];
    if (isNewMinute) {
      if (arr.length >= MAX_CHART_MINUTES) arr.shift();
      arr.push(rate);
    } else {
      // Update latest minute's rate in place
      arr[arr.length - 1] = rate;
    }

    const color = colors[colorIdx++ % colors.length];

    return {
      label: `${name} (${formatExpRate(rate)})`,
      data: [...arr],
      borderColor: color,
      backgroundColor: color + '15',
      borderWidth: 2,
      pointRadius: 2,
      tension: 0.2,
      fill: false
    };
  });

  perfChart.data.labels = telemetryHistory.timestamps;
  perfChart.data.datasets = datasets;
  perfChart.update('none'); // Update without canvas reflow animation
}

// Macro Modal Logic
function openMacroModal(profileName) {
  document.getElementById('macro-profile-target').value = profileName;
  document.getElementById('macro-modal-title').textContent = `Dispatch Macro Action - ${profileName}`;
  onMacroTypeChanged();
  document.getElementById('macro-modal').classList.add('open');
}

function closeMacroModal() {
  document.getElementById('macro-modal').classList.remove('open');
}

function onMacroTypeChanged() {
  const type = document.getElementById('macro-type-select').value;
  const upgradeFields = document.getElementById('macro-upgrade-fields');
  const travelFields = document.getElementById('macro-travel-fields');
  const buyFields = document.getElementById('macro-buy-fields');

  upgradeFields.style.display = type === 'UpgradeItem' ? 'flex' : 'none';
  travelFields.style.display = type === 'TravelToMap' ? 'flex' : 'none';
  buyFields.style.display = type === 'BuyItem' ? 'flex' : 'none';
}

function submitMacro() {
  const profile = document.getElementById('macro-profile-target').value;
  const type = document.getElementById('macro-type-select').value;

  const payload = {
    actionType: type,
    itemName: document.getElementById('macro-item-name').value || undefined,
    targetRefineLevel: parseInt(document.getElementById('macro-target-refine').value, 10) || 4,
    stopAtSafeLimit: document.getElementById('macro-safe-limit').checked,
    targetMap: document.getElementById('macro-target-map').value || undefined,
    quantity: parseInt(document.getElementById('macro-quantity').value, 10) || 1,
    vendorName: document.getElementById('macro-vendor-name').value || undefined
  };

  fetch(`/api/bot/${encodeURIComponent(profile)}/macro`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(payload)
  })
    .then(r => r.json())
    .then(data => {
      if (data.success) {
        closeMacroModal();
      } else {
        alert(data.error || 'Failed to dispatch macro');
      }
    });
}

// Config Modal Logic
function openConfigModal(profileName) {
  document.getElementById('config-profile-target').value = profileName;
  document.getElementById('config-modal-title').textContent = `Configuration - ${profileName}`;

  fetch(`/api/bot/${encodeURIComponent(profileName)}/config`)
    .then(r => r.text())
    .then(rawText => {
      try {
        const parsed = JSON.parse(rawText);
        document.getElementById('config-json-editor').value = JSON.stringify(parsed, null, 2);
      } catch {
        document.getElementById('config-json-editor').value = rawText || '{}';
      }
      document.getElementById('config-modal').classList.add('open');
    })
    .catch(err => {
      document.getElementById('config-json-editor').value = '{}';
      document.getElementById('config-modal').classList.add('open');
    });
}

function closeConfigModal() {
  document.getElementById('config-modal').classList.remove('open');
}

function saveConfig() {
  const profile = document.getElementById('config-profile-target').value;
  const raw = document.getElementById('config-json-editor').value;

  fetch(`/api/bot/${encodeURIComponent(profile)}/config`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: raw
  })
    .then(r => r.json())
    .then(data => {
      if (data.success) {
        closeConfigModal();
      } else {
        alert(data.error || 'Failed to save config');
      }
    })
    .catch(err => alert('Invalid JSON syntax: ' + err.message));
}

// --------------------------------------------------------------------------
// Live Terminal & Bot Log Stream Logic
// --------------------------------------------------------------------------

function updateLogTabs(profiles) {
  const tabsContainer = document.getElementById('terminal-tabs');
  if (!tabsContainer || !profiles) return;

  profiles.forEach(p => {
    const name = p.profileName ?? p.ProfileName;
    if (name && !knownProfiles.has(name)) {
      knownProfiles.add(name);
      if (!botLogs[name]) botLogs[name] = [];

      const btn = document.createElement('button');
      btn.className = `terminal-tab ${activeLogTab === name ? 'active' : ''}`;
      btn.setAttribute('data-tab', name);
      btn.textContent = name;
      btn.onclick = () => switchLogTab(name);
      tabsContainer.appendChild(btn);
    }
  });
}

function switchLogTab(tabId) {
  activeLogTab = tabId;

  // Update tab buttons
  document.querySelectorAll('.terminal-tab').forEach(t => {
    if (t.getAttribute('data-tab') === tabId) {
      t.classList.add('active');
    } else {
      t.classList.remove('active');
    }
  });

  // If tab buffer is empty, load initial logs from server
  if (!botLogs[tabId] || botLogs[tabId].length === 0) {
    loadInitialLogs(tabId);
  } else {
    renderActiveLogs();
  }
}

function loadInitialLogs(tabId) {
  fetch(`/api/bot/${encodeURIComponent(tabId)}/logs?lines=150`)
    .then(r => r.json())
    .then(lines => {
      if (!botLogs[tabId]) botLogs[tabId] = [];
      botLogs[tabId] = lines.map(l => parseLogItem(tabId === 'all' ? extractProfileFromLine(l) : tabId, l));
      renderActiveLogs();
    })
    .catch(() => renderActiveLogs());
}

function extractProfileFromLine(raw) {
  const match = raw.match(/^\[([a-zA-Z0-9_-]+)\]/);
  return match ? match[1] : '';
}

function appendBotLog(profile, rawLine, timestamp) {
  const item = parseLogItem(profile, rawLine, timestamp);

  // Store in per-profile and 'all' buffers
  if (!botLogs[profile]) botLogs[profile] = [];
  botLogs[profile].push(item);
  if (botLogs[profile].length > 1000) botLogs[profile].shift();

  if (!botLogs['all']) botLogs['all'] = [];
  botLogs['all'].push(item);
  if (botLogs['all'].length > 1000) botLogs['all'].shift();

  // If item belongs to current active view, append or re-render
  if (activeLogTab === 'all' || activeLogTab === profile) {
    if (matchesActiveFilter(item)) {
      const container = document.getElementById('terminal-lines');
      const emptyMsg = container.querySelector('.terminal-empty-msg');
      if (emptyMsg) emptyMsg.remove();

      const el = document.createElement('div');
      el.className = 'terminal-line';
      el.innerHTML = createTerminalLineHtml(item);
      container.appendChild(el);

      // Enforce DOM limit
      while (container.children.length > 500) {
        container.removeChild(container.firstChild);
      }

      // Auto-scroll
      if (document.getElementById('log-autoscroll').checked) {
        const body = document.getElementById('terminal-body');
        body.scrollTop = body.scrollHeight;
      }
    }
  }
}

function parseLogItem(profile, rawLine, timestamp) {
  let cleanLine = rawLine || '';

  // Strip leading [Profile] prefix if present in aggregated logs
  cleanLine = cleanLine.replace(/^\[([a-zA-Z0-9_-]+)\]\s*/, (match, p) => {
    if (!profile) profile = p;
    return '';
  });

  // Extract timestamp like [2026-08-31 19:50:00] or [19:50:00]
  let timeStr = new Date(timestamp || Date.now()).toLocaleTimeString();
  cleanLine = cleanLine.replace(/^\[(\d{4}-\d{2}-\d{2}\s+)?(\d{2}:\d{2}:\d{2})\]\s*/, (match, d, t) => {
    if (t) timeStr = t;
    return '';
  });

  // Extract category tag like [Combat], [Loot], [Macro], [Travel], [Town Routine], [Progression], [Death], etc.
  let tag = '';
  let tagClass = 'tag-state';
  cleanLine = cleanLine.replace(/^\[([a-zA-Z\s_-]+)\]\s*/, (match, t) => {
    tag = t.trim();
    const low = tag.toLowerCase();
    if (low.includes('combat') || low.includes('attack')) tagClass = 'tag-combat';
    else if (low.includes('loot')) tagClass = 'tag-loot';
    else if (low.includes('macro') || low.includes('upgrade') || low.includes('refine')) tagClass = 'tag-macro';
    else if (low.includes('travel') || low.includes('move') || low.includes('warp')) tagClass = 'tag-travel';
    else if (low.includes('town') || low.includes('vendor') || low.includes('kafra')) tagClass = 'tag-town';
    else if (low.includes('progression') || low.includes('stat') || low.includes('level')) tagClass = 'tag-progression';
    else if (low.includes('death') || low.includes('respawn') || low.includes('error')) tagClass = 'tag-death';
    return '';
  });

  return {
    profile: profile || '',
    time: timeStr,
    tag: tag,
    tagClass: tagClass,
    text: cleanLine,
    raw: rawLine
  };
}

function matchesActiveFilter(item) {
  const search = (document.getElementById('log-search-input')?.value || '').toLowerCase().trim();
  const category = (document.getElementById('log-category-select')?.value || 'all');

  if (category !== 'all') {
    const itemTag = (item.tag || '').toLowerCase();
    const catLow = category.toLowerCase();
    if (!itemTag.includes(catLow) && !item.text.toLowerCase().includes(catLow)) {
      return false;
    }
  }

  if (search.length > 0) {
    const fullText = `${item.profile} ${item.tag} ${item.text}`.toLowerCase();
    if (!fullText.includes(search)) {
      return false;
    }
  }

  return true;
}

function onLogFilterChanged() {
  renderActiveLogs();
}

function renderActiveLogs() {
  const container = document.getElementById('terminal-lines');
  if (!container) return;

  const logs = botLogs[activeLogTab] || [];
  const filtered = logs.filter(matchesActiveFilter);

  if (filtered.length === 0) {
    container.innerHTML = `<div class="terminal-empty-msg">No log entries matching filter for [${activeLogTab === 'all' ? 'All Bots' : activeLogTab}].</div>`;
    return;
  }

  container.innerHTML = filtered.map(item => `
    <div class="terminal-line">
      ${createTerminalLineHtml(item)}
    </div>
  `).join('');

  if (document.getElementById('log-autoscroll').checked) {
    const body = document.getElementById('terminal-body');
    body.scrollTop = body.scrollHeight;
  }
}

function createTerminalLineHtml(item) {
  return `
    <span class="terminal-time">${item.time}</span>
    ${activeLogTab === 'all' && item.profile ? `<span class="terminal-bot-badge">[${escapeHtml(item.profile)}]</span>` : ''}
    ${item.tag ? `<span class="terminal-tag ${item.tagClass}">[${escapeHtml(item.tag)}]</span>` : ''}
    <span class="terminal-msg">${escapeHtml(item.text)}</span>
  `;
}

function clearActiveLogs() {
  botLogs[activeLogTab] = [];
  renderActiveLogs();
}

// Event Listeners Setup
function setupEventListeners() {
  document.getElementById('macro-type-select').addEventListener('change', onMacroTypeChanged);
}

// Utility Helpers
function formatNumber(num) {
  if (num === null || num === undefined) return '0';
  return num.toLocaleString();
}

function formatExpRate(val) {
  if (!val || val <= 0) return '0/hr';
  if (val >= 1000000) return (val / 1000000).toFixed(2) + 'M/hr';
  if (val >= 1000) return (val / 1000).toFixed(1) + 'k/hr';
  return Math.round(val) + '/hr';
}

function escapeHtml(str) {
  if (!str) return '';
  return str.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
}
