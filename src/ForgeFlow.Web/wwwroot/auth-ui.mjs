const tokenPath = '/account/antiforgery';

export function clearDrafts() {
    try {
        for (let index = sessionStorage.length - 1; index >= 0; index--) {
            const key = sessionStorage.key(index);
            if (key?.startsWith('forgeflow.draft:')) sessionStorage.removeItem(key);
        }
    } catch { /* Storage may be disabled; server authorization remains authoritative. */ }
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
                clearDrafts();
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
            }
        }, true);

        document.addEventListener('click', async event => {
            const button = event.target.closest('[data-auth-culture]');
            if (!button) return;
            event.preventDefault();
            document.querySelectorAll('[data-auth-culture]').forEach(item => item.disabled = true);
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
                showNotice(document.documentElement.dataset.authLoginUnavailable || '');
            } catch {
                showNotice(document.documentElement.dataset.authLoginUnavailable || '');
            } finally {
                document.querySelectorAll('[data-auth-culture]').forEach(item => item.disabled = false);
            }
        }, true);
    }

    const activeForm = login || logout;
    if (activeForm && !activeForm.dataset.antiforgeryReady) {
        getRequestToken().then(() => activeForm.dataset.antiforgeryReady = 'true').catch(() => {
            const fallback = login?.dataset.unavailableMessage || document.documentElement.dataset.authLogoutUnavailable || '';
            showNotice(fallback);
        });
    }
}

document.addEventListener('DOMContentLoaded', bind);
document.addEventListener('enhancedload', bind);
