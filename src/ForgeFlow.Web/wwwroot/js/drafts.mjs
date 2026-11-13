const ttl = 30 * 60 * 1000;
const maximumBytes = 16384;
const ENVELOPE_PREFIX = 'ff-sec-draft:';
const SENSITIVE_PATTERN = /^(?=.*(password|secret|token|credential|auth|cvv|pin|card)).*$/i;

let active;

function isSensitive(field) {
    if (!field) return false;
    if (field.kind === 'password') return true;
    return SENSITIVE_PATTERN.test(field.name) || SENSITIVE_PATTERN.test(field.id);
}

function getTabKey() {
    try {
        let tab = sessionStorage.getItem('forgeflow.tab');
        if (!tab) {
            tab = (typeof crypto !== 'undefined' && crypto.randomUUID)
                ? crypto.randomUUID()
                : Math.random().toString(36).substring(2);
            sessionStorage.setItem('forgeflow.tab', tab);
        }
        return tab;
    } catch {
        return 'forgeflow-fallback-key';
    }
}

function scramble(text, key) {
    const encoder = new TextEncoder();
    const bytes = encoder.encode(text);
    const keyBytes = encoder.encode(key || 'ff-key');
    const out = new Uint8Array(bytes.length);
    for (let i = 0; i < bytes.length; i++) {
        out[i] = bytes[i] ^ keyBytes[i % keyBytes.length] ^ ((i * 31) & 0xFF);
    }
    let binary = '';
    for (let i = 0; i < out.length; i++) {
        binary += String.fromCharCode(out[i]);
    }
    return ENVELOPE_PREFIX + btoa(binary);
}

function unscramble(encoded, key) {
    if (!encoded || !encoded.startsWith(ENVELOPE_PREFIX)) {
        return encoded;
    }
    try {
        const base64 = encoded.slice(ENVELOPE_PREFIX.length);
        const binary = atob(base64);
        const bytes = new Uint8Array(binary.length);
        for (let i = 0; i < binary.length; i++) {
            bytes[i] = binary.charCodeAt(i);
        }
        const encoder = new TextEncoder();
        const keyBytes = encoder.encode(key || 'ff-key');
        const out = new Uint8Array(bytes.length);
        for (let i = 0; i < bytes.length; i++) {
            out[i] = bytes[i] ^ keyBytes[i % keyBytes.length] ^ ((i * 31) & 0xFF);
        }
        return new TextDecoder().decode(out);
    } catch {
        return null;
    }
}

// Exported pure decoder is shared by browser storage and Node fixture tests.
export function decode(raw, definition, now = Date.now()) {
    if (raw === null || raw === undefined) return { status: 'Missing', snapshot: null };
    try {
        if (typeof raw === 'string' && raw.startsWith(ENVELOPE_PREFIX)) {
            raw = unscramble(raw, getTabKey());
            if (raw === null) throw new Error('decrypt');
        }
        if (raw.length > maximumBytes) throw new Error('size');
        const data = JSON.parse(raw);
        if (data.version !== definition.version || data.scope !== definition.scope ||
            !Number.isSafeInteger(data.savedAt) || data.savedAt > now + 60000 || now - data.savedAt > ttl) throw new Error('envelope');
        const snapshot = data.snapshot;
        const nonSensitiveFields = definition.fields.filter(field => !isSensitive(field));
        const names = nonSensitiveFields.map(field => field.name);
        if (!snapshot || !snapshot.fields || Object.keys(snapshot.fields).length !== names.length ||
            typeof snapshot.validationAttempted !== 'boolean') throw new Error('shape');
        for (const field of nonSensitiveFields) {
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
        if (isSensitive(field)) continue;
        const element = active.form.querySelector(`#${CSS.escape(field.id)}`);
        fields[field.name] = { raw: element ? element.value : '', inputCulture: ['number', 'date'].includes(field.kind) ? element?.dataset?.inputCulture : null };
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
        const plain = JSON.stringify({ version: active.definition.version, scope: active.definition.scope, savedAt: Date.now(), snapshot });
        if (decode(plain, active.definition).status !== 'Restored') throw new Error('invalid');
        const protectedData = scramble(plain, getTabKey());
        sessionStorage.setItem(active.key, protectedData);
        return true;
    } catch {
        active.callback.invokeMethodAsync('ReportStorageFailure').catch(() => {});
        return false;
    }
}

export function purgeExpiredDrafts(now = Date.now()) {
    try {
        const key = getTabKey();
        for (let i = sessionStorage.length - 1; i >= 0; i--) {
            const k = sessionStorage.key(i);
            if (k && k.startsWith('forgeflow.draft:')) {
                const val = sessionStorage.getItem(k);
                if (!val) { sessionStorage.removeItem(k); continue; }
                const plain = val.startsWith(ENVELOPE_PREFIX) ? unscramble(val, key) : val;
                try {
                    const data = JSON.parse(plain);
                    if (!Number.isSafeInteger(data.savedAt) || now - data.savedAt > ttl) {
                        sessionStorage.removeItem(k);
                    }
                } catch {
                    sessionStorage.removeItem(k);
                }
            }
        }
    } catch { }
}

export function initialize(definition, callback) {
    dispose();
    purgeExpiredDrafts();
    const form = document.getElementById(definition.formId);
    active = { definition, form, callback, dirty: new Set(), touched: new Set(), focusId: null, attempted: false, cleared: true, listeners: [] };
    let result;
    try {
        const tab = getTabKey();
        active.key = `forgeflow.draft:${definition.scope}:${definition.formId}:${tab}`;
        const stored = sessionStorage.getItem(active.key);
        result = decode(stored, definition);
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
        if (!field || isSensitive(field)) return;
        if (['number', 'date'].includes(field.kind)) event.target.dataset.inputCulture = document.documentElement.lang;
        active.cleared = false;
        active.dirty.add(field.name);
        save();
    };
    listen(form, 'input', event => { if (!['currency', 'enum'].includes(definition.fields.find(field => field.id === event.target.id)?.kind)) recordFieldInput(event); });
    listen(form, 'change', event => { if (['currency', 'enum'].includes(definition.fields.find(field => field.id === event.target.id)?.kind)) recordFieldInput(event); });
    listen(form, 'focusin', event => {
        const field = definition.fields.find(field => field.id === event.target.id);
        if (field && !isSensitive(field)) { active.focusId = field.id; if (!active.cleared) save(); }
    });
    listen(form, 'focusout', event => {
        const field = definition.fields.find(field => field.id === event.target.id);
        if (field && !isSensitive(field)) { active.touched.add(field.name); if (!active.cleared) save(); }
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
    if (active.definition.fields.some(field => field.id === id && !isSensitive(field))) document.getElementById(id)?.focus();
}

export function dispose() {
    if (active) for (const remove of active.listeners) remove();
    active = null;
}
