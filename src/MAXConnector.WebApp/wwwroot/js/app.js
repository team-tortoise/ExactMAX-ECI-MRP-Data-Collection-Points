// ── State ─────────────────────────────────────────────────────────────────
let employeeId = '';
let currentOrder = null;
let currentOp = null;
let searchTimeout = null;
let currentEntries = [];

// ── Navigation ───────────────────────────────────────────────────────────
const screens = ['screenLogin', 'screenOrders', 'screenDetail'];
let screenStack = ['screenLogin'];

function showScreen(id) {
    screens.forEach(s => document.getElementById(s).classList.remove('active'));
    document.getElementById(id).classList.add('active');
    document.getElementById('btnBack').style.display = (id === 'screenLogin') ? 'none' : '';
}

function navigateTo(id) {
    screenStack.push(id);
    showScreen(id);
    updateHeaderTitle();
}

function goBack() {
    if (screenStack.length <= 1) return;
    screenStack.pop();
    showScreen(screenStack[screenStack.length - 1]);
    updateHeaderTitle();
}

function updateHeaderTitle() {
    const current = screenStack[screenStack.length - 1];
    const titles = {
        screenLogin:  'Shop Floor',
        screenOrders: 'Shop Orders',
        screenDetail: currentOrder ? `Order ${currentOrder.orderNum}` : 'Order Detail',
    };
    document.getElementById('headerTitle').textContent = titles[current] || 'Shop Floor';
}

// ── Employee Sign In ────────────────────────────────────────────────────
async function signIn() {
    const id = document.getElementById('inputEmployeeId').value.trim();
    if (!id) { toast('Enter your Employee ID', 'error'); return; }

    showLoading(true);
    try {
        const emp = await api(`/api/employees/${encodeURIComponent(id)}`);
        employeeId = emp.employeeId;
        const label = emp.fullName ? `${emp.employeeId} – ${emp.fullName}` : emp.employeeId;
        document.getElementById('employeeBadge').textContent = label;
        document.getElementById('employeeBadge').style.display = '';
        navigateTo('screenOrders');
        loadOrders();
    } catch (e) {
        toast(e.message, 'error');
    } finally {
        showLoading(false);
    }
}

document.getElementById('inputEmployeeId').addEventListener('keydown', e => {
    if (e.key === 'Enter') signIn();
});

// ── Orders ──────────────────────────────────────────────────────────────
async function loadOrders(search) {
    const url = search ? `/api/orders?search=${encodeURIComponent(search)}` : '/api/orders';
    showLoading(true);
    try {
        const orders = await api(url);
        renderOrders(orders);
    } catch (e) {
        toast(e.message, 'error');
    } finally {
        showLoading(false);
    }
}

function renderOrders(orders) {
    const list = document.getElementById('orderList');
    if (!orders.length) {
        list.innerHTML = '<p style="text-align:center;color:var(--gray-500);padding:40px;">No open orders found.</p>';
        return;
    }
    list.innerHTML = orders.map(o => `
        <div class="card" onclick="selectOrder('${esc(o.orderNum)}')">
            <div class="card-title">${esc(o.orderNum)} &mdash; ${esc(o.partNum)}</div>
            <div class="card-sub">${esc(o.partDescription)}</div>
            <div class="card-meta">
                <span class="card-tag">Qty: ${o.quantity}</span>
                <span class="card-tag ${o.status === 4 ? 'status-in-process' : ''}">${esc(o.statusText)}</span>
                ${o.dueDate ? `<span class="card-tag">Due: ${formatDate(o.dueDate)}</span>` : ''}
            </div>
        </div>
    `).join('');
}

function debounceSearch() {
    clearTimeout(searchTimeout);
    searchTimeout = setTimeout(() => {
        loadOrders(document.getElementById('inputSearch').value.trim() || undefined);
    }, 400);
}

// ── Order Detail ────────────────────────────────────────────────────────
async function selectOrder(orderNum) {
    showLoading(true);
    try {
        const [order, ops, entries] = await Promise.all([
            api(`/api/orders/${orderNum}`),
            api(`/api/orders/${orderNum}/operations`),
            api(`/api/time-entries/${orderNum}`),
        ]);
        currentOrder = order;
        currentEntries = entries;
        renderOrderDetail(order, ops);
        renderTimeEntries(entries);
        navigateTo('screenDetail');
    } catch (e) {
        toast(e.message, 'error');
    } finally {
        showLoading(false);
    }
}

function renderOrderDetail(order, ops) {
    document.getElementById('orderInfo').innerHTML = `
        <h2>${esc(order.orderNum)} &mdash; ${esc(order.partNum)}</h2>
        <div class="desc">${esc(order.partDescription)}</div>
        <dl class="detail-grid">
            <dt>Quantity</dt><dd>${order.quantity}</dd>
            <dt>Status</dt><dd>${esc(order.statusText)}</dd>
            <dt>Due Date</dt><dd>${order.dueDate ? formatDate(order.dueDate) : '—'}</dd>
            <dt>Stockroom</dt><dd>${esc(order.stockroom) || '—'}</dd>
        </dl>
    `;

    const opList = document.getElementById('operationList');
    if (!ops.length) {
        opList.innerHTML = '<p style="color:var(--gray-500);">No operations on this order.</p>';
        return;
    }
    opList.innerHTML = ops.map(op => {
        const pct = order.quantity > 0 ? Math.min(100, Math.round((op.qtyCompleted / order.quantity) * 100)) : 0;
        return `
        <div class="card op-card">
            <div style="display:flex;justify-content:space-between;align-items:center;">
                <div>
                    <div class="card-title">Op ${esc(op.operationSeq)}</div>
                    <div class="card-sub">${esc(op.description)} &bull; WC: ${esc(op.workCenter)}</div>
                </div>
                <button class="btn btn-primary" onclick="event.stopPropagation();showLogWork('${esc(op.operationSeq)}','${esc(op.description)}')">
                    Log Work
                </button>
            </div>
            <div class="op-progress">
                <div class="progress-bar"><div class="progress-fill" style="width:${pct}%"></div></div>
                <span style="font-size:.8rem;color:var(--gray-500);">${op.qtyCompleted}/${order.quantity}</span>
            </div>
            <div class="card-meta">
                <span class="card-tag">Run: ${op.runActual.toFixed(1)}/${op.runStandard.toFixed(1)} hr</span>
                <span class="card-tag">Setup: ${op.setupActual.toFixed(1)}/${op.setupStandard.toFixed(1)} hr</span>
            </div>
        </div>`;
    }).join('');
}

// ── Log Work Modal ──────────────────────────────────────────────────────
function showLogWork(opSeq, opDesc) {
    currentOp = { seq: opSeq, desc: opDesc };
    document.getElementById('logWorkTitle').textContent = `Log Work — Op ${opSeq}`;
    document.getElementById('inputWorkDate').value = new Date().toISOString().split('T')[0];
    document.getElementById('inputQty').value = '0';
    document.getElementById('inputRunTime').value = '0';
    document.getElementById('inputSetupTime').value = '0';
    document.getElementById('inputScrap').value = '0';
    document.getElementById('inputNotes').value = '';
    document.getElementById('modalLogWork').style.display = '';
}

async function submitTimeEntry(andPost = false) {
    const qty       = parseFloat(document.getElementById('inputQty').value) || 0;
    const runTime   = parseFloat(document.getElementById('inputRunTime').value) || 0;
    const setupTime = parseFloat(document.getElementById('inputSetupTime').value) || 0;
    const scrap     = parseFloat(document.getElementById('inputScrap').value) || 0;
    const workDate  = document.getElementById('inputWorkDate').value;
    const notes     = document.getElementById('inputNotes').value.trim();

    if (qty <= 0 && runTime <= 0 && setupTime <= 0) {
        toast('Enter quantity or time', 'error');
        return;
    }

    closeModal('modalLogWork');
    showLoading(true);

    try {
        const saved = await api('/api/time-entry', {
            orderNum:     currentOrder.orderNum,
            operationSeq: currentOp.seq,
            employeeId:   employeeId,
            qtyCompleted: qty,
            runHours:     runTime,
            setupHours:   setupTime,
            qtyScrap:     scrap,
            workDate:     workDate || null,
            notes:        notes || null,
        });

        if (andPost && saved.entryId) {
            const posted = await api(`/api/time-entries/${saved.entryId}/post`, {});
            toast(posted.message, 'success');
        } else {
            toast(saved.message, 'success');
        }

        // Refresh operations and time entries
        const [ops, entries] = await Promise.all([
            api(`/api/orders/${currentOrder.orderNum}/operations`),
            api(`/api/time-entries/${currentOrder.orderNum}`),
        ]);
        currentEntries = entries;
        renderOrderDetail(currentOrder, ops);
        renderTimeEntries(entries);
    } catch (e) {
        toast(e.message, 'error');
    } finally {
        showLoading(false);
    }
}

// ── Time Entry Display ──────────────────────────────────────────────────
function renderTimeEntries(entries) {
    const section = document.getElementById('timeEntriesSection');
    const list    = document.getElementById('timeEntryList');
    const btnAll  = document.getElementById('btnPostAll');

    if (!entries.length) {
        section.style.display = 'none';
        list.innerHTML = '';
        return;
    }

    section.style.display = '';
    const hasPending = entries.some(e => !e.postedToMax);
    btnAll.style.display = hasPending ? '' : 'none';

    list.innerHTML = entries.map(e => `
        <div class="card entry-card ${e.postedToMax ? 'entry-posted' : 'entry-pending'}" id="entry-${e.entryId}">
            <div class="entry-header">
                <span class="entry-op">Op ${esc(e.operationSeq)}</span>
                <span class="entry-badge ${e.postedToMax ? 'badge-posted' : 'badge-pending'}">
                    ${e.postedToMax ? '&#10003; Posted' : '&#9679; Pending'}
                </span>
            </div>
            <div class="entry-row">
                <span class="entry-field"><strong>Date</strong> ${formatDate(e.workDate)}</span>
                <span class="entry-field"><strong>Emp</strong> ${esc(e.employeeId)}</span>
            </div>
            <div class="entry-row">
                <span class="entry-field"><strong>Run</strong> ${e.runHours.toFixed(2)} hr</span>
                <span class="entry-field"><strong>Setup</strong> ${e.setupHours.toFixed(2)} hr</span>
                <span class="entry-field"><strong>Qty</strong> ${e.qtyCompleted}</span>
                ${e.qtyScrap > 0 ? `<span class="entry-field"><strong>Scrap</strong> ${e.qtyScrap}</span>` : ''}
            </div>
            ${e.notes ? `<div class="entry-notes">${esc(e.notes)}</div>` : ''}
            ${!e.postedToMax ? `
            <div class="entry-actions">
                <button class="btn btn-success btn-sm" onclick="postEntry(${e.entryId})">Post to MAX</button>
                <button class="btn btn-danger btn-sm" onclick="deleteEntry(${e.entryId})">Delete</button>
            </div>` : ''}
        </div>
    `).join('');
}

async function deleteEntry(entryId) {
    if (!confirm('Delete this time entry?')) return;
    showLoading(true);
    try {
        await fetch(`/api/time-entries/${entryId}`, { method: 'DELETE' });
        toast('Entry deleted', 'success');
        const entries = await api(`/api/time-entries/${currentOrder.orderNum}`);
        currentEntries = entries;
        renderTimeEntries(entries);
    } catch (e) {
        toast(e.message, 'error');
    } finally {
        showLoading(false);
    }
}

async function postEntry(entryId) {
    showLoading(true);
    try {
        const resp = await api(`/api/time-entries/${entryId}/post`, {});
        toast(resp.message, 'success');
        const entries = await api(`/api/time-entries/${currentOrder.orderNum}`);
        currentEntries = entries;
        renderTimeEntries(entries);
    } catch (e) {
        toast(e.message, 'error');
    } finally {
        showLoading(false);
    }
}

async function postAllPending() {
    const pending = currentEntries.filter(e => !e.postedToMax);
    if (!pending.length) return;

    showLoading(true);
    let posted = 0;
    const errors = [];
    for (const e of pending) {
        try {
            await api(`/api/time-entries/${e.entryId}/post`, {});
            posted++;
        } catch (err) {
            errors.push(`Op ${e.operationSeq}: ${err.message}`);
        }
    }
    showLoading(false);

    if (errors.length) {
        toast(`Posted ${posted}, failed ${errors.length}`, 'error');
        // Show first error detail
        setTimeout(() => toast(errors[0], 'error'), 3200);
    } else {
        toast(`All ${posted} entr${posted === 1 ? 'y' : 'ies'} posted to MAX`, 'success');
    }

    const entries = await api(`/api/time-entries/${currentOrder.orderNum}`);
    currentEntries = entries;
    renderTimeEntries(entries);
}

// ── Helpers ─────────────────────────────────────────────────────────────
async function api(url, body) {
    const opts = body
        ? { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) }
        : {};
    const resp = await fetch(url, opts);
    let data;
    const ct = resp.headers.get('content-type') || '';
    if (ct.includes('application/json')) {
        data = await resp.json();
    } else {
        const text = await resp.text();
        throw new Error(`Server error (${resp.status}): ${text.substring(0, 200)}`);
    }
    if (!resp.ok || data.success === false) {
        throw new Error(data.message || `Request failed (${resp.status})`);
    }
    return data;
}

function closeModal(id) {
    document.getElementById(id).style.display = 'none';
}

function showLoading(show) {
    document.getElementById('loading').style.display = show ? '' : 'none';
}

let toastTimer = null;
function toast(msg, type) {
    const el = document.getElementById('toast');
    el.textContent = msg;
    el.className = 'toast show' + (type ? ' ' + type : '');
    clearTimeout(toastTimer);
    // Errors stay until tapped; success auto-dismisses after 4s
    if (type === 'error') {
        el.onclick = () => el.classList.remove('show');
    } else {
        el.onclick = null;
        toastTimer = setTimeout(() => el.classList.remove('show'), 4000);
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
    const dt = new Date(d);
    return dt.toLocaleDateString('en-US', { month: 'short', day: 'numeric', year: 'numeric' });
}
