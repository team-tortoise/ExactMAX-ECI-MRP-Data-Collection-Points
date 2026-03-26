// ── State ─────────────────────────────────────────────────────────────────
const ws = {
    employee:      null,   // { employeeId, fullName }
    activeSession: null,   // ActiveSession from API (or null)
    currentOrder:  null,   // ShopOrderSummary
    timerMs:       0,      // Date.now() - (elapsedSeconds * 1000)
    timerInt:      null,   // banner timer
    postMode:      false,  // true = Complete & Post, false = Log Out
    coTimerInt:    null,   // modal timer
};

// ── Screen navigation ────────────────────────────────────────────────────
function showScreen(id) {
    document.querySelectorAll('.screen').forEach(s => s.classList.remove('active'));
    document.getElementById(id).classList.add('active');

    // Auto-focus the correct scan input so a scanner can fire immediately
    const focusTargets = {
        screenEmployee:  'inputEmpId',
        screenDashboard: 'inputOrderScan',
    };
    const target = focusTargets[id];
    if (target) setTimeout(() => document.getElementById(target)?.focus(), 80);
}

// ── Employee sign-in ─────────────────────────────────────────────────────
document.getElementById('inputEmpId').addEventListener('keydown', e => {
    if (e.key === 'Enter') signIn();
});

async function signIn() {
    const raw = document.getElementById('inputEmpId').value.trim();
    if (!raw) return;

    showLoading(true);
    try {
        const data = await api(`/api/workstation/employee/${encodeURIComponent(raw)}`);
        ws.employee       = data.employee;
        ws.activeSession  = data.activeSession;

        document.getElementById('employeeBadge').textContent =
            data.employee.fullName || data.employee.employeeId;
        document.getElementById('employeeBadge').style.display = '';
        document.getElementById('btnSignOut').style.display    = '';
        document.getElementById('inputEmpId').value = '';

        refreshDashboard();

        if (data.activeSession) {
            // Already has an active session — jump to the order so they can complete it.
            const orderNum = data.activeSession.orderNum;
            const [order, ops] = await Promise.all([
                api(`/api/orders/${encodeURIComponent(orderNum)}`),
                api(`/api/orders/${encodeURIComponent(orderNum)}/operations`),
            ]);
            ws.currentOrder = order;
            renderOrderScreen(order, ops);
            showScreen('screenOrder');
        } else {
            showScreen('screenDashboard');
            flashInput('inputOrderScan');
        }
    } catch (e) {
        toast(e.message, 'error');
        document.getElementById('inputEmpId').select();
    } finally {
        showLoading(false);
    }
}

function signOut() {
    if (ws.activeSession && !confirm('You have an active session. Sign out anyway?')) return;

    stopTimer();
    ws.employee = ws.activeSession = ws.currentOrder = null;
    ws.timerMs  = 0;

    document.getElementById('employeeBadge').style.display = 'none';
    document.getElementById('btnSignOut').style.display    = 'none';
    document.getElementById('inputEmpId').value            = '';
    showScreen('screenEmployee');
}

// ── Dashboard ────────────────────────────────────────────────────────────
document.getElementById('inputOrderScan').addEventListener('keydown', e => {
    if (e.key === 'Enter') lookupOrder();
});

function refreshDashboard() {
    const banner = document.getElementById('activeClockBanner');
    const idle   = document.getElementById('idleBanner');

    if (ws.activeSession) {
        const s = ws.activeSession;
        banner.style.display = '';
        idle.style.display   = 'none';

        document.getElementById('bannerOrder').textContent =
            `Order ${s.orderNum}  ·  Op ${s.operationSeq}  ·  WC: ${s.workCenter}`;
        document.getElementById('bannerPart').textContent =
            s.partNum ? `${s.partNum} — ${s.partDescription}` : s.partDescription;

        startTimer(s.elapsedSeconds);
    } else {
        banner.style.display = 'none';
        idle.style.display   = '';
        stopTimer();
    }
}

// ── Live elapsed timer ───────────────────────────────────────────────────
function startTimer(elapsedSecondsAtLoad) {
    stopTimer();
    // Seed so the timer reads elapsedSecondsAtLoad immediately and counts up
    ws.timerMs = Date.now() - elapsedSecondsAtLoad * 1000;

    function tick() {
        const ms = Math.max(0, Date.now() - ws.timerMs);
        document.getElementById('bannerTimer').textContent = fmtElapsed(ms);
    }
    tick();
    ws.timerInt = setInterval(tick, 1000);
}

function stopTimer() {
    clearInterval(ws.timerInt);
    ws.timerInt = null;
}

function fmtElapsed(ms) {
    const s   = Math.floor(ms / 1000);
    const h   = Math.floor(s / 3600);
    const m   = Math.floor((s % 3600) / 60);
    const sec = s % 60;
    return `${h}:${String(m).padStart(2, '0')}:${String(sec).padStart(2, '0')}`;
}

// ── Order lookup ─────────────────────────────────────────────────────────
async function lookupOrder() {
    const raw = document.getElementById('inputOrderScan').value.trim();
    if (!raw) return;

    const orderNum = normalizeOrderNum(raw);
    document.getElementById('inputOrderScan').value = '';

    // Block navigating to a different order while a session is active.
    if (ws.activeSession && ws.activeSession.orderNum !== orderNum) {
        toast(`Must complete Order ${ws.activeSession.orderNum} first.`, 'error');
        document.getElementById('inputOrderScan').focus();
        return;
    }

    showLoading(true);
    try {
        const [order, ops] = await Promise.all([
            api(`/api/orders/${encodeURIComponent(orderNum)}`),
            api(`/api/orders/${encodeURIComponent(orderNum)}/operations`),
        ]);
        ws.currentOrder = order;
        renderOrderScreen(order, ops);
        showScreen('screenOrder');
    } catch (e) {
        toast(e.message, 'error');
        document.getElementById('inputOrderScan').focus();
    } finally {
        showLoading(false);
    }
}

// Strips the 4-char suffix from a 12-char Employee_Work order number.
// "502368470000" → "50236847". Plain order numbers pass through unchanged.
function normalizeOrderNum(s) {
    s = s.trim();
    if (s.length >= 12 && s.endsWith('0000')) return s.slice(0, -4).trimEnd();
    return s;
}

// ── Order / operation screen ─────────────────────────────────────────────
function renderOrderScreen(order, ops) {
    document.getElementById('wsOrderInfo').innerHTML = `
        <div style="display:flex;justify-content:space-between;align-items:flex-start;gap:12px;">
            <div>
                <div style="font-weight:700;font-size:1.15rem;">
                    ${esc(order.orderNum)} &mdash; ${esc(order.partNum)}
                </div>
                <div style="color:var(--gray-500);font-size:.9rem;margin-top:2px;">
                    ${esc(order.partDescription)}
                </div>
            </div>
            <button class="btn btn-secondary btn-sm" onclick="backToDashboard()" style="flex-shrink:0;">
                &#8592; Back
            </button>
        </div>
        <div class="card-meta" style="margin-top:10px;">
            <span class="card-tag">Qty: ${order.quantity}</span>
            <span class="card-tag">${esc(order.statusText)}</span>
            ${order.dueDate ? `<span class="card-tag">Due: ${formatDate(order.dueDate)}</span>` : ''}
        </div>`;

    const list = document.getElementById('wsOpList');
    if (!ops.length) {
        list.innerHTML = '<p style="color:var(--gray-500);text-align:center;padding:32px;">No operations found.</p>';
        return;
    }

    list.innerHTML = ops.map(op => {
        const isActive = ws.activeSession?.orderNum === order.orderNum
                      && ws.activeSession?.operationSeq === op.operationSeq;
        const pct = order.quantity > 0
            ? Math.min(100, Math.round((op.qtyCompleted / order.quantity) * 100))
            : 0;

        return `
        <div class="ws-op-card ${isActive ? 'is-active' : ''}">
            <div style="flex:1;min-width:0;">
                <div class="ws-op-seq">
                    Op ${esc(op.operationSeq)}
                    ${isActive ? '<span style="color:var(--green);font-size:.85rem;"> &#9679; Active</span>' : ''}
                </div>
                <div class="ws-op-desc">${esc(op.description)} &bull; WC: ${esc(op.workCenter)}</div>
                <div class="ws-op-meta">
                    Run: ${op.runActual.toFixed(1)}/${op.runStandard.toFixed(1)} hr
                    &nbsp;&bull;&nbsp;
                    Qty: ${op.qtyCompleted}/${order.quantity}${pct > 0 ? ` (${pct}%)` : ''}
                </div>
            </div>
            <div class="ws-op-btns">
                ${isActive
                    ? `<button class="btn btn-secondary btn-sm"
                               onclick="showSessionModal(false)">Log Out</button>
                       <button class="btn btn-danger btn-sm"
                               onclick="showSessionModal(true)">Complete</button>`
                    : `<button class="btn btn-primary btn-sm"
                               onclick="startSession('${esc(op.operationSeq)}')"
                               ${ws.activeSession ? 'disabled title="Complete active session first"' : ''}>
                           Start
                       </button>`
                }
            </div>
        </div>`;
    }).join('');
}

function backToDashboard() {
    ws.currentOrder = null;
    showScreen('screenDashboard');
}

// ── Standalone Receive to Stock modal ────────────────────────────────────
// ── Start Session ─────────────────────────────────────────────────────────────
async function startSession(operationSeq) {
    if (!ws.employee || !ws.currentOrder) return;
    showLoading(true);
    try {
        const data = await api('/api/session/start', {
            employeeId:   ws.employee.employeeId,
            orderNum:     ws.currentOrder.orderNum,
            operationSeq: operationSeq,
            shift:        '1',
        });
        ws.activeSession = data.session;
        toast(data.message, 'success');
        refreshDashboard();
        showScreen('screenDashboard');
    } catch (e) {
        toast(e.message, 'error');
    } finally {
        showLoading(false);
    }
}

// ── Clock In ──────────────────────────────────────────────────────────────
// ── Session modal (Log Out / Complete & Post) ───────────────────────────
function showSessionModal(withCompletion) {
    if (!ws.activeSession) return;
    ws.postMode = withCompletion;

    const s = ws.activeSession;

    document.getElementById('coTitle').textContent =
        withCompletion ? 'Complete & Post' : 'Log Out';

    document.getElementById('coSummary').innerHTML = `
        <div class="ws-co-order">Order ${esc(s.orderNum)} &bull; Op ${esc(s.operationSeq)}</div>
        <div class="ws-co-part">${esc(s.partNum)} ${esc(s.partDescription)}</div>
        <div class="ws-co-timer" id="coTimer">${fmtElapsed(Math.max(0, Date.now() - ws.timerMs))}</div>`;

    document.getElementById('coForm').style.display = withCompletion ? '' : 'none';
    document.getElementById('inputCoQty').value   = '';
    document.getElementById('inputCoScrap').value = '';
    document.getElementById('inputCoSetup').value = '';

    document.getElementById('coActions').innerHTML = `
        <button class="btn btn-secondary" onclick="closeSessionModal()">Cancel</button>
        <button class="btn ${withCompletion ? 'btn-danger' : 'btn-primary'}"
                onclick="${withCompletion ? 'submitSession()' : 'abandonSession()'}">
            ${withCompletion ? 'Complete &amp; Post' : 'Log Out'}
        </button>`;

    clearInterval(ws.coTimerInt);
    ws.coTimerInt = setInterval(() => {
        const el = document.getElementById('coTimer');
        if (!el) { clearInterval(ws.coTimerInt); return; }
        el.textContent = fmtElapsed(Math.max(0, Date.now() - ws.timerMs));
    }, 1000);

    document.getElementById('modalClockOut').style.display = '';
    if (withCompletion) setTimeout(() => document.getElementById('inputCoQty').focus(), 100);
}

function closeSessionModal() {
    clearInterval(ws.coTimerInt);
    ws.coTimerInt = null;
    document.getElementById('modalClockOut').style.display = 'none';
}

async function abandonSession() {
    if (!ws.activeSession) return;
    closeSessionModal();
    showLoading(true);
    try {
        await api('/api/session/abandon', { sessionId: ws.activeSession.sessionId });
        ws.activeSession = null;
        toast('Logged out.', 'success');
        stopTimer();
        refreshDashboard();
    } catch (e) {
        toast(e.message, 'error');
    } finally {
        showLoading(false);
    }
}

async function submitSession() {
    if (!ws.activeSession) return;

    const qty   = parseFloat(document.getElementById('inputCoQty').value)   || 0;
    const scrap = parseFloat(document.getElementById('inputCoScrap').value)  || 0;
    const setup = parseFloat(document.getElementById('inputCoSetup').value)  || 0;

    if (qty <= 0) {
        toast('Enter pieces completed before posting.', 'error');
        return;
    }
    if (setup > 0 && ws.timerMs) {
        const elapsedHours = (Date.now() - ws.timerMs) / 3600000;
        if (setup >= elapsedHours) {
            toast(`Setup time (${setup}h) must be less than elapsed time (${elapsedHours.toFixed(2)}h)`, 'error');
            return;
        }
    }

    closeSessionModal();
    showLoading(true);
    try {
        const data = await api('/api/session/post', {
            sessionId:    ws.activeSession.sessionId,
            qtyCompleted: qty,
            qtyScrap:     scrap,
            setupHours:   setup,
        });
        ws.activeSession = null;
        toast(`Posted ${qty} pc${qty !== 1 ? 's' : ''} · ${data.elapsedHours.toFixed(2)} h`, 'success');
        stopTimer();
        refreshDashboard();
    } catch (e) {
        toast(e.message, 'error');
    } finally {
        showLoading(false);
    }
}

// ── Shared helpers ────────────────────────────────────────────────────────
async function api(url, body) {
    const opts = body !== undefined
        ? { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) }
        : {};
    const resp = await fetch(url, opts);
    const ct   = resp.headers.get('content-type') || '';
    let data;
    if (ct.includes('application/json')) {
        data = await resp.json();
    } else {
        const text = await resp.text();
        throw new Error(`Server error (${resp.status}): ${text.substring(0, 200)}`);
    }
    if (!resp.ok || data.success === false) throw new Error(data.message || `Error (${resp.status})`);
    return data;
}

function showLoading(show) {
    document.getElementById('loading').style.display = show ? '' : 'none';
}

let toastTimer;
function toast(msg, type) {
    const el = document.getElementById('toast');
    el.textContent = msg;
    el.className   = 'toast show' + (type ? ' ' + type : '');
    clearTimeout(toastTimer);
    if (type === 'error') {
        el.onclick = () => el.classList.remove('show');
    } else {
        el.onclick = null;
        toastTimer = setTimeout(() => el.classList.remove('show'), 5000);
    }
}

function esc(s) {
    if (!s) return '';
    const d = document.createElement('div');
    d.textContent = s;
    return d.innerHTML;
}

function formatDate(d) {
    if (!d) return '';
    return new Date(d).toLocaleDateString('en-US', { month: 'short', day: 'numeric', year: 'numeric' });
}

// Brief green border flash on an input — visual acknowledgement after a scan
function flashInput(id) {
    const el = document.getElementById(id);
    if (!el) return;
    el.classList.add('scan-ok');
    setTimeout(() => el.classList.remove('scan-ok'), 600);
}
