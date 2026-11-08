import { chromium, expect } from '../.tools/browser/node_modules/@playwright/test/index.mjs';
import { spawn } from 'node:child_process';
import fs from 'node:fs';
import net from 'node:net';
import path from 'node:path';
import { randomUUID } from 'node:crypto';

const root = path.resolve(import.meta.dirname, '..');
const output = path.join(root, 'verification/slice00-acceptance-05');
fs.mkdirSync(output, { recursive: true });
function readResource(resourceSet, culture) {
    const source = fs.readFileSync(path.join(root, `src/ForgeFlow.Web/Resources/Localization/${resourceSet}.${culture}.resx`), 'utf8');
    return Object.fromEntries([...source.matchAll(/<data name="([^"]+)"[^>]*><value>([^<]*)<\/value><\/data>/g)].map(match => [match[1], match[2]]));
}
const shared = { 'fa-IR': readResource('SharedResources', 'fa-IR'), 'en-US': readResource('SharedResources', 'en-US') };
const sample = { 'fa-IR': readResource('SampleResources', 'fa-IR'), 'en-US': readResource('SampleResources', 'en-US') };
const listener = net.createServer();
await new Promise(resolve => listener.listen(0, '127.0.0.1', resolve));
const port = listener.address().port;
await new Promise(resolve => listener.close(resolve));
const base = `http://127.0.0.1:${port}`;
const runDirectory = path.join(root, '.tools/smoke-runs', randomUUID());
fs.mkdirSync(runDirectory, { recursive: true });
const log = fs.openSync(path.join(output, 'server.log'), 'w');
const server = spawn(path.join(root, '.tools/dotnet/dotnet.exe'),
    [path.join(root, 'src/ForgeFlow.Web/bin/Release/net10.0/ForgeFlow.Web.dll'), '--urls', base], {
        cwd: path.join(root, 'src/ForgeFlow.Web'), stdio: ['ignore', log, log],
        env: { ...process.env, DOTNET_ROOT: path.join(root, '.tools/dotnet'),
            DOTNET_CLI_HOME: path.join(root, '.tools/cli-home'), NUGET_PACKAGES: path.join(root, '.tools/packages'),
            DOTNET_ENVIRONMENT: 'SmokeTest', ASPNETCORE_ENVIRONMENT: 'SmokeTest',
            ForgeFlow__Smoke__KeyDirectory: path.join(runDirectory, 'keys') }
    });
let browser;
const evidence = { environment: 'SmokeTest', browser: null, checks: [], layouts: [], screenshots: [], failures: [] };
function check(name, details = {}) { evidence.checks.push({ name, ...details }); }
async function ready(page, culture) {
    await expect(page.locator('#sample-name')).toBeEnabled({ timeout: 20000 });
    await expect(page.locator('html')).toHaveAttribute('lang', culture);
    await expect(page.locator('html')).toHaveAttribute('dir', culture === 'fa-IR' ? 'rtl' : 'ltr');
}
async function draftKeys(page) {
    return page.evaluate(() => Object.keys(sessionStorage).filter(key => key.startsWith('forgeflow.draft:')));
}
async function switchTo(page, culture) {
    await page.locator(culture === 'fa-IR' ? '#language-fa' : '#language-en').click();
    await ready(page, culture);
}

try {
    for (let attempt = 0; ; attempt++) {
        try { if ((await fetch(base)).ok) break; } catch { }
        if (attempt > 100 || server.exitCode !== null) throw new Error('SmokeTest server readiness failed');
        await new Promise(resolve => setTimeout(resolve, 200));
    }
    browser = await chromium.launch({ channel: 'msedge', headless: true, args: ['--disable-gpu'] });
    evidence.browser = `Microsoft Edge ${browser.version()}`;

    const keyboardContext = await browser.newContext({ locale: 'fa-IR', viewport: { width: 390, height: 844 } });
    const keyboardPage = await keyboardContext.newPage();
    await keyboardPage.goto(base);
    await ready(keyboardPage, 'fa-IR');
    const opener = keyboardPage.locator('.nav-drawer-toggle');
    const tabSequence = [];
    for (let attempt = 0; attempt < 14 && !(await opener.evaluate(el => el === document.activeElement)); attempt++) {
        await keyboardPage.keyboard.press('Tab');
        tabSequence.push(await keyboardPage.evaluate(() => ({ tag: document.activeElement?.tagName,
            id: document.activeElement?.id, className: document.activeElement?.className,
            label: document.activeElement?.getAttribute('aria-label') })));
    }
    if (!(await opener.evaluate(el => el === document.activeElement)))
        throw new Error(`Keyboard tab order did not reach the mobile drawer opener: ${JSON.stringify(tabSequence)}`);
    const openerFocus = await opener.evaluate(el => ({ visible: el.matches(':focus-visible'), outline: getComputedStyle(el).outlineStyle,
        outlineWidth: getComputedStyle(el).outlineWidth }));
    if (!openerFocus.visible || openerFocus.outline === 'none' || openerFocus.outlineWidth === '0px')
        throw new Error(`Drawer opener has no visible keyboard focus: ${JSON.stringify(openerFocus)}`);
    await keyboardPage.keyboard.press('Enter');
    const dialog = keyboardPage.getByRole('dialog', { name: shared['fa-IR'].Navigation });
    await expect(dialog).toBeVisible();
    await expect.poll(() => dialog.evaluate(el => el.contains(document.activeElement)), { timeout: 3000 }).toBe(true);
    const dialogFocusables = dialog.locator('button:visible, a[href]:visible, input:visible, textarea:visible, select:visible');
    const focusableCount = await dialogFocusables.count();
    if (focusableCount < 2) throw new Error('Drawer does not expose both its close control and navigation link');
    await dialogFocusables.last().focus();
    await keyboardPage.keyboard.press('Tab');
    if (!(await dialog.evaluate(el => el.contains(document.activeElement))))
        throw new Error('Tab escaped the last focusable control in the drawer');
    await keyboardPage.keyboard.press('Shift+Tab');
    if (!(await dialog.evaluate(el => el.contains(document.activeElement))))
        throw new Error('Shift+Tab escaped the first focusable control in the drawer');
    await keyboardPage.keyboard.press('Escape');
    await expect(dialog).toBeHidden();
    await expect(opener).toHaveAttribute('aria-expanded', 'false');
    await expect.poll(() => opener.evaluate(el => el === document.activeElement), { timeout: 3000 }).toBe(true);
    check('Keyboard focus, visible focus ring, drawer containment, Escape close, and opener focus restoration', { openerFocus });
    await keyboardContext.close();

    const preferenceContext = await browser.newContext({ locale: 'fa-IR', viewport: { width: 390, height: 844 } });
    const page = await preferenceContext.newPage();
    await page.goto(base);
    await ready(page, 'fa-IR');
    await page.locator('#sample-name').fill('Synthetic Slice00 نام');
    await page.locator('#sample-amount').fill('۱۲۳۴٫۵۰');
    await page.locator('#sample-notes').fill('Unsaved line one\nUnsaved line two');
    await page.locator('#sample-notes').focus();
    await switchTo(page, 'en-US');
    await expect(page.locator('#sample-name')).toHaveValue('Synthetic Slice00 نام');
    await expect(page.locator('#sample-amount')).toHaveValue('۱۲۳۴٫۵۰');
    await expect(page.locator('#sample-notes')).toHaveValue('Unsaved line one\nUnsaved line two');
    await expect(page.locator('#amount-preview')).toHaveText('1,234.50');
    await expect(page.locator('#sample-notes')).toBeFocused();
    await page.reload();
    await ready(page, 'en-US');
    await expect(page.locator('#sample-name')).toHaveValue('Synthetic Slice00 نام');
    const cultureCookie = (await preferenceContext.cookies()).find(cookie => cookie.name === '.AspNetCore.Culture');
    if (!cultureCookie?.httpOnly || cultureCookie.expires <= Date.now() / 1000)
        throw new Error('Language preference cookie is not persistent and HTTP-only');
    const freshTab = await preferenceContext.newPage();
    await freshTab.goto(base);
    await ready(freshTab, 'en-US');
    await expect(freshTab.locator('#sample-name')).toHaveValue('');
    await expect(freshTab.locator('#sample-amount')).toHaveValue('0');
    await switchTo(page, 'fa-IR');
    await expect(page.locator('#sample-name')).toHaveValue('Synthetic Slice00 نام');
    await expect(page.locator('#sample-amount')).toHaveValue('۱۲۳۴٫۵۰');
    await expect(page.locator('#sample-notes')).toHaveValue('Unsaved line one\nUnsaved line two');
    await expect(page.locator('#amount-preview')).not.toBeEmpty();
    await expect(page.locator('#sample-notes')).toBeFocused();
    check('FA -> EN -> FA preserves unsaved text/raw numeric input; preference survives reload and fresh tab; drafts remain tab scoped');

    await page.locator('#sample-name').fill('');
    await page.locator('#sample-amount').fill('12..');
    await page.locator('#sample-submit').click();
    await expect(page.locator('#name-error')).toHaveText(sample['fa-IR'].NameRequired);
    await expect(page.locator('#amount-error')).toHaveText(sample['fa-IR'].AmountInvalid);
    await switchTo(page, 'en-US');
    await expect(page.locator('#name-error')).toHaveText(sample['en-US'].NameRequired);
    await expect(page.locator('#amount-error')).toHaveText(sample['en-US'].AmountInvalid);
    await expect(page.locator('#sample-amount')).toHaveValue('12..');
    await switchTo(page, 'fa-IR');
    await expect(page.locator('#name-error')).toHaveText(sample['fa-IR'].NameRequired);
    await expect(page.locator('#amount-error')).toHaveText(sample['fa-IR'].AmountInvalid);
    check('Validation is recomputed and localized after each language switch');

    const secondTab = await preferenceContext.newPage();
    await secondTab.goto(base);
    await ready(secondTab, 'fa-IR');
    await expect(secondTab.locator('#sample-name')).toHaveValue('');
    await secondTab.locator('#sample-name').fill('Independent second tab');
    await secondTab.locator('#sample-amount').fill('7.25');
    const primaryKeys = await draftKeys(page);
    const secondKeys = await draftKeys(secondTab);
    if (primaryKeys.length !== 1 || secondKeys.length !== 1 || primaryKeys[0] === secondKeys[0])
        throw new Error('Tab-specific draft storage keys are missing or collide');
    await page.reload();
    await ready(page, 'fa-IR');
    await expect(page.locator('#sample-name')).toHaveValue('');
    await expect(page.locator('#sample-amount')).toHaveValue('12..');
    await expect(secondTab.locator('#sample-name')).toHaveValue('Independent second tab');
    check('Independent draft restoration across tabs');

    for (const damage of ['malformed', 'version', 'missing-field']) {
        await page.locator('#sample-name').fill('Synthetic rejected payload');
        await page.evaluate(async damage => {
            const key = Object.keys(sessionStorage).find(item => item.startsWith('forgeflow.draft:'));
            const saved = JSON.parse(sessionStorage.getItem(key));
            (await import('/js/drafts.mjs')).dispose();
            if (damage === 'malformed') sessionStorage.setItem(key, '{broken');
            else {
                if (damage === 'version') saved.version = 999;
                if (damage === 'missing-field') delete saved.snapshot.fields.Name;
                sessionStorage.setItem(key, JSON.stringify(saved));
            }
        }, damage);
        await page.reload();
        await ready(page, 'fa-IR');
        await expect(page.locator('#draft-status')).toHaveText(sample['fa-IR'].DraftRejected);
        await expect(page.locator('#sample-name')).toHaveValue('');
        if ((await draftKeys(page)).length) throw new Error(`${damage} payload was not removed`);
    }
    await expect(page.locator('#draft-status')).toHaveText(sample['fa-IR'].DraftRejected);
    check('Missing payload starts empty; malformed JSON, incompatible version, and incomplete schema are rejected and removed');

    await page.locator('#sample-name').fill('Synthetic successful submission');
    await page.locator('#sample-amount').fill('22.50');
    await page.locator('#sample-submit').click();
    await expect(page.locator('#submission-status')).toBeVisible();
    await expect(page.locator('#sample-name')).toHaveValue('');
    if ((await draftKeys(page)).length) throw new Error('Successful submission did not clear its draft');
    await page.reload();
    await ready(page, 'fa-IR');
    await expect(page.locator('#sample-name')).toHaveValue('');
    if ((await draftKeys(page)).length) throw new Error('Successful submission draft was recreated after reload');
    check('Draft clears after successful submission and stays cleared after reload');

    await secondTab.locator('#sample-discard').click();
    await expect(secondTab.locator('#draft-status')).toHaveText(sample['fa-IR'].DraftDiscarded);
    if ((await draftKeys(secondTab)).length) throw new Error('Explicit discard did not clear its draft');
    await secondTab.reload();
    await ready(secondTab, 'fa-IR');
    await expect(secondTab.locator('#sample-name')).toHaveValue('');
    if ((await draftKeys(secondTab)).length) throw new Error('Discarded draft was recreated after reload');
    check('Draft clears after explicit discard and stays cleared after reload');
    await preferenceContext.close();

    for (const culture of ['fa-IR', 'en-US']) {
      for (const width of [1440, 390, 320]) {
        const layoutContext = await browser.newContext({ locale: culture, viewport: { width, height: width === 1440 ? 1000 : 844 } });
        await layoutContext.addCookies([{ name: '.AspNetCore.Culture', value: `c=${culture}|uic=${culture}`,
            url: base, httpOnly: true, sameSite: 'Lax' }]);
        const layoutPage = await layoutContext.newPage();
        await layoutPage.goto(base);
        await ready(layoutPage, culture);
        if (width >= 1280) await expect(layoutPage.locator('.app-nav-panel')).toHaveCSS('opacity', '1');
        await expect(layoutPage.locator('h1')).toBeVisible();
        await expect(layoutPage.locator('#sample-name')).toBeVisible();
        const layout = await layoutPage.evaluate(() => {
            const direction = getComputedStyle(document.documentElement).direction;
            const heading = document.querySelector('h1');
            const controls = [...document.querySelectorAll('input:not([type=hidden]), textarea, button')];
            return {
                viewportWidth: innerWidth,
                documentWidth: document.documentElement.scrollWidth,
                direction,
                headingDirection: getComputedStyle(heading).direction,
                headingTextAlign: getComputedStyle(heading).textAlign,
                smallText: [...document.querySelectorAll('h1,label,button')].filter(element =>
                    parseFloat(getComputedStyle(element).fontSize) < 12).map(element => element.tagName),
                clippedControls: controls.filter(element => {
                    const bounds = element.getBoundingClientRect();
                    const style = getComputedStyle(element);
                    return style.visibility === 'visible' && style.display !== 'none' && bounds.width >= 1 && bounds.height >= 1 &&
                        (bounds.left < -1 || bounds.right > innerWidth + 1);
                }).map(element => ({ id: element.id, tag: element.tagName, label: element.getAttribute('aria-label'),
                    className: element.className, bounds: (() => { const bounds = element.getBoundingClientRect();
                        return { left: bounds.left, right: bounds.right, width: bounds.width }; })() }))
            };
        });
        if (layout.documentWidth > layout.viewportWidth || layout.direction !== (culture === 'fa-IR' ? 'rtl' : 'ltr') ||
            layout.headingDirection !== layout.direction || layout.headingTextAlign !== 'start' ||
            layout.smallText.length || layout.clippedControls.length)
            throw new Error(`${width}px readability/layout failed for ${culture}: ${JSON.stringify(layout)}`);
        const screenshot = `sample-${culture}-${width}.png`;
        await layoutPage.screenshot({ path: path.join(output, screenshot), fullPage: true });
        evidence.layouts.push({ culture, ...layout, screenshot });
        evidence.screenshots.push(path.join('verification/slice00-acceptance-05', screenshot));
        if (width === 390) {
            await layoutPage.getByRole('button', { name: shared[culture].OpenNavigation }).click();
            await expect(layoutPage.getByRole('dialog', { name: shared[culture].Navigation })).toBeVisible();
            await expect(layoutPage.locator('.app-nav-panel')).toHaveCSS('opacity', '1');
            const drawerScreenshot = `navigation-${culture}-390-open.png`;
            await layoutPage.screenshot({ path: path.join(output, drawerScreenshot), fullPage: false });
            evidence.screenshots.push(path.join('verification/slice00-acceptance-05', drawerScreenshot));
        }
        await layoutContext.close();
      }
    }
    check('320px, 390px, and 1440px Persian/RTL and English/LTR screenshots, readability, start alignment, control bounds, and horizontal overflow');
    evidence.status = 'passed';
} catch (error) {
    evidence.status = 'failed';
    evidence.error = error.stack ?? String(error);
    evidence.failures.push(error.stack ?? String(error));
    console.error(error);
    process.exitCode = 1;
} finally {
    await browser?.close();
    server.kill();
    fs.closeSync(log);
    fs.writeFileSync(path.join(output, 'browser-results.json'), JSON.stringify(evidence, null, 2));
}

if (evidence.status === 'passed') console.log(JSON.stringify(evidence, null, 2));
