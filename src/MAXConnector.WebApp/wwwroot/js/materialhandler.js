// ── State ─────────────────────────────────────────────────────────────────
const mh = {
    employee: null,   // { employeeId, fullName }
};

// ── Sign in ───────────────────────────────────────────────────────────────

document.getElementById('inputMhEmpId').addEventListener('keydown', e => {
    if (e.key === 'Enter') mhSignIn();
});

async function mhSignIn() {
    const raw = document.getElementById('inputMhEmpId').value.trim();
    if (!raw) { toast('Enter your Employee ID.', 'error'); return; }

    showLoading(true);
    try {
        const data = await api(`/api/workstation/employee/${encodeURIComponent(raw)}`);
        mh.employee = data.employee;

        document.getElementById('mhBadge').textContent =
            data.employee.fullName || data.employee.employeeId;
        document.getElementById('mhBadge').style.display    = '';
        document.getElementById('btnMhSignOut').style.display = '';
        document.getElementById('inputMhEmpId').value = '';

        showScreen('screenReceive');
        setTimeout(() => document.getElementById('inputMhOrder')?.focus(), 80);
    } catch (e) {
        toast(e.message, 'error');
    } finally {
        showLoading(false);
    }
}

function mhSignOut() {
    mh.employee = null;
    document.getElementById('mhBadge').style.display      = 'none';
    document.getElementById('btnMhSignOut').style.display = 'none';
    document.getElementById('inputMhEmpId').value         = '';
    document.getElementById('resultCard').style.display   = 'none';
    showScreen('screenSignIn');
    setTimeout(() => document.getElementById('inputMhEmpId')?.focus(), 80);
}

// ── Order field ───────────────────────────────────────────────────────────

document.getElementById('inputMhOrder').addEventListener('keydown', e => {
    if (e.key === 'Enter') document.getElementById('inputMhQty')?.focus();
});

function mhClearOrder() {
    document.getElementById('inputMhOrder').value = '';
    document.getElementById('inputMhOrder').focus();
}

// ── Submit receive ────────────────────────────────────────────────────────

async function mhSubmit() {
    if (!mh.employee) { toast('Please sign in first.', 'error'); return; }

    const rawOrder = document.getElementById('inputMhOrder').value.trim().replace(/\D/g, '');
    const qty      = parseFloat(document.getElementById('inputMhQty').value) || 0;
    const stock    = document.getElementById('inputMhStock').value.trim().toUpperCase();

    if (!rawOrder) { toast('Enter a work order number.', 'error'); return; }
    if (qty <= 0)  { toast('Quantity must be greater than zero.', 'error'); return; }

    showLoading(true);
    try {
        const data = await api('/api/material/receive', {
            employeeId:    mh.employee.employeeId,
            orderNum:      rawOrder,
            qtyCompleted:  qty,
            receiveToStock: stock || null,
        });

        const resultCard   = document.getElementById('resultCard');
        const resultTitle  = document.getElementById('resultTitle');
        const resultDetail = document.getElementById('resultDetail');

        resultTitle.textContent  = `\u2713 ${data.message}`;
        resultDetail.textContent =
            `Order ${rawOrder} \u00b7 ${qty} pc${qty !== 1 ? 's' : ''} \u2192 stockroom ${data.receivedToStock}`;
        resultCard.style.display = '';

        toast(data.message, 'success');

        // Clear qty and stockroom for next receive; keep order number
        document.getElementById('inputMhQty').value   = '';
        document.getElementById('inputMhStock').value = '';
        setTimeout(() => document.getElementById('inputMhOrder')?.select(), 100);
    } catch (e) {
        toast(e.message, 'error');
    } finally {
        showLoading(false);
    }
}

// ── Shared helpers ────────────────────────────────────────────────────────

async function api(url, body) {
    const opts = body
        ? { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) }
        : { method: 'GET' };
    const res  = await fetch(url, opts);
    const data = await res.json();
    if (!res.ok || data.success === false)
        throw new Error(data.message || `HTTP ${res.status}`);
    return data;
}

function showScreen(id) {
    document.querySelectorAll('.screen').forEach(s => s.classList.remove('active'));
    document.getElementById(id).classList.add('active');
}

function showLoading(on) {
    document.getElementById('loading').style.display = on ? 'flex' : 'none';
}

let _toastTimer = null;
function toast(msg, type = 'info') {
    const el = document.getElementById('toast');
    el.textContent = msg;
    el.className = `toast show${type !== 'info' ? ' ' + type : ''}`;
    clearTimeout(_toastTimer);
    _toastTimer = setTimeout(() => el.classList.remove('show'), 4000);
}
