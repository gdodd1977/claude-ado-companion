// ===== State =====
let bugs = [];
let currentFilter = 'all';
let currentSort = { key: 'roi', dir: 'desc' };
let currentUserName = '';
let showOnlyMine = false;

// Live streaming state
let liveEventSource = null;
let liveSessionId = null;
let isLive = false;
let liveMessageCount = 0;

// Triage modal state
let triageEventSource = null;
let triageMessageCount = 0;
let triagePollTimer = null;

// Claude auth state
let claudeAuthenticated = false;
let authPollTimer = null;
let pendingTriageAction = null;

// Config state
let appConfig = {};

// ===== Init =====
document.addEventListener('DOMContentLoaded', async () => {
  try {
    appConfig = await apiFetch('/api/config');
  } catch { /* ignore */ }

  if (!appConfig.isConfigured && !appConfig.demo) {
    showSetupBanner(true);
    return;
  }

  try {
    const me = await apiFetch('/api/me');
    currentUserName = me.displayName || '';
  } catch { /* ignore */ }
  loadBugs();
});

// ===== API Helpers =====
async function apiFetch(url, options = {}) {
  const res = await fetch(url, {
    ...options,
    headers: { 'Content-Type': 'application/json', ...options.headers },
  });

  if (!res.ok) {
    const text = await res.text();
    throw new Error(`API error ${res.status}: ${text}`);
  }

  return res.json();
}

// ===== Bugs Tab =====
async function loadBugs() {
  const tbody = document.getElementById('bugsTable');
  tbody.innerHTML = '<tr><td colspan="6" class="loading"><div class="spinner"></div> Loading bugs...</td></tr>';

  try {
    bugs = await apiFetch('/api/bugs');
    renderBugs();
    showToast(`Loaded ${bugs.length} bugs`, 'success');
  } catch (err) {
    tbody.innerHTML = `<tr><td colspan="6" class="loading" style="color:var(--score-low)">${escapeHtml(err.message)}</td></tr>`;
    showToast('Failed to load bugs', 'error');
  }
}

function renderBugs() {
  const filtered = filterBugs(bugs);
  const sorted = sortBugs(filtered);
  const tbody = document.getElementById('bugsTable');

  document.getElementById('bugCount').textContent =
    filtered.length === bugs.length ? `${bugs.length} bugs` : `${filtered.length} of ${bugs.length} bugs`;

  if (sorted.length === 0) {
    tbody.innerHTML = '<tr><td colspan="6" class="loading">No bugs match the current filter</td></tr>';
    return;
  }

  tbody.innerHTML = sorted.map(bug => {
    const ts = bug.triageStatus;
    const sevNum = parseSeverity(bug.severity);
    const readiness = ts?.copilotReadiness || '';
    const showAssign = readiness === 'Ready' || readiness === 'Possible';
    const needsInfo = ts?.needsInfo ? '<span class="needs-info-badge">Needs Info</span>' : '';

    return `<tr>
      <td><a class="bug-id-link" href="${escapeHtml(bug.adoUrl)}" target="_blank">${bug.id}</a></td>
      <td><span class="bug-title" title="${escapeHtml(bug.title)}">${escapeHtml(bug.title)}</span>${needsInfo}</td>
      <td><span class="severity-badge severity-${sevNum}">${sevNum}</span></td>
      <td>${roiBadge(ts?.highRoi)}</td>
      <td>${copilotBadge(readiness)}</td>
      <td>
        <div class="actions-cell">
          ${showAssign ? `<button class="btn btn-sm btn-icon" title="Assign to Copilot" onclick="assignCopilot(${bug.id})">&#129302;</button>` : ''}
          <button class="btn btn-sm btn-icon" title="Re-triage" onclick="retriage(${bug.id})">&#8635;</button>
        </div>
      </td>
    </tr>`;
  }).join('');

  document.querySelectorAll('thead th').forEach(th => {
    th.classList.toggle('sorted', th.dataset.sort === currentSort.key);
    const ind = th.querySelector('.sort-indicator');
    if (ind) {
      ind.textContent = th.dataset.sort === currentSort.key
        ? (currentSort.dir === 'asc' ? '\u25B2' : '\u25BC')
        : '\u25B2';
    }
  });
}

function roiBadge(highRoi) {
  if (highRoi) return '<span class="score-badge high">High</span>';
  return '<span class="score-label">\u2014</span>';
}

function copilotBadge(readiness) {
  if (!readiness) return '<span class="score-label">\u2014</span>';
  switch (readiness) {
    case 'Ready': return '<span class="score-badge high">Ready</span>';
    case 'Possible': return '<span class="score-badge mid">Possible</span>';
    case 'Human Required': return '<span class="score-badge low">Human Required</span>';
    default: return '<span class="score-label">\u2014</span>';
  }
}

function parseSeverity(sev) {
  if (!sev) return 4;
  const m = sev.match(/^(\d)/);
  return m ? parseInt(m[1]) : 4;
}

// ===== Sorting =====
function sortBy(key) {
  if (currentSort.key === key) {
    currentSort.dir = currentSort.dir === 'asc' ? 'desc' : 'asc';
  } else {
    currentSort.key = key;
    currentSort.dir = 'desc';
  }
  renderBugs();
}

const copilotReadinessOrder = { 'Ready': 3, 'Possible': 2, 'Human Required': 1 };

function sortBugs(list) {
  const { key, dir } = currentSort;
  const mult = dir === 'asc' ? 1 : -1;

  return [...list].sort((a, b) => {
    let va, vb;
    switch (key) {
      case 'id': va = a.id; vb = b.id; break;
      case 'title': va = a.title; vb = b.title; break;
      case 'severity': va = parseSeverity(a.severity); vb = parseSeverity(b.severity); break;
      case 'roi': va = a.triageStatus?.highRoi ? 1 : 0; vb = b.triageStatus?.highRoi ? 1 : 0; break;
      case 'copilotReadiness':
        va = copilotReadinessOrder[a.triageStatus?.copilotReadiness] || 0;
        vb = copilotReadinessOrder[b.triageStatus?.copilotReadiness] || 0;
        break;
      default: va = 0; vb = 0;
    }

    if (typeof va === 'string') return mult * va.localeCompare(vb);
    return mult * (va - vb);
  });
}

// ===== Filtering =====
function setFilter(filter) {
  currentFilter = filter;
  document.querySelectorAll('.chip').forEach(c => {
    c.classList.toggle('active', c.dataset.filter === filter);
  });
  renderBugs();
}

function toggleMyBugs() {
  showOnlyMine = !showOnlyMine;
  document.getElementById('myBugsToggle').classList.toggle('active', showOnlyMine);
  renderBugs();
}

function filterBugs(list) {
  let filtered = list;

  if (showOnlyMine && currentUserName) {
    filtered = filtered.filter(bug =>
      bug.assignedTo.toLowerCase().includes(currentUserName.toLowerCase()));
  }

  if (currentFilter === 'all') return filtered;

  return filtered.filter(bug => {
    const ts = bug.triageStatus;
    switch (currentFilter) {
      case 'triaged': return ts?.isTriaged === true;
      case 'untriaged': return !ts?.isTriaged;
      case 'copilot-ready': return ts?.copilotReadiness === 'Ready';
      case 'copilot-possible': return ts?.copilotReadiness === 'Possible';
      case 'human-required': return ts?.copilotReadiness === 'Human Required';
      default: return true;
    }
  });
}

// ===== Actions =====
async function assignCopilot(id) {
  if (!confirm(`Assign bug #${id} to GitHub Copilot coding agent?`)) return;

  try {
    showToast(`Assigning bug #${id} to Copilot...`, 'info');
    await apiFetch(`/api/bugs/${id}/assign-copilot`, { method: 'POST' });
    showToast(`Bug #${id} assigned to Copilot`, 'success');
    await loadBugs();
  } catch (err) {
    showToast(`Failed to assign: ${err.message}`, 'error');
  }
}

async function retriage(id) {
  if (!confirm(`Re-triage bug #${id}? This will run Claude Code.`)) return;

  const authed = await ensureClaudeAuth();
  if (!authed) return;

  try {
    const res = await apiFetch(`/api/bugs/${id}/retriage`, { method: 'POST' });
    openTriageModal(res.demo);
  } catch (err) {
    showToast(`Failed to start re-triage: ${err.message}`, 'error');
  }
}

async function batchTriage() {
  const max = prompt('How many bugs to analyze with Claude?', '10');
  if (!max) return;

  const authed = await ensureClaudeAuth();
  if (!authed) return;

  try {
    const res = await apiFetch('/api/triage/batch', {
      method: 'POST',
      body: JSON.stringify({ max: parseInt(max) }),
    });
    openTriageModal(res.demo);
  } catch (err) {
    showToast(`Failed to start analysis: ${err.message}`, 'error');
  }
}

// ===== Claude Auth =====
async function ensureClaudeAuth() {
  if (claudeAuthenticated) return true;

  const overlay = document.getElementById('triageModal');
  const status = document.getElementById('triageModalStatus');
  const body = document.getElementById('triageModalBody');

  overlay.classList.remove('hidden');
  overlay.classList.remove('minimized');
  status.textContent = 'Checking authentication...';
  body.innerHTML = '<div class="loading"><div class="spinner"></div> Checking Claude CLI authentication...</div>';

  try {
    const res = await apiFetch('/api/claude/status');

    if (!res.installed) {
      status.textContent = 'Not Installed';
      body.innerHTML = '<div class="loading">Claude CLI is not installed. Please install Claude Code and try again.</div>';
      return false;
    }

    if (res.authenticated) {
      claudeAuthenticated = true;
      overlay.classList.add('hidden');
      return true;
    }

    // Not authenticated — launch terminal and poll
    return await promptClaudeAuth(status, body);
  } catch (err) {
    status.textContent = 'Error';
    body.innerHTML = `<div class="loading" style="color:var(--score-low)">Failed to check Claude status: ${escapeHtml(err.message)}</div>`;
    return false;
  }
}

async function promptClaudeAuth(status, body) {
  status.textContent = 'Authentication Required';
  body.innerHTML = `
    <div style="text-align:center; padding: 40px 20px;">
      <div style="font-size: 40px; margin-bottom: 16px;">&#128274;</div>
      <h3 style="margin-bottom: 8px;">Claude Authentication Required</h3>
      <p style="color: var(--text-secondary); margin-bottom: 20px;">
        A terminal window will open for you to log in to Claude.<br>
        Complete the login there, then this window will continue automatically.
      </p>
      <div class="loading"><div class="spinner"></div> Opening terminal &amp; waiting for authentication...</div>
    </div>`;

  try {
    await apiFetch('/api/claude/launch-auth', { method: 'POST' });
  } catch {
    body.innerHTML = '<div class="loading" style="color:var(--score-low)">Failed to open authentication terminal. Please run "claude" in a terminal manually.</div>';
    return false;
  }

  // Poll for auth success
  return new Promise((resolve) => {
    let pollCount = 0;
    authPollTimer = setInterval(async () => {
      pollCount++;
      try {
        const res = await apiFetch('/api/claude/status');
        if (res.authenticated) {
          clearInterval(authPollTimer);
          authPollTimer = null;
          claudeAuthenticated = true;
          document.getElementById('triageModal').classList.add('hidden');
          showToast('Claude authenticated successfully', 'success');
          resolve(true);
        } else if (pollCount >= 60) {
          // 5 minute timeout (60 * 5s)
          clearInterval(authPollTimer);
          authPollTimer = null;
          status.textContent = 'Timed Out';
          body.innerHTML = '<div class="loading">Authentication timed out after 5 minutes. Close this modal and try again.</div>';
          resolve(false);
        }
      } catch {
        // keep polling
      }
    }, 5000);
  });
}

// ===== Triage Panel =====
function minimizeTriagePanel() {
  const panel = document.getElementById('triageModal');
  panel.classList.toggle('minimized');
  const btn = panel.querySelector('[title="Minimize"]');
  if (btn) btn.innerHTML = panel.classList.contains('minimized') ? '&#9633;' : '&#8211;';
}

function openTriageModal(isDemo) {
  const overlay = document.getElementById('triageModal');
  const status = document.getElementById('triageModalStatus');
  const body = document.getElementById('triageModalBody');

  overlay.classList.remove('hidden');
  overlay.classList.remove('minimized');
  triageMessageCount = 0;

  if (isDemo) {
    status.textContent = 'Demo Mode';
    body.innerHTML = '<div class="loading">Demo mode — no Claude process runs. In production, Claude session logs will stream here in real time.</div>';
    return;
  }

  status.textContent = 'Starting...';
  body.innerHTML = '<div class="loading"><div class="spinner"></div> Waiting for Claude session to start...</div>';

  let pollCount = 0;
  triagePollTimer = setInterval(async () => {
    pollCount++;
    try {
      const res = await fetch('/api/sessions/active').then(r => r.json());
      if (res.active) {
        clearInterval(triagePollTimer);
        triagePollTimer = null;
        connectTriageStream(res.id);
      } else if (pollCount >= 20) {
        clearInterval(triagePollTimer);
        triagePollTimer = null;
        status.textContent = 'Timed out';
        body.innerHTML = '<div class="loading">No active Claude session detected after 30 seconds. The process may have failed to start.</div>';
      }
    } catch {
      // keep polling
    }
  }, 1500);
}

function connectTriageStream(sessionId) {
  const status = document.getElementById('triageModalStatus');
  const body = document.getElementById('triageModalBody');

  status.textContent = 'Connecting...';
  body.innerHTML = '';

  triageEventSource = new EventSource(`/api/sessions/${encodeURIComponent(sessionId)}/stream`);

  triageEventSource.onmessage = (event) => {
    try {
      const msg = JSON.parse(event.data);
      triageMessageCount++;
      status.textContent = `Streaming: ${triageMessageCount} messages`;

      body.insertAdjacentHTML('beforeend', renderMessage(msg, triageMessageCount));
      body.scrollTop = body.scrollHeight;
    } catch (e) {
      console.error('Failed to parse triage SSE message:', e);
    }
  };

  triageEventSource.onerror = () => {
    if (triageEventSource && triageEventSource.readyState === EventSource.CLOSED) {
      status.textContent = `Complete — ${triageMessageCount} messages`;
      showToast('Triage complete! Bug list updated.', 'success');
      triageEventSource = null;
      loadBugs();
    } else {
      status.textContent = `Streaming: ${triageMessageCount} messages (reconnecting...)`;
    }
  };
}

function closeTriageModal() {
  if (triageEventSource) {
    triageEventSource.close();
    triageEventSource = null;
  }

  if (triagePollTimer) {
    clearInterval(triagePollTimer);
    triagePollTimer = null;
  }

  if (authPollTimer) {
    clearInterval(authPollTimer);
    authPollTimer = null;
  }

  const panel = document.getElementById('triageModal');
  panel.classList.add('hidden');
  panel.classList.remove('minimized');
  triageMessageCount = 0;

  loadBugs();
}

// ===== Tabs =====
function switchTab(tab) {
  document.querySelectorAll('.tab-btn').forEach(b => {
    b.classList.toggle('active', b.dataset.tab === tab);
  });
  document.querySelectorAll('.tab-panel').forEach(p => {
    p.classList.toggle('active', p.id === `tab-${tab}`);
  });

  if (tab === 'sessions') {
    loadSessions();
  } else {
    if (isLive) disconnectLive();
  }
}

// ===== Sessions Tab =====
async function loadSessions() {
  const list = document.getElementById('sessionList');
  list.innerHTML = '<div class="loading"><div class="spinner"></div> Loading sessions...</div>';

  try {
    const sessions = await fetch('/api/sessions').then(r => r.json());

    if (sessions.length === 0) {
      list.innerHTML = '<div class="loading">No sessions found</div>';
      return;
    }

    list.innerHTML = sessions.map(s => {
      const isActive = s.id === liveSessionId;
      return `
      <div class="session-item ${isActive ? 'active' : ''}" data-id="${escapeHtml(s.id)}" onclick="loadSession('${escapeHtml(s.id)}')">
        <div class="session-time">${formatDate(s.timestamp)}</div>
        <div class="session-preview">${escapeHtml(s.preview || 'Session ' + s.id.slice(0, 8))}</div>
      </div>`;
    }).join('');
  } catch (err) {
    list.innerHTML = `<div class="loading" style="color:var(--score-low)">${escapeHtml(err.message)}</div>`;
  }
}

async function loadSession(id) {
  if (isLive) disconnectLive();

  document.querySelectorAll('.session-item').forEach(el => {
    el.classList.toggle('active', el.dataset.id === id);
  });

  const detail = document.getElementById('sessionDetail');
  detail.innerHTML = '<div class="loading"><div class="spinner"></div> Loading session...</div>';

  try {
    const messages = await fetch(`/api/sessions/${encodeURIComponent(id)}`).then(r => r.json());

    if (messages.length === 0) {
      detail.innerHTML = '<div class="session-detail-empty">No messages in this session</div>';
      return;
    }

    detail.innerHTML = messages.map((msg, i) => renderMessage(msg, i)).join('');
    detail.scrollTop = detail.scrollHeight;
  } catch (err) {
    detail.innerHTML = `<div class="loading" style="color:var(--score-low)">${escapeHtml(err.message)}</div>`;
  }
}

// ===== Live Streaming =====
async function toggleLive() {
  if (isLive) {
    disconnectLive();
    return;
  }

  try {
    const res = await fetch('/api/sessions/active').then(r => r.json());
    if (!res.active) {
      showToast('No active session found (must be modified within last 5 min)', 'info');
      return;
    }

    connectLive(res.id);
  } catch (err) {
    showToast(`Failed to find active session: ${err.message}`, 'error');
  }
}

function connectLive(sessionId) {
  liveSessionId = sessionId;
  isLive = true;
  liveMessageCount = 0;

  document.getElementById('liveBtn').classList.add('btn-live-active');
  document.getElementById('liveDot').classList.add('pulsing');

  const status = document.getElementById('liveStatus');
  status.textContent = 'Connecting...';

  const detail = document.getElementById('sessionDetail');
  detail.innerHTML = '';

  document.querySelectorAll('.session-item').forEach(el => {
    el.classList.toggle('active', el.dataset.id === sessionId);
  });

  liveEventSource = new EventSource(`/api/sessions/${encodeURIComponent(sessionId)}/stream`);

  liveEventSource.onmessage = (event) => {
    try {
      const msg = JSON.parse(event.data);
      liveMessageCount++;
      status.textContent = `Streaming: ${liveMessageCount} messages`;

      detail.insertAdjacentHTML('beforeend', renderMessage(msg, liveMessageCount));
      detail.scrollTop = detail.scrollHeight;
    } catch (e) {
      console.error('Failed to parse SSE message:', e);
    }
  };

  liveEventSource.onerror = () => {
    status.textContent = `Streaming: ${liveMessageCount} messages (reconnecting...)`;
  };

  showToast(`Live streaming session ${sessionId.slice(0, 8)}...`, 'info');
}

function disconnectLive() {
  if (liveEventSource) {
    liveEventSource.close();
    liveEventSource = null;
  }

  isLive = false;
  liveSessionId = null;

  document.getElementById('liveBtn').classList.remove('btn-live-active');
  document.getElementById('liveDot').classList.remove('pulsing');
  document.getElementById('liveStatus').textContent = '';

  showToast('Disconnected from live stream', 'info');
}

// ===== Message Rendering =====
function renderMessage(msg, idx) {
  const collapsed = msg.type === 'thinking' || msg.type === 'tool_result';
  const collapseClass = collapsed ? 'collapsed' : '';
  const toggleText = collapsed ? '\u25B6 expand' : '\u25BC collapse';

  let headerLabel = '';
  let bodyContent = '';

  switch (msg.type) {
    case 'user':
      headerLabel = 'User';
      bodyContent = escapeHtml(msg.text);
      break;
    case 'thinking':
      headerLabel = 'Thinking';
      bodyContent = escapeHtml(msg.text);
      break;
    case 'tool_call':
      headerLabel = `Tool: ${escapeHtml(msg.toolName || 'unknown')}`;
      bodyContent = escapeHtml(msg.toolInput || '');
      break;
    case 'tool_result':
      headerLabel = 'Result';
      bodyContent = escapeHtml(msg.text);
      break;
    case 'text':
      headerLabel = 'Output';
      bodyContent = escapeHtml(msg.text);
      break;
    default:
      headerLabel = msg.type;
      bodyContent = escapeHtml(msg.text);
  }

  const timestamp = msg.timestamp ? `<span class="msg-time">${formatTime(msg.timestamp)}</span>` : '';

  return `
    <div class="msg ${escapeHtml(msg.type)} msg-enter">
      <div class="msg-header" onclick="toggleMsg(this)">
        <span class="msg-type">${headerLabel}</span>
        ${timestamp}
        <span class="msg-toggle">${toggleText}</span>
      </div>
      <div class="msg-body ${collapseClass}">${bodyContent}</div>
    </div>`;
}

function toggleMsg(header) {
  const body = header.nextElementSibling;
  const toggle = header.querySelector('.msg-toggle');
  body.classList.toggle('collapsed');
  toggle.textContent = body.classList.contains('collapsed') ? '\u25B6 expand' : '\u25BC collapse';
}

// ===== Utilities =====
function escapeHtml(str) {
  if (!str) return '';
  const div = document.createElement('div');
  div.textContent = str;
  return div.innerHTML;
}

function formatDate(iso) {
  if (!iso) return '';
  const d = new Date(iso);
  return d.toLocaleString('en-US', {
    year: 'numeric', month: 'short', day: 'numeric',
    hour: '2-digit', minute: '2-digit',
  });
}

function formatTime(iso) {
  if (!iso) return '';
  const d = new Date(iso);
  return d.toLocaleTimeString('en-US', {
    hour: '2-digit', minute: '2-digit', second: '2-digit',
  });
}

let toastTimeout;
function showToast(message, type = 'info') {
  const toast = document.getElementById('toast');
  toast.textContent = message;
  toast.className = `toast ${type} show`;
  clearTimeout(toastTimeout);
  toastTimeout = setTimeout(() => { toast.classList.remove('show'); }, 4000);
}

// ===== Settings =====
function showSetupBanner(isFirstRun) {
  const banner = document.getElementById('setupBanner');
  banner.classList.remove('hidden');

  // Pre-fill from current config
  document.getElementById('cfg-adoOrg').value = appConfig.adoOrg || '';
  document.getElementById('cfg-adoProject').value = appConfig.adoProject || '';
  document.getElementById('cfg-areaPath').value = appConfig.areaPath || '';
  document.getElementById('cfg-iterationPath').value = appConfig.iterationPath || '';
  document.getElementById('cfg-copilotUserId').value = appConfig.copilotUserId || '';
  document.getElementById('cfg-repoProjectGuid').value = appConfig.repoProjectGuid || '';
  document.getElementById('cfg-repoGuid').value = appConfig.repoGuid || '';
  document.getElementById('cfg-branchRef').value = appConfig.branchRef || 'GBmain';
  document.getElementById('cfg-maxBugs').value = appConfig.maxBugsDefault || 100;

  // Hide cancel on first run
  const cancelBtn = banner.querySelector('.setup-actions .btn:not(.btn-primary)');
  if (cancelBtn) cancelBtn.style.display = isFirstRun ? 'none' : '';
}

function openSettings() {
  showSetupBanner(false);
}

function closeSettings() {
  document.getElementById('setupBanner').classList.add('hidden');
}

async function saveSettings(e) {
  e.preventDefault();

  const settings = {
    AdoOrg: document.getElementById('cfg-adoOrg').value.trim(),
    AdoProject: document.getElementById('cfg-adoProject').value.trim(),
    AreaPath: document.getElementById('cfg-areaPath').value.trim(),
    IterationPath: document.getElementById('cfg-iterationPath').value.trim() || null,
    CopilotUserId: document.getElementById('cfg-copilotUserId').value.trim(),
    RepoProjectGuid: document.getElementById('cfg-repoProjectGuid').value.trim(),
    RepoGuid: document.getElementById('cfg-repoGuid').value.trim(),
    BranchRef: document.getElementById('cfg-branchRef').value.trim() || 'GBmain',
    MaxBugsDefault: parseInt(document.getElementById('cfg-maxBugs').value) || 100,
  };

  try {
    await apiFetch('/api/config', {
      method: 'POST',
      body: JSON.stringify(settings),
    });
    showToast('Settings saved! Restarting...', 'success');
    setTimeout(() => location.reload(), 1500);
  } catch (err) {
    showToast(`Failed to save settings: ${err.message}`, 'error');
  }
}
