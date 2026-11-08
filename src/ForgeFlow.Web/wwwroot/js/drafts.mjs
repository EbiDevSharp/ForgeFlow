const ttl = 30 * 60 * 1000;
const maximumBytes = 16384;
let active;

// Exported pure decoder is shared by browser storage and Node fixture tests.
export function decode(raw, definition, now = Date.now()) {
    if (raw === null) return { status: 'Missing', snapshot: null };
    try {
        if (raw.length > maximumBytes) throw new Error('size');
        const data = JSON.parse(raw);
        if (data.version !== definition.version || data.scope !== definition.scope ||
            !Number.isSafeInteger(data.savedAt) || data.savedAt > now + 60000 || now - data.savedAt > ttl) throw new Error('envelope');
        const snapshot = data.snapshot;
        const names = definition.fields.map(field => field.name);
        if (!snapshot || !snapshot.fields || Object.keys(snapshot.fields).length !== names.length ||
            typeof snapshot.validationAttempted !== 'boolean') throw new Error('shape');
        for (const field of definition.fields) {
            const value = snapshot.fields[field.name];
            if (!value || typeof value.raw !== 'string' || value.raw.length > field.maxLength ||
                Object.keys(value).some(key => !['raw', 'inputCulture'].includes(key))) throw new Error('field');
            if (['number', 'date'].includes(field.kind) && !definition.cultures.includes(value.inputCulture)) throw new Error('culture');
            if (!['number', 'date'].includes(field.kind) && value.inputCulture !== null) throw new Error('culture');
            if (field.allowedValues && (!Array.isArray(field.allowedValues) || !field.allowedValues.includes(value.raw))) throw new Error('value');
        }
        for (const key of ['dirty', 'touched']) {
            if (!Array.isArray(snapshot[key]) || snapshot[key].length > names.length ||
                snapshot[key].some(name => !names.includes(name))) throw new Error('metadata');
        }
        if (snapshot.focusId !== null && !definition.fields.some(field => field.id === snapshot.focusId)) throw new Error('focus');
        return { status: 'Restored', snapshot };
    } catch {
        return { status: 'Rejected', snapshot: null };
    }
}

function capture() {
    const fields = {};
    for (const field of active.definition.fields) {
        const element = active.form.querySelector(`#${CSS.escape(field.id)}`);
        fields[field.name] = { raw: element.value, inputCulture: ['number', 'date'].includes(field.kind) ? element.dataset.inputCulture : null };
    }
    return {
        fields, dirty: [...active.dirty], touched: [...active.touched],
        validationAttempted: active.attempted || active.form.dataset.validationAttempted === 'true',
        focusId: active.focusId
    };
}

function save() {
    if (active.cleared) return true;
    try {
        const snapshot = capture();
        const raw = JSON.stringify({ version: active.definition.version, scope: active.definition.scope, savedAt: Date.now(), snapshot });
        if (decode(raw, active.definition).status !== 'Restored') throw new Error('invalid');
        sessionStorage.setItem(active.key, raw);
        return true;
    } catch {
        active.callback.invokeMethodAsync('ReportStorageFailure').catch(() => {});
        return false;
    }
}

export function initialize(definition, callback) {
    dispose();
    const form = document.getElementById(definition.formId);
    active = { definition, form, callback, dirty: new Set(), touched: new Set(), focusId: null, attempted: false, cleared: true, listeners: [] };
    let result;
    try {
        let tab = sessionStorage.getItem('forgeflow.tab');
        if (!tab) { tab = crypto.randomUUID(); sessionStorage.setItem('forgeflow.tab', tab); }
        active.key = `forgeflow.draft:${definition.scope}:${definition.formId}:${tab}`;
        result = decode(sessionStorage.getItem(active.key), definition);
        if (result.status === 'Rejected') sessionStorage.removeItem(active.key);
        if (result.snapshot) {
            active.dirty = new Set(result.snapshot.dirty);
            active.touched = new Set(result.snapshot.touched);
            active.focusId = result.snapshot.focusId;
            active.attempted = result.snapshot.validationAttempted;
            active.cleared = false;
        }
    } catch { result = { status: 'Unavailable', snapshot: null }; }
    function listen(target, event, handler) {
        target.addEventListener(event, handler);
        active.listeners.push(() => target.removeEventListener(event, handler));
    }
    const recordFieldInput = event => {
        const field = definition.fields.find(field => field.id === event.target.id);
        if (!field) return;
        if (['number', 'date'].includes(field.kind)) event.target.dataset.inputCulture = document.documentElement.lang;
        active.cleared = false;
        active.dirty.add(field.name);
        save();
    };
    listen(form, 'input', event => { if (!['currency', 'enum'].includes(definition.fields.find(field => field.id === event.target.id)?.kind)) recordFieldInput(event); });
    listen(form, 'change', event => { if (['currency', 'enum'].includes(definition.fields.find(field => field.id === event.target.id)?.kind)) recordFieldInput(event); });
    listen(form, 'focusin', event => {
        const field = definition.fields.find(field => field.id === event.target.id);
        if (field) { active.focusId = field.id; if (!active.cleared) save(); }
    });
    listen(form, 'focusout', event => {
        const field = definition.fields.find(field => field.id === event.target.id);
        if (field) { active.touched.add(field.name); if (!active.cleared) save(); }
    });
    listen(form, 'submit', () => { active.attempted = true; active.cleared = false; save(); });
    listen(window, 'pagehide', () => { if (!active.cleared) save(); });
    return result;
}

export async function switchCulture(culture) {
    if (!active.definition.cultures.includes(culture)) return 'SwitchFailed';
    if (!save()) return 'DraftUnavailable';
    try {
        const token = active.form.querySelector('input[name="__RequestVerificationToken"]');
        const body = new FormData();
        body.set('__RequestVerificationToken', token.value);
        body.set('culture', culture);
        body.set('returnUrl', '/');
        const response = await fetch('/culture', { method: 'POST', credentials: 'same-origin', body });
        if (!response.ok) return 'SwitchFailed';
        window.location.assign('/');
        return 'Navigating';
    } catch { return 'SwitchFailed'; }
}

export function clear() {
    try {
        sessionStorage.removeItem(active.key);
        active.dirty.clear(); active.touched.clear(); active.attempted = false; active.cleared = true;
        return true;
    } catch { return false; }
}

export function restoreFocus(id) {
    if (active.definition.fields.some(field => field.id === id)) document.getElementById(id)?.focus();
}

export function dispose() {
    if (active) for (const remove of active.listeners) remove();
    active = null;
}
