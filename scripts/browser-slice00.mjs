// Dedicated browser fixtures use invented, non-sensitive values only.
import { chromium, expect } from '../.tools/browser/node_modules/@playwright/test/index.mjs';
import { spawn } from 'node:child_process';
import fs from 'node:fs';
import path from 'node:path';
import net from 'node:net';
import { randomUUID } from 'node:crypto';

const root = path.resolve(import.meta.dirname, '..');
const output = path.join(root, 'verification/switch-03');
fs.mkdirSync(output, { recursive: true });
function resources(culture) {
    const source = fs.readFileSync(path.join(root, `src/ForgeFlow.Web/Resources/Localization/SampleResources.${culture}.resx`), 'utf8');
    return Object.fromEntries([...source.matchAll(/<data name="([^"]+)"[^>]*><value>([^<]*)<\/value><\/data>/g)].map(match => [match[1], match[2]]));
}
const text = { 'fa-IR': resources('fa-IR'), 'en-US': resources('en-US') };
const available = net.createServer();
await new Promise(resolve => available.listen(0, '127.0.0.1', resolve));
const port = available.address().port;
await new Promise(resolve => available.close(resolve));
const base = `http://127.0.0.1:${port}`;
const runDirectory = path.join(root, '.tools/smoke-runs', randomUUID());
fs.mkdirSync(runDirectory, { recursive: true });
const log = fs.openSync(path.join(output, 'browser-server.log'), 'w');
const server = spawn(path.join(root, '.tools/dotnet/dotnet.exe'),
    [path.join(root, 'src/ForgeFlow.Web/bin/Release/net10.0/ForgeFlow.Web.dll'), '--urls', base], {
        cwd: path.join(root, 'src/ForgeFlow.Web'), stdio: ['ignore', log, log],
        env: { ...process.env, DOTNET_ROOT: path.join(root, '.tools/dotnet'),
            DOTNET_CLI_HOME: path.join(root, '.tools/cli-home'), NUGET_PACKAGES: path.join(root, '.tools/packages'),
            DOTNET_ENVIRONMENT: 'SmokeTest', ASPNETCORE_ENVIRONMENT: 'SmokeTest', ForgeFlow__Smoke__KeyDirectory: path.join(runDirectory, 'keys') }
    });
let browser;
const evidence = { environment: 'SmokeTest', chromiumSandbox: true, checks: [], layouts: [], pageErrors: [] };
function passed(name) { evidence.checks.push(name); console.log(`PASS ${name}`); }
async function ready(page, culture) {
    await expect(page.locator('html')).toHaveAttribute('lang', culture);
    await expect(page.locator('html')).toHaveAttribute('dir', culture === 'fa-IR' ? 'rtl' : 'ltr');
    await expect(page.locator('#sample-name')).toBeEnabled({ timeout: 20000 });
}
async function switchTo(page, culture) {
    await page.locator(culture === 'fa-IR' ? '#language-fa' : '#language-en').click();
    await ready(page, culture);
}
async function draftKeys(page) { return page.evaluate(() => Object.keys(sessionStorage).filter(key => key.startsWith('forgeflow.draft:'))); }
try {
    for (let attempt = 0; ; attempt++) {
        try { if ((await fetch(base)).ok) break; } catch { }
        if (attempt > 100 || server.exitCode !== null) throw new Error('Server readiness failed');
        await new Promise(resolve => setTimeout(resolve, 200));
    }
    browser = await chromium.launch({ channel: 'chrome', headless: true, chromiumSandbox: true });
    evidence.browser = `Chrome ${browser.version()}`;
    const context = await browser.newContext({ locale: 'fa-IR', viewport: { width: 1440, height: 1000 } });
    context.on('page', page => page.on('pageerror', error => evidence.pageErrors.push(error.message)));
    const page = await context.newPage();
    await page.goto(base); await ready(page, 'fa-IR');
    await page.locator('#sample-name').fill('Synthetic نام 03');
    await page.locator('#sample-amount').fill('۱۲۳۴٫۵۰');
    await page.locator('#sample-notes').fill('Invented notes\nیادداشت ساختگی');
    await switchTo(page, 'en-US');
    await expect(page.locator('#sample-name')).toHaveValue('Synthetic نام 03');
    await expect(page.locator('#sample-amount')).toHaveValue('۱۲۳۴٫۵۰');
    await expect(page.locator('#sample-notes')).toHaveValue('Invented notes\nیادداشت ساختگی');
    await expect(page.locator('#amount-preview')).toHaveText('1,234.50');
    await expect(page.locator('#sample-notes')).toBeFocused();
    await switchTo(page, 'fa-IR');
    await expect(page.locator('#sample-amount')).toHaveValue('۱۲۳۴٫۵۰');
    await expect(page.locator('#sample-notes')).toBeFocused();
    await page.reload(); await ready(page, 'fa-IR');
    await expect(page.locator('#sample-name')).toHaveValue('Synthetic نام 03');
    const cookie = (await context.cookies()).find(cookie => cookie.name === '.AspNetCore.Culture');
    if (!cookie?.httpOnly || cookie.expires <= Date.now() / 1000) throw new Error('Persistent culture cookie missing');
    passed('FA -> EN -> FA preserves mixed-script text, multiline notes, raw Persian number, canonical formatting, focus, and preference on reload');

    await page.locator('#sample-name').fill('');
    await page.locator('#sample-amount').fill('12..');
    await page.locator('#sample-submit').click();
    await expect(page.locator('#name-error')).toHaveText(text['fa-IR'].NameRequired);
    await expect(page.locator('#amount-error')).toHaveText(text['fa-IR'].AmountInvalid);
    await switchTo(page, 'en-US');
    await expect(page.locator('#name-error')).toHaveText(text['en-US'].NameRequired);
    await expect(page.locator('#amount-error')).toHaveText(text['en-US'].AmountInvalid);
    await expect(page.locator('#sample-amount')).toHaveValue('12..');
    await switchTo(page, 'fa-IR');
    await expect(page.locator('#amount-error')).toHaveText(text['fa-IR'].AmountInvalid);
    passed('Invalid raw number survives and validation is recomputed in each selected language');

    const other = await context.newPage(); await other.goto(base); await ready(other, 'fa-IR');
    await other.locator('#sample-name').fill('Other synthetic tab');
    await other.locator('#sample-amount').fill('7.25');
    await switchTo(other, 'en-US');
    await page.reload(); await ready(page, 'en-US');
    await expect(page.locator('#sample-name')).toHaveValue('');
    await expect(page.locator('#sample-amount')).toHaveValue('12..');
    await expect(other.locator('#sample-name')).toHaveValue('Other synthetic tab');
    if ((await draftKeys(page))[0] === (await draftKeys(other))[0]) throw new Error('Tab identifiers collided');
    passed('Separate tabs retain independent drafts while sharing the browser language preference');

    await page.locator('#sample-name').fill('Successful synthetic sample');
    await page.locator('#sample-amount').fill('22.50');
    await page.locator('#sample-submit').click();
    await expect(page.locator('#submission-status')).toBeVisible();
    await expect(page.locator('#sample-name')).toHaveValue('');
    if ((await draftKeys(page)).length) throw new Error('Submission left a draft');
    await page.reload(); await ready(page, 'en-US');
    await expect(page.locator('#sample-name')).toHaveValue('');
    if ((await draftKeys(page)).length) throw new Error('Submission draft resurrected on reload');
    await other.locator('#sample-discard').click();
    await expect(other.locator('#draft-status')).toHaveText(text['en-US'].DraftDiscarded);
    if ((await draftKeys(other)).length) throw new Error('Discard left a draft');
    await other.reload(); await ready(other, 'en-US');
    await expect(other.locator('#sample-name')).toHaveValue('');
    passed('Successful submission and explicit discard clear drafts without pagehide resurrection');

    for (const damage of ['malformed', 'version', 'expired']) {
        await page.locator('#sample-name').fill('Disposable synthetic fixture');
        await page.evaluate(async damage => {
            const key = Object.keys(sessionStorage).find(key => key.startsWith('forgeflow.draft:'));
            const saved = JSON.parse(sessionStorage.getItem(key));
            (await import('/js/drafts.mjs')).dispose();
            if (damage === 'version') saved.version = 999;
            if (damage === 'expired') saved.savedAt = 0;
            sessionStorage.setItem(key, damage === 'malformed' ? '{broken' : JSON.stringify(saved));
        }, damage);
        await page.reload(); await ready(page, 'en-US');
        await expect(page.locator('#draft-status')).toHaveText(text['en-US'].DraftRejected);
        await expect(page.locator('#sample-name')).toHaveValue('');
        if ((await draftKeys(page)).length) throw new Error(`${damage} draft was not removed`);
    }
    passed('Missing, malformed, incompatible-version, and expired drafts are handled without breaking the form');

    await page.locator('#sample-name').fill('Failure fixture');
    await page.evaluate(() => {
        window.fixtureSetItem = Storage.prototype.setItem;
        Storage.prototype.setItem = function () { throw new DOMException('Fixture blocked storage', 'QuotaExceededError'); };
    });
    await page.locator('#language-fa').click();
    await expect(page.locator('#draft-status')).toHaveText(text['en-US'].DraftUnavailable);
    await expect(page.locator('html')).toHaveAttribute('lang', 'en-US');
    await expect(page.locator('#sample-name')).toHaveValue('Failure fixture');
    await page.evaluate(() => { Storage.prototype.setItem = window.fixtureSetItem; delete window.fixtureSetItem; });
    await page.route('**/culture', route => route.abort('failed'));
    await page.locator('#language-fa').click();
    await expect(page.locator('#draft-status')).toHaveText(text['en-US'].SwitchFailed);
    await expect(page.locator('#sample-name')).toBeEnabled();
    await page.unroute('**/culture');
    passed('Storage and network failures retain usable form values and do not reload');

    const rejected = await page.evaluate(async () => {
        const send = async (culture, returnUrl, token) => {
            const body = new FormData(); body.set('culture', culture); body.set('returnUrl', returnUrl);
            if (token) body.set('__RequestVerificationToken', document.querySelector('input[name="__RequestVerificationToken"]').value);
            return (await fetch('/culture', { method: 'POST', body })).status;
        };
        return [await send('fa-IR', '/', false), await send('xx-XX', '/', true), await send('fa-IR', 'https://example.invalid/', true)];
    });
    if (rejected.some(status => status !== 400)) throw new Error('Unsafe culture request was accepted');
    await page.reload(); await ready(page, 'en-US');
    passed('Culture endpoint rejects missing antiforgery tokens, unsupported cultures, and nonlocal return routes');

    for (const width of [1440, 390, 320]) {
        await page.setViewportSize({ width, height: width === 1440 ? 1000 : 844 });
        for (const culture of ['fa-IR', 'en-US']) {
            await page.locator('#sample-name').fill('Layout sample نام');
            await page.locator('#sample-amount').fill('1234.50');
            await page.locator('#sample-notes').fill('Invented layout fixture\nیادداشت ساختگی');
            await switchTo(page, culture);
            await expect(page.locator('#sample-notes')).toBeFocused();
            await page.locator('#sample-name').focus();
            await page.keyboard.press('Tab'); await expect(page.locator('#sample-amount')).toBeFocused();
            await page.keyboard.press('Tab'); await expect(page.locator('#sample-notes')).toBeFocused();
            await page.keyboard.press('Tab'); await expect(page.locator('#sample-submit')).toBeFocused();
            const layout = await page.evaluate(() => ({ width: innerWidth, scrollWidth: document.documentElement.scrollWidth,
                direction: getComputedStyle(document.documentElement).direction,
                focusOutline: getComputedStyle(document.activeElement).outlineStyle,
                clipped: [...document.querySelectorAll('input:not([type=hidden]),textarea,button')].filter(element => {
                    const bounds = element.getBoundingClientRect(); return bounds.left < -1 || bounds.right > innerWidth + 1;
                }).map(element => element.id) }));
            if (layout.scrollWidth > layout.width || layout.clipped.length || layout.focusOutline === 'none') throw new Error(`Layout failed: ${JSON.stringify(layout)}`);
            const screenshot = `sample-${culture}-${width}.png`;
            await page.screenshot({ path: path.join(output, screenshot), fullPage: true });
            evidence.layouts.push({ culture, ...layout, screenshot });
        }
    }
    passed('Both languages at desktop 1440px, mobile 390px, and 320px: keyboard order, restored focus, visible focus ring, and no horizontal overflow');
    if (evidence.pageErrors.length) throw new Error(`Browser exceptions: ${evidence.pageErrors.join('; ')}`);
    evidence.status = 'passed';
} catch (error) {
    evidence.status = 'failed'; evidence.error = error.stack; console.error(error); process.exitCode = 1;
} finally {
    await browser?.close(); server.kill(); fs.closeSync(log);
    fs.writeFileSync(path.join(output, 'browser-results.json'), JSON.stringify(evidence, null, 2));
}
