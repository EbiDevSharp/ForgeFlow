const tokenPath = '/account/antiforgery';
const identityChannelName = 'forgeflow.identity';
const identityChannel = typeof BroadcastChannel === 'function' ? new BroadcastChannel(identityChannelName) : null;

export function clearDrafts() {
    try {
        for (let index = sessionStorage.length - 1; index >= 0; index--) {
            const key = sessionStorage.key(index);
            if (key?.startsWith('forgeflow.draft:')) sessionStorage.removeItem(key);
        }
    } catch { /* Storage may be disabled; server authorization remains authoritative. */ }
}

export function applyThemePreference(mode) {
    return window.__forgeflowTheme?.apply?.(mode) || 'light';
}

export function previewThemePreference(mode) {
    return window.__forgeflowTheme?.preview?.(mode) || 'light';
}

export function restoreThemePreference() {
    return window.__forgeflowTheme?.restore?.() || 'light';
}

export function unregisterGlobalShortcuts() {
    window.__forgeflowShortcutTarget = null;
}

export function registerGlobalShortcuts(dotNetRef) {
    window.__forgeflowShortcutTarget = dotNetRef;
    if (document.documentElement.dataset.shortcutsBound) return;
    document.documentElement.dataset.shortcutsBound = 'true';
    document.addEventListener('keydown', event => {
        const target = window.__forgeflowShortcutTarget;
        if (!target) return;

        if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 'k') {
            event.preventDefault();
            target.invokeMethodAsync('OpenCommandPaletteFromJs').catch(() => {});
            return;
        }

        const active = document.activeElement;
        const editing = active && (active.tagName === 'INPUT' || active.tagName === 'TEXTAREA' || active.tagName === 'SELECT' || active.isContentEditable);
        if (editing || event.ctrlKey || event.metaKey || event.altKey) return;

        if (event.key === '?') {
            event.preventDefault();
            target.invokeMethodAsync('OpenShortcutsHelpFromJs').catch(() => {});
        } else if (event.key === '/') {
            const searchInput = document.querySelector('main input[type="search"]:not([disabled])');
            event.preventDefault();
            if (searchInput instanceof HTMLElement) {
                searchInput.focus();
            } else {
                target.invokeMethodAsync('OpenCommandPaletteFromJs').catch(() => {});
            }
        }
    });
}

export async function applyCultureCookie(culture) {
    try {
        await getRequestToken();
        const requestToken = document.querySelector('input[name="__RequestVerificationToken"]')?.value;
        const body = new URLSearchParams({ culture, returnUrl: '/', __RequestVerificationToken: requestToken || '' });
        const response = await fetch('/culture', {
            method: 'POST', body, credentials: 'same-origin', headers: { 'Content-Type': 'application/x-www-form-urlencoded' }, cache: 'no-store'
        });
        return response.status === 204 || response.ok;
    } catch {
        return false;
    }
}

export function notifyIdentityCleared() {
    clearDrafts();
    try {
        identityChannel?.postMessage({ type: 'signed-out', nonce: crypto.randomUUID?.() || String(Date.now()) });
    } catch { /* Cross-tab channel is best-effort cleanup; server session state remains authoritative. */ }
}

if (identityChannel) {
    identityChannel.addEventListener('message', event => {
        if (event.data?.type !== 'signed-out') return;
        clearDrafts();
        if (document.querySelector('[data-authenticated-content]')) {
            document.querySelectorAll('[data-authenticated-content]').forEach(node => node.hidden = true);
            location.assign('/');
        }
    });
}

async function getRequestToken() {
    const response = await fetch(tokenPath, { credentials: 'same-origin', cache: 'no-store' });
    if (!response.ok) throw new Error('antiforgery-token-unavailable');
    const body = await response.json();
    if (typeof body.requestToken !== 'string' || body.requestToken.length === 0) {
        throw new Error('antiforgery-token-unavailable');
    }
    document.querySelectorAll('input[name="__RequestVerificationToken"]').forEach(input => {
        input.value = body.requestToken;
    });
}

function showNotice(message) {
    const notice = document.querySelector('#auth-ui-notice');
    if (!notice) return;
    notice.textContent = message;
    notice.hidden = false;
}

async function responseMessage(response, fallback) {
    try {
        const body = await response.json();
        return typeof body.message === 'string' ? body.message : fallback;
    } catch {
        return fallback;
    }
}

function setBusy(form, busy) {
    const button = form.querySelector('[data-auth-submit]');
    if (!button) return;
    button.disabled = busy;
    button.setAttribute('aria-busy', String(busy));
    const loading = button.querySelector('[data-auth-loading]');
    if (loading) loading.hidden = !busy;
}

function bind() {
    const login = document.querySelector('[data-auth-login]');
    const logout = document.querySelector('[data-auth-logout]');
    const changePassword = document.querySelector('[data-auth-change-password]');
    const notice = document.querySelector('#auth-ui-notice');
    if (notice) {
        const message = sessionStorage.getItem('auth-ui-notice');
        if (message) {
            sessionStorage.removeItem('auth-ui-notice');
            showNotice(message);
        }
    }

    if (!document.documentElement.dataset.authUiBound) {
        document.documentElement.dataset.authUiBound = 'true';
        document.addEventListener('submit', async event => {
            const form = event.target;
            if (!(form instanceof HTMLFormElement)) return;
            if (form.matches('[data-auth-login]')) {
                event.preventDefault();
                const username = form.elements.namedItem('username');
                const password = form.elements.namedItem('password');
                const error = document.querySelector('#login-error');
                const errorText = error?.querySelector('[data-auth-error-message]');
                const fail = message => {
                    if (errorText) errorText.textContent = message;
                    if (error) error.hidden = false;
                    (username?.value ? password : username)?.focus();
                    if (password) password.value = '';
                    setBusy(form, false);
                };
                if (!username?.value.trim() || !password?.value) {
                    fail(form.dataset.requiredMessage || form.dataset.unavailableMessage || '');
                    return;
                }
                if (error) error.hidden = true;
                setBusy(form, true);
                try {
                    if (!form.elements.namedItem('__RequestVerificationToken')?.value) await getRequestToken();
                    const response = await fetch(form.action, {
                        method: 'POST', body: new FormData(form), credentials: 'same-origin', redirect: 'follow', cache: 'no-store'
                    });
                    if (response.redirected && response.ok && new URL(response.url).origin === location.origin) {
                        clearDrafts();
                        location.assign(response.url);
                        return;
                    }
                    fail(await responseMessage(response, form.dataset.unavailableMessage || ''));
                } catch {
                    fail(form.dataset.unavailableMessage || '');
                }
            } else if (form.matches('[data-auth-logout]')) {
                event.preventDefault();
                notifyIdentityCleared();
                setBusy(form, true);
                document.querySelectorAll('[data-authenticated-content]').forEach(node => node.hidden = true);
                try {
                    if (!form.elements.namedItem('__RequestVerificationToken')?.value) await getRequestToken();
                    const response = await fetch(form.action, {
                        method: 'POST', body: new FormData(form), credentials: 'same-origin', redirect: 'follow', cache: 'no-store'
                    });
                    if (response.redirected && response.ok && new URL(response.url).origin === location.origin) {
                        location.assign(response.url);
                        return;
                    }
                    const message = await responseMessage(response, document.documentElement.dataset.authLogoutUnavailable || '');
                    sessionStorage.setItem('auth-ui-notice', message);
                    location.assign('/');
                } catch {
                    const message = document.documentElement.dataset.authLogoutUnavailable || '';
                    sessionStorage.setItem('auth-ui-notice', message);
                    location.assign('/');
                }
            } else if (form.matches('[data-auth-change-password]')) {
                event.preventDefault();
                const currentPwd = form.elements.namedItem('currentPassword');
                const newPwd = form.elements.namedItem('newPassword');
                const confirmPwd = form.elements.namedItem('confirmPassword');
                const error = document.querySelector('#change-pwd-error');
                const errorText = error?.querySelector('[data-auth-error-message]');
                const success = document.querySelector('#change-pwd-success');
                const sessionsLink = document.querySelector('#change-pwd-sessions-link');
                const fail = message => {
                    if (success) success.hidden = true;
                    if (sessionsLink) sessionsLink.hidden = true;
                    if (errorText) errorText.textContent = message;
                    if (error) error.hidden = false;
                    setBusy(form, false);
                };
                if (!currentPwd?.value || !newPwd?.value || !confirmPwd?.value) {
                    fail(form.dataset.requiredMessage || form.dataset.unavailableMessage || '');
                    return;
                }
                if (newPwd.value !== confirmPwd.value) {
                    fail(form.dataset.mismatchMessage || form.dataset.unavailableMessage || '');
                    return;
                }
                if (error) error.hidden = true;
                if (success) success.hidden = true;
                if (sessionsLink) sessionsLink.hidden = true;
                setBusy(form, true);
                try {
                    await getRequestToken();
                    const response = await fetch(form.action, {
                        method: 'POST', body: new FormData(form), credentials: 'same-origin', cache: 'no-store'
                    });
                    if (response.status === 401) {
                        location.assign('/account/login');
                        return;
                    }
                    if (response.ok) {
                        const data = await response.json();
                        if (currentPwd) currentPwd.value = '';
                        if (newPwd) newPwd.value = '';
                        if (confirmPwd) confirmPwd.value = '';
                        if (success && typeof data.message === 'string' && data.message) {
                            success.textContent = data.message;
                            success.hidden = false;
                        }
                        if (sessionsLink) sessionsLink.hidden = false;
                        await getRequestToken();
                        setBusy(form, false);
                        return;
                    }
                    fail(await responseMessage(response, form.dataset.unavailableMessage || ''));
                } catch {
                    fail(form.dataset.unavailableMessage || '');
                }
            }
        }, true);

        document.addEventListener('click', async event => {
            const reconnectButton = event.target.closest('[data-reconnect-action]');
            if (reconnectButton) {
                event.preventDefault();
                const action = reconnectButton.dataset.reconnectAction;
                if (action === 'reload') {
                    location.reload();
                    return;
                }
                reconnectButton.disabled = true;
                try {
                    const ok = action === 'resume'
                        ? await window.Blazor?.resumeCircuit?.()
                        : (await window.Blazor?.reconnect?.()) || (await window.Blazor?.resumeCircuit?.());
                    if (!ok) location.reload();
                } catch {
                    location.reload();
                } finally {
                    reconnectButton.disabled = false;
                }
                return;
            }
            const button = event.target.closest('[data-auth-culture]');
            if (!button) return;
            event.preventDefault();
            document.querySelectorAll('[data-auth-culture]').forEach(item => item.disabled = true);
            const fallback = document.documentElement.dataset.serverUnavailable || document.documentElement.dataset.authLoginUnavailable || '';
            try {
                const token = document.querySelector('input[name="__RequestVerificationToken"]')?.value;
                if (!token) await getRequestToken();
                const requestToken = document.querySelector('input[name="__RequestVerificationToken"]')?.value;
                const body = new URLSearchParams({ culture: button.dataset.authCulture, returnUrl: '/', __RequestVerificationToken: requestToken || '' });
                const response = await fetch('/culture', {
                    method: 'POST', body, credentials: 'same-origin', headers: { 'Content-Type': 'application/x-www-form-urlencoded' }, cache: 'no-store'
                });
                if (response.status === 204) {
                    location.reload();
                    return;
                }
                if (response.ok) {
                    const data = await response.json();
                    if (data?.cookieApplied) {
                        if (typeof data.message === 'string' && data.message)
                            sessionStorage.setItem('auth-ui-notice', data.message);
                        location.reload();
                        return;
                    }
                }
                showNotice(fallback);
            } catch {
                showNotice(fallback);
            } finally {
                document.querySelectorAll('[data-auth-culture]').forEach(item => item.disabled = false);
            }
        }, true);
    }

    const activeForm = login || logout || changePassword;
    if (activeForm && !activeForm.dataset.antiforgeryReady) {
        getRequestToken().then(() => activeForm.dataset.antiforgeryReady = 'true').catch(() => {
            const fallback = login?.dataset.unavailableMessage || document.documentElement.dataset.authLogoutUnavailable || '';
            showNotice(fallback);
        });
    }
}

document.addEventListener('DOMContentLoaded', bind);
document.addEventListener('enhancedload', bind);
