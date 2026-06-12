const App = (() => {
  let config = JSON.parse(localStorage.getItem('opt-config') || 'null');
  let currentTab = 'dashboard';
  let refreshInterval = null;
  let deferredInstallPrompt = null;

  // ── API helper ────────────────────────────────────────────────────────────

  async function api(path, opts = {}) {
    if (!config) throw new Error('Not configured');
    const r = await fetch(`${config.url}${path}`, {
      ...opts,
      headers: {
        'Authorization': `Bearer ${config.token}`,
        'Content-Type': 'application/json',
        ...(opts.headers || {})
      }
    });
    if (!r.ok) throw new Error(`HTTP ${r.status}`);
    return r.json();
  }

  // ── View helpers ──────────────────────────────────────────────────────────

  function showView(name) {
    document.querySelectorAll('.view').forEach(v => v.style.display = 'none');
    const el = document.getElementById(`${name}-view`);
    if (el) el.style.display = 'flex';
    const tabbar = document.getElementById('tabbar');
    if (tabbar) tabbar.style.display = name === 'setup' ? 'none' : 'flex';
  }

  function showTab(tab) {
    currentTab = tab;
    document.querySelectorAll('.tab').forEach(t =>
      t.classList.toggle('active', t.dataset.tab === tab)
    );
    if (tab === 'dashboard') { showView('dashboard'); refresh(); }
    else if (tab === 'profiles') { showView('profiles'); loadProfiles(); }
    else if (tab === 'recs') { showView('recs'); loadRecs(); }
    else if (tab === 'settings') { showView('settings'); updateSettings(); }
  }

  // ── Connection ────────────────────────────────────────────────────────────

  async function saveConfig() {
    const urlEl = document.getElementById('server-url');
    const tokenEl = document.getElementById('server-token');
    if (!urlEl || !tokenEl) return;

    const url = urlEl.value.trim().replace(/\/$/, '');
    const token = tokenEl.value.trim();
    if (!url || !token) { alert('Please fill both fields'); return; }

    try {
      const res = await fetch(`${url}/api/health`, {
        headers: { 'Authorization': `Bearer ${token}` }
      });
      if (!res.ok) throw new Error(`Server responded with ${res.status}`);
    } catch (e) {
      alert(`Could not connect: ${e.message}\n\nMake sure:\n• Optimizer desktop is running\n• Remote API is enabled in Settings\n• Phone and PC are on the same network`);
      return;
    }

    config = { url, token };
    localStorage.setItem('opt-config', JSON.stringify(config));
    startApp();
  }

  function disconnect() {
    config = null;
    localStorage.removeItem('opt-config');
    if (refreshInterval) { clearInterval(refreshInterval); refreshInterval = null; }
    const urlEl = document.getElementById('server-url');
    const tokenEl = document.getElementById('server-token');
    if (urlEl) urlEl.value = '';
    if (tokenEl) tokenEl.value = '';
    showView('setup');
  }

  function updateSettings() {
    const urlEl = document.getElementById('cur-url');
    const statusEl = document.getElementById('cur-status');
    if (urlEl) urlEl.textContent = config?.url || '—';
    if (statusEl) statusEl.textContent = config ? 'Connected' : 'Disconnected';
  }

  // ── Dashboard ─────────────────────────────────────────────────────────────

  async function refresh() {
    if (!config) return;

    try {
      const m = await api('/api/metrics');
      const cpu = typeof m.cpu === 'number' ? m.cpu : 0;
      const memPct = m.memory && m.memory.total > 0
        ? ((m.memory.total - m.memory.available) / m.memory.total) * 100
        : 0;
      const gpu = typeof m.gpu === 'number' ? m.gpu : 0;

      setText('m-cpu', `${cpu.toFixed(0)}%`);
      setWidth('bar-cpu', Math.min(100, cpu));
      setText('m-mem', `${memPct.toFixed(0)}%`);
      setWidth('bar-mem', Math.min(100, memPct));
      setText('m-gpu', `${gpu.toFixed(0)}%`);
      setWidth('bar-gpu', Math.min(100, gpu));
    } catch (_) { /* offline — leave last values */ }

    try {
      const s = await api('/api/sensors');
      if (s.cpuTemp != null) setText('s-cputemp', `${s.cpuTemp.toFixed(0)}°C`);
      if (s.gpuTemp != null) setText('s-gputemp', `${s.gpuTemp.toFixed(0)}°C`);
      if (s.cpuPower != null) setText('s-cpupwr', `${s.cpuPower.toFixed(0)}W`);
      if (s.gpuPower != null) setText('s-gpupwr', `${s.gpuPower.toFixed(0)}W`);
    } catch (_) { /* sensors optional */ }

    try {
      const fc = await api('/api/fancontrol');
      const card = document.getElementById('cooling-card');
      if (card && fc.brain) {
        card.style.display = 'block';
        const b = fc.brain;
        const pill = document.getElementById('fc-mode');
        if (pill) {
          pill.textContent = b.alarm ? 'ALARM' : b.stale ? 'STALE' : (b.mode || '—');
          pill.className = 'cooling-pill ' + (b.alarm ? 'danger' : (b.stale || !b.lhmOk) ? 'warn' : 'ok');
        }
        if (b.coolant != null) setText('fc-coolant', `${b.coolant.toFixed(1)}°C`);
        if (b.pumpRpm != null) setText('fc-pump', `${b.pumpRpm} RPM`);
        setText('fc-demands', `${b.caseDemand ?? '—'} / ${b.radDemand ?? '—'}%`);
        setText('fc-profile', (fc.profiles && fc.profiles.lastAppliedProfile) || '—');
        const sn = fc.sentinel;
        if (sn) {
          const verdict = sn.stale ? 'stale' : (sn.pass && (!sn.issues || sn.issues.length === 0)) ? 'PASS' : `${(sn.issues || []).length} issue(s)`;
          const t = new Date(sn.timestamp);
          setText('fc-sentinel', `Health check ${verdict} · ${t.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })}`);
        }
      }
    } catch (_) { /* 404 = federation not configured — card stays hidden */ }
  }

  // ── Profiles ──────────────────────────────────────────────────────────────

  async function loadProfiles() {
    const list = document.getElementById('profiles-list');
    if (!list) return;
    list.innerHTML = '<p class="loading">Loading...</p>';
    try {
      const profiles = await api('/api/profiles');
      list.innerHTML = profiles.length === 0
        ? '<p class="loading">No profiles found.</p>'
        : profiles.map(p => `
          <div class="profile-card">
            <div class="profile-info">
              <h3>${esc(p.name || p.Name || '')}</h3>
              <p>${esc((p.description || p.Description) || '')}</p>
            </div>
            <button class="apply-btn" onclick="App.applyProfile('${esc(String(p.id || p.Id))}')">Apply</button>
          </div>
        `).join('');
    } catch (e) {
      list.innerHTML = '<p class="loading">Could not load profiles. Check connection.</p>';
    }
  }

  async function applyProfile(id) {
    try {
      const r = await api(`/api/apply/${encodeURIComponent(id)}`, { method: 'POST' });
      alert(r.success ? '✓ Profile applied!' : `Failed: ${r.message || 'unknown error'}`);
    } catch (e) {
      alert(`Error: ${e.message}`);
    }
  }

  // ── Recommendations ───────────────────────────────────────────────────────

  async function loadRecs() {
    const list = document.getElementById('recs-list');
    if (!list) return;
    list.innerHTML = '<p class="loading">Loading...</p>';
    try {
      const recs = await api('/api/recommendations');
      if (!recs || recs.length === 0) {
        list.innerHTML = '<p class="loading">No recommendations — your system looks great!</p>';
        return;
      }
      list.innerHTML = recs.map(r => {
        const sev = (r.severity || r.Severity || 'info').toLowerCase();
        return `<div class="rec-card ${sev}">
          <h3>${esc(r.title || r.Title || '')}</h3>
          <p>${esc(r.description || r.Description || '')}</p>
        </div>`;
      }).join('');
    } catch (_) {
      list.innerHTML = '<p class="loading">Could not load recommendations.</p>';
    }
  }

  // ── Cleanup ───────────────────────────────────────────────────────────────

  async function runCleanup() {
    if (!confirm('Run quick cleanup (clear temp files)?')) return;
    try {
      const r = await api('/api/cleanup', { method: 'POST' });
      alert(r.success ? '✓ Cleanup started!' : `Cleanup failed: ${r.message || ''}`);
    } catch (e) {
      alert(`Failed: ${e.message}`);
    }
  }

  // ── Utilities ─────────────────────────────────────────────────────────────

  function esc(s) {
    return String(s).replace(/[<>&"]/g, c =>
      ({ '<': '&lt;', '>': '&gt;', '&': '&amp;', '"': '&quot;' }[c])
    );
  }

  function setText(id, value) {
    const el = document.getElementById(id);
    if (el) el.textContent = value;
  }

  function setWidth(id, pct) {
    const el = document.getElementById(id);
    if (el) el.style.width = `${pct}%`;
  }

  // ── PWA Install prompt ────────────────────────────────────────────────────

  function initInstallPrompt() {
    window.addEventListener('beforeinstallprompt', e => {
      e.preventDefault();
      deferredInstallPrompt = e;
      const banner = document.getElementById('install-banner');
      if (banner) banner.style.display = 'flex';
    });

    window.addEventListener('appinstalled', () => {
      const banner = document.getElementById('install-banner');
      if (banner) banner.style.display = 'none';
      deferredInstallPrompt = null;
    });
  }

  function triggerInstall() {
    if (!deferredInstallPrompt) return;
    deferredInstallPrompt.prompt();
    deferredInstallPrompt.userChoice.then(() => {
      deferredInstallPrompt = null;
      const banner = document.getElementById('install-banner');
      if (banner) banner.style.display = 'none';
    });
  }

  function dismissInstall() {
    const banner = document.getElementById('install-banner');
    if (banner) banner.style.display = 'none';
  }

  // ── Bootstrap ─────────────────────────────────────────────────────────────

  function startApp() {
    showTab('dashboard');
    if (refreshInterval) clearInterval(refreshInterval);
    refreshInterval = setInterval(() => {
      if (currentTab === 'dashboard') refresh();
    }, 3000);
  }

  function init() {
    initInstallPrompt();
    if (config) {
      const urlEl = document.getElementById('server-url');
      const tokenEl = document.getElementById('server-token');
      if (urlEl) urlEl.value = config.url;
      if (tokenEl) tokenEl.value = config.token;
      startApp();
    } else {
      showView('setup');
    }
  }

  return {
    init,
    saveConfig,
    disconnect,
    showTab,
    refresh,
    applyProfile,
    runCleanup,
    triggerInstall,
    dismissInstall
  };
})();

document.addEventListener('DOMContentLoaded', App.init);
