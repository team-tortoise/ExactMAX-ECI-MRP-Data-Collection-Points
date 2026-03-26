// ── State ──────────────────────────────────────────────────────────────────
const tl = {
    teamLead:     null,   // { employeeId, fullName }
    currentOrder: null,   // shop order summary from API
    selectedOp:   null,   // currently selected operation sequence
};

// ── Shared helpers ─────────────────────────────────────────────────────────

async function api(url, body) {
    const opts = body
        ? { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) }
        : { method: 'GET' };
    const res = await fetch(url, opts);
    const data = await res.json();
    if (!res.ok || data.success === false)
        throw new Error(data.message || `HTTP ${res.status}`);
    return data;
}

let _toastTimer = null;
function toast(msg, type = 'info') {
    const el = document.getElementById('toast');
    el.textContent = msg;
    el.className = `toast show${type !== 'info' ? ' ' + type : ''}`;
    clearTimeout(_toastTimer);
    _toastTimer = setTimeout(() => el.classList.remove('show'), 4000);
}

function showScreen(id) {
    document.querySelectorAll('.screen').forEach(s => s.classList.remove('active'));
    document.getElementById(id).classList.add('active');
}

function showLoading(on) {
    document.getElementById('loading').style.display = on ? 'flex' : 'none';
}

// ── Sign in ────────────────────────────────────────────────────────────────

async function tlSignIn() {
    const raw = document.getElementById('inputTlEmpId').value.trim();
    if (!raw) { toast('Enter your employee ID.', 'error'); return; }

    showLoading(true);
    try {
        const data = await api(`/api/workstation/employee/${encodeURIComponent(raw)}`);
        tl.teamLead = data.employee;

        const badge = document.getElementById('tlBadge');
        badge.textContent = data.employee.fullName || data.employee.employeeId;
        badge.style.display = '';
        document.getElementById('btnTlSignOut').style.display = '';
        document.getElementById('inputTlEmpId').value = '';

        // Pre-fill worker field with team lead's own ID (can be changed per entry)
        document.getElementById('inputTlWorker').value = tl.teamLead.employeeId;
        document.getElementById('workerName').textContent = tl.teamLead.fullName || '';

        // Default work date to today
        document.getElementById('inputTlDate').value = new Date().toISOString().split('T')[0];

        showScreen('screenPost');
        setTimeout(() => document.getElementById('inputTlOrder')?.focus(), 80);
    } catch (e) {
        toast(e.message, 'error');
    } finally {
        showLoading(false);
    }
}

function tlSignOut() {
    tl.teamLead    = null;
    tl.currentOrder = null;
    document.getElementById('tlBadge').style.display    = 'none';
    document.getElementById('btnTlSignOut').style.display = 'none';
    document.getElementById('inputTlEmpId').value = '';
    tlResetForm();
    showScreen('screenSignIn');
    setTimeout(() => document.getElementById('inputTlEmpId')?.focus(), 80);
}

// ── Order lookup ───────────────────────────────────────────────────────────

async function tlLookupOrder() {
    const raw = document.getElementById('inputTlOrder').value.trim().replace(/\D/g, '');
    if (!raw) { toast('Enter an order number.', 'error'); return; }

    showLoading(true);
    try {
        const [order, ops] = await Promise.all([
            api(`/api/orders/${encodeURIComponent(raw)}`),
            api(`/api/orders/${encodeURIComponent(raw)}/operations`),
        ]);
        tl.currentOrder = order;

        // Populate order info card
        document.getElementById('tlOrderPart').textContent   = order.partNum ?? '';
        document.getElementById('tlOrderDesc').textContent   = order.partDescription ?? '';
        document.getElementById('tlOrderNum').textContent    = ' ' + order.orderNum;
        document.getElementById('tlOrderQty').textContent    = ' ' + (order.quantity ?? '?');
        document.getElementById('tlOrderDue').textContent    = ' ' + (order.dueDate ? new Date(order.dueDate).toLocaleDateString() : '—');
        document.getElementById('tlOrderStatus').textContent = ' ' + (order.statusText ?? '—');
        document.getElementById('tlOrderCard').style.display = '';

        // Build operation button group
        const group = document.getElementById('opButtonGroup');
        if (ops.length === 0) {
            group.innerHTML = '<p class="op-placeholder">No operations found for this order.</p>';
            tl.selectedOp = null;
        } else {
            const inProc = ops.find(o => (o.queueCode ?? '') === 'Y');
            tl.selectedOp = inProc ? inProc.operationSeq : ops[0].operationSeq;
            group.innerHTML = ops.map(o => {
                const que     = o.queueCode ?? '';
                const isSel   = o.operationSeq === tl.selectedOp;
                const isDone  = que === 'C';
                const btnCls  = ['op-btn', isSel ? 'is-selected' : '', isDone ? 'is-done' : ''].filter(Boolean).join(' ');
                const badgeCls = que === 'Y' ? 'op-badge-active' : que === 'C' ? 'op-badge-done' : 'op-badge-pending';
                const badgeTxt = que === 'Y' ? 'ACTIVE' : que === 'C' ? 'DONE' : 'QUEUE';
                return `<button class="${btnCls}" data-seq="${o.operationSeq}" onclick="selectOp('${o.operationSeq}')">
                    <div class="op-btn-seq">${o.operationSeq}</div>
                    <div class="op-btn-body">
                        <div class="op-btn-desc">${(o.description || '').trim()}</div>
                        <div class="op-btn-wc">${o.workCenter ?? ''}</div>
                    </div>
                    <span class="op-btn-badge ${badgeCls}">${badgeTxt}</span>
                </button>`;
            }).join('');
        }

        toast(`Order ${order.orderNum} loaded — ${ops.length} operation(s)`, 'success');
    } catch (e) {
        toast(e.message, 'error');
        tl.currentOrder = null;
        document.getElementById('tlOrderCard').style.display = 'none';
        document.getElementById('opButtonGroup').innerHTML = '<p class="op-placeholder">Order not found — check the order number.</p>';
        tl.selectedOp = null;
    } finally {
        showLoading(false);
    }
}

// ── Worker name lookup (blur on worker ID field) ───────────────────────────

document.getElementById('inputTlWorker').addEventListener('blur', async () => {
    const id = document.getElementById('inputTlWorker').value.trim();
    const nameEl = document.getElementById('workerName');
    if (!id) { nameEl.textContent = ''; return; }
    try {
        const data = await fetch(`/api/workstation/employee/${encodeURIComponent(id)}`);
        if (data.ok) {
            const d = await data.json();
            nameEl.textContent = d.employee?.fullName ? `✓ ${d.employee.fullName}` : '';
            nameEl.style.color = 'var(--green)';
        } else {
            nameEl.textContent = '⚠ Employee not found';
            nameEl.style.color = 'var(--red)';
        }
    } catch { nameEl.textContent = ''; }
});

// ── Operation selection ────────────────────────────────────────────────────

function selectOp(seq) {
    tl.selectedOp = seq;
    document.querySelectorAll('#opButtonGroup .op-btn').forEach(btn => {
        btn.classList.toggle('is-selected', btn.dataset.seq === seq);
    });
}

// ── Numpad ────────────────────────────────────────────────────────────────

const PAD_LABELS = {
    inputTlQty:   'Pieces Completed',
    inputTlScrap: 'Scrap Qty',
    inputTlRun:   'Run Hours',
    inputTlSetup: 'Setup Hours',
};
const pad = { targetId: null, value: '' };

function openNumpad(fieldId) {
    pad.targetId = fieldId;
    pad.value    = document.getElementById(fieldId).value || '';
    document.getElementById('numpadLabel').textContent = PAD_LABELS[fieldId] ?? 'Value';
    _padRefresh();
    document.getElementById('numpadOverlay').style.display = 'flex';
}

function closeNumpad() {
    if (pad.targetId) document.getElementById(pad.targetId).value = pad.value;
    document.getElementById('numpadOverlay').style.display = 'none';
    pad.targetId = null;
}

function numpadBackdrop(e) {
    if (e.target === document.getElementById('numpadOverlay')) closeNumpad();
}

function numpadPress(key) {
    if (key === 'back') {
        pad.value = pad.value.slice(0, -1);
    } else if (key === '.') {
        if (!pad.value.includes('.')) pad.value += '.';
    } else {
        if (pad.value === '0') pad.value = key;
        else if (pad.value.length < 9) pad.value += key;
    }
    _padRefresh();
    if (pad.targetId) document.getElementById(pad.targetId).value = pad.value;
}

function _padRefresh() {
    const disp = document.getElementById('numpadDisplay');
    disp.textContent = pad.value || '—';
    disp.classList.toggle('empty', !pad.value);
}

// Physical keyboard passthrough when numpad is visible
document.addEventListener('keydown', e => {
    if (!pad.targetId) return;
    if (e.key >= '0' && e.key <= '9')          { numpadPress(e.key); e.preventDefault(); }
    else if (e.key === '.')                     { numpadPress('.'); e.preventDefault(); }
    else if (e.key === 'Backspace')             { numpadPress('back'); e.preventDefault(); }
    else if (e.key === 'Enter' || e.key === 'Tab') { closeNumpad(); e.preventDefault(); }
    else if (e.key === 'Escape')                { pad.value = document.getElementById(pad.targetId)?.value || ''; closeNumpad(); }
});

// ── Option toggles ─────────────────────────────────────────────────────────

document.getElementById('chkTlComplete').addEventListener('change', function () {
    document.getElementById('receiveRow').style.display = this.checked ? '' : 'none';
    if (!this.checked) {
        document.getElementById('stockroomRow').style.display = 'none';
        document.getElementById('chkTlReceive').checked = true;
    }
});

document.getElementById('chkTlReceive').addEventListener('change', function () {
    document.getElementById('stockroomRow').style.display = this.checked ? '' : 'none';
});

// ── Submit ─────────────────────────────────────────────────────────────────

async function tlSubmit() {
    if (!tl.teamLead) { toast('Please sign in first.', 'error'); return; }

    const orderNum    = (document.getElementById('inputTlOrder').value.trim().replace(/\D/g, ''));
    const operationSeq = tl.selectedOp;
    const employeeId  = document.getElementById('inputTlWorker').value.trim();
    const workDateVal = document.getElementById('inputTlDate').value;
    const qty         = parseFloat(document.getElementById('inputTlQty').value) || 0;
    const scrap       = parseFloat(document.getElementById('inputTlScrap').value) || 0;
    const runHours    = parseFloat(document.getElementById('inputTlRun').value) || 0;
    const setupHours  = parseFloat(document.getElementById('inputTlSetup').value) || 0;
    const markComplete = document.getElementById('chkTlComplete').checked;
    const receiveChecked = markComplete && document.getElementById('chkTlReceive').checked;
    const receiveToStock = receiveChecked
        ? (document.getElementById('inputTlStock').value.trim().toUpperCase() || '')
        : null;

    // Validation
    if (!orderNum)     { toast('Enter a shop order number.', 'error'); return; }
    if (!operationSeq) { toast('Select an operation.', 'error'); return; }
    if (!employeeId)   { toast('Enter the worker employee ID.', 'error'); return; }
    if (qty <= 0)      { toast('Pieces completed must be greater than zero.', 'error'); return; }
    if (runHours <= 0) { toast('Run hours must be greater than zero.', 'error'); return; }
    if (setupHours >= runHours) { toast('Setup hours must be less than run hours.', 'error'); return; }

    const workDate = workDateVal ? new Date(workDateVal + 'T00:00:00').toISOString() : null;

    showLoading(true);
    try {
        const data = await api('/api/teamlead/post', {
            employeeId, orderNum, operationSeq,
            qtyCompleted: qty,
            runHours,
            setupHours,
            qtyScrap: scrap,
            workDate,
            markComplete,
            receiveToStock,
        });

        // Show result banner
        const resultCard  = document.getElementById('resultCard');
        const resultTitle = document.getElementById('resultTitle');
        const resultDetail = document.getElementById('resultDetail');
        resultTitle.textContent  = `✓ ${data.message}`;
        let detail = `Order ${orderNum} · Op ${operationSeq} · Emp ${employeeId} · ${qty} pc · ${runHours}h run`;
        if (setupHours > 0) detail += ` / ${setupHours}h setup`;
        if (data.receivedToStock) detail += ` → stockroom ${data.receivedToStock}`;
        resultDetail.textContent = detail;
        resultCard.style.display = '';

        toast(data.message, 'success');

        // Clear quantities/hours for the next entry but keep order/op/emp/date
        document.getElementById('inputTlQty').value   = '';
        document.getElementById('inputTlScrap').value = '';
        document.getElementById('inputTlRun').value   = '';
        document.getElementById('inputTlSetup').value = '';
        document.getElementById('chkTlComplete').checked = false;
        document.getElementById('chkTlReceive').checked  = true;
        document.getElementById('receiveRow').style.display    = 'none';
        document.getElementById('stockroomRow').style.display  = 'none';
        document.getElementById('inputTlQty').focus();
    } catch (e) {
        toast(e.message, 'error');
    } finally {
        showLoading(false);
    }
}

// ── Reset form ─────────────────────────────────────────────────────────────

function tlResetForm() {
    tl.currentOrder = null;
    tl.selectedOp   = null;
    document.getElementById('inputTlOrder').value   = '';
    document.getElementById('tlOrderCard').style.display = 'none';
    document.getElementById('opButtonGroup').innerHTML = '<p class="op-placeholder">Look up an order to see operations.</p>';
    document.getElementById('inputTlDate').value    = new Date().toISOString().split('T')[0];
    document.getElementById('inputTlQty').value     = '';
    document.getElementById('inputTlScrap').value   = '';
    document.getElementById('inputTlRun').value     = '';
    document.getElementById('inputTlSetup').value   = '';
    document.getElementById('workerName').textContent = tl.teamLead
        ? (tl.teamLead.fullName || '') : '';
    if (tl.teamLead) {
        document.getElementById('inputTlWorker').value = tl.teamLead.employeeId;
        document.getElementById('workerName').style.color = 'var(--green)';
    }
    document.getElementById('chkTlComplete').checked = false;
    document.getElementById('chkTlReceive').checked  = true;
    document.getElementById('receiveRow').style.display    = 'none';
    document.getElementById('stockroomRow').style.display  = 'none';
    document.getElementById('resultCard').style.display    = 'none';
    document.getElementById('inputTlOrder').focus();
}
