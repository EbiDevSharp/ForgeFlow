// Targeted SmokeTest verification; synthetic credentials, intercepted failed login, no SQL.
const { chromium } = require('../../.tools/browser/node_modules/playwright');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const crypto = require('node:crypto');
const output = __dirname;
const result = { environment: 'SmokeTest HTTP, synthetic failure interception; no SQL/authentication proof', cases: [] };

(async () => {
    const browser = await chromium.launch({ channel: 'chrome', headless: true });
    try {
        for (const culture of ['fa-IR', 'en-US']) {
            for (const width of [1440, 320]) {
                const context = await browser.newContext({ viewport: { width, height: 1000 }, locale: culture });
                const page = await context.newPage();
                const entry = { culture, width };
                try {
                    await page.goto('http://127.0.0.1:5107/');
                    await page.waitForURL('**/account/login?returnUrl=%2F');
                    await page.waitForSelector('html[data-auth-ui-bound="true"]');
                    assert.equal(await page.locator('html').getAttribute('lang'), culture);
                    assert.equal(await page.locator('html').getAttribute('dir'), culture === 'fa-IR' ? 'rtl' : 'ltr');
                    assert.equal(await page.locator('.app-nav-panel').count(), 0);
                    const font = await page.request.get('http://127.0.0.1:5107/fonts/Vazirmatn.woff2');
                    assert.equal(font.status(), 200);
                    assert.equal(crypto.createHash('sha256').update(await font.body()).digest('hex'), '4e3fa217d38fdafc1fea4414ceb58ca5e662cf0ab5fa735a8c8c20e8b42cad92');
                    assert.equal(await page.evaluate(async () => {
                        await document.fonts.load('400 16px Vazirmatn', 'ForgeFlow فارسی');
                        await document.fonts.load('700 28px Vazirmatn', 'ForgeFlow فارسی');
                        await document.fonts.ready;
                        await new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)));
                        return document.fonts.check('400 16px Vazirmatn', 'ForgeFlow فارسی') &&
                            [...document.fonts].some(font => font.family === 'Vazirmatn' && font.status === 'loaded');
                    }), true);
                    const cdp = await context.newCDPSession(page);
                    await cdp.send('DOM.enable'); await cdp.send('CSS.enable');
                    const documentNode = (await cdp.send('DOM.getDocument')).root.nodeId;
                    const headingNode = (await cdp.send('DOM.querySelector', { nodeId: documentNode, selector: '#login-title' })).nodeId;
                    const platformFonts = await cdp.send('CSS.getPlatformFontsForNode', { nodeId: headingNode });
                    entry.headingFonts = platformFonts.fonts;
                    assert(platformFonts.fonts.some(font => font.isCustomFont && font.familyName.includes('Vazirmatn')));
                    entry.fontLoaded = true;
                    assert.equal(await page.locator('[data-auth-submit]').evaluate(node => getComputedStyle(node).fontFamily.startsWith('Vazirmatn')), true);
                    assert.equal(await page.locator('#login-error').isVisible(), false);
                    assert.equal(await page.locator('[data-auth-loading]').isVisible(), false);

                    async function checkShape(state) {
                        const shapes = await page.locator('.login-field-group, .login-field-input, .login-field-reveal').evaluateAll(nodes => nodes.map(node => {
                            const style = getComputedStyle(node);
                            return [style.borderTopLeftRadius, style.borderTopRightRadius, style.borderBottomLeftRadius, style.borderBottomRightRadius];
                        }));
                        for (const corners of shapes) assert.deepEqual(corners, ['8px', '8px', '8px', '8px'], state);
                        const heights = await page.locator('.login-field-group').evaluateAll(nodes => nodes.map(node => node.getBoundingClientRect().height));
                        assert(heights.every(height => height === 48), `${state}: field height`);
                        entry.states ??= []; entry.states.push(state);
                    }
                    await checkShape('default');
                    await page.locator('input[name="username"]').hover(); await checkShape('hover');
                    await page.locator('input[name="username"]').click(); await checkShape('focus');
                    await page.keyboard.press('Tab'); await checkShape('keyboard focus');
                    const focusVisible = await page.evaluate(() => {
                        const input = document.activeElement;
                        const group = input.closest('.login-field-group');
                        return !!group && parseFloat(getComputedStyle(group).outlineWidth) > 0;
                    });
                    assert(focusVisible);
                    await page.locator('input[name="password"]').fill(crypto.randomUUID());
                    await page.locator('.login-field-reveal').click();
                    await page.waitForFunction(() => document.querySelector('input[name="password"]').type === 'text');
                    await checkShape('password reveal');
                    await page.locator('.login-field-reveal').click();
                    await page.waitForFunction(() => document.querySelector('input[name="password"]').type === 'password');
                    await page.locator('input[name="username"]').evaluate(node => {
                        node.setCustomValidity('synthetic-invalid-state'); node.setAttribute('aria-invalid', 'true'); node.closest('.login-field').classList.add('bit-inv');
                    });
                    await checkShape('synthetic invalid');
                    await page.locator('input[name="username"]').evaluate(node => { node.setCustomValidity(''); node.removeAttribute('aria-invalid'); node.closest('.login-field').classList.remove('bit-inv'); });
                    const inputNode = (await cdp.send('DOM.querySelector', { nodeId: documentNode, selector: 'input[name="username"]' })).nodeId;
                    await cdp.send('CSS.forcePseudoState', { nodeId: inputNode, forcedPseudoClasses: ['autofill'] });
                    entry.autofillPseudoMatched = await page.locator('input[name="username"]').evaluate(node => node.matches(':autofill'));
                    await checkShape('CDP autofill state');
                    await cdp.send('CSS.forcePseudoState', { nodeId: inputNode, forcedPseudoClasses: [] });

                    let release, seen;
                    const gate = new Promise(resolve => release = resolve);
                    const requestSeen = new Promise(resolve => seen = resolve);
                    const catalog = fs.readFileSync(path.join(__dirname, '../../src/ForgeFlow.Web/Resources/Authentication/AuthenticationResources.' + culture + '.resx'), 'utf8');
                    const message = catalog.match(/<data name="Auth.LoginRejected"[^>]*><value>(.*?)<\/value>/s)[1];
                    await page.route('**/account/sign-in', async route => {
                        assert.equal(route.request().method(), 'POST');
                        const body = route.request().postData() || '';
                        assert(body.includes('name="username"') && body.includes('name="password"') && body.includes('name="__RequestVerificationToken"'));
                        seen(); await gate;
                        await route.fulfill({ status: 401, contentType: 'application/json', body: JSON.stringify({ code: 'Auth.LoginRejected', message }) });
                    });
                    await page.locator('input[name="username"]').fill('synthetic.operator');
                    await page.locator('input[name="password"]').fill(crypto.randomUUID());
                    await page.locator('[data-auth-submit]').click();
                    await requestSeen;
                    assert.equal(await page.locator('[data-auth-submit]').isDisabled(), true);
                    assert.equal(await page.locator('[data-auth-loading]').isVisible(), true);
                    release();
                    await page.waitForSelector('#login-error:not([hidden])');
                    assert.equal(await page.locator('#login-error').getAttribute('role'), 'alert');
                    assert.equal((await page.locator('#login-error').innerText()).trim(), message);
                    assert.equal(await page.locator('#login-error h1, #login-error h2, #login-error [class*="bit-"]').count(), 0);
                    assert.deepEqual(await page.locator('[data-auth-error-message]').evaluate(node => ({ color: getComputedStyle(node).color, background: getComputedStyle(node).backgroundColor })), { color: 'rgb(143, 32, 39)', background: 'rgba(0, 0, 0, 0)' });
                    assert.equal(await page.locator('[data-auth-submit]').isDisabled(), false);
                    assert.equal(await page.locator('[data-auth-loading]').isVisible(), false);
                    assert.equal(await page.locator('input[name="password"]').inputValue(), '');
                    assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), true);
                    entry.syntheticPostAndFailure = true; entry.noOverflow = true;
                    await page.screenshot({ path: path.join(output, `${culture}-${width}.png`), fullPage: true });
                    entry.pass = true;
                } finally { result.cases.push(entry); await context.close(); }
            }
        }
    } finally { await browser.close(); }
})().catch(error => { result.blocker = error.message; process.exitCode = 1; }).finally(() => {
    fs.writeFileSync(path.join(output, 'browser-results.json'), JSON.stringify(result, null, 2));
    console.log(JSON.stringify(result));
});
