import { chromium, expect } from '../.tools/browser/node_modules/@playwright/test/index.mjs';
import { spawn } from 'node:child_process';
import fs from 'node:fs';
import net from 'node:net';
import path from 'node:path';
import { randomUUID } from 'node:crypto';

const root = path.resolve(import.meta.dirname, '..');
const output = path.join(root, 'verification/navigation-04');
fs.mkdirSync(output, { recursive: true });
function readResource(resourceSet, culture) {
    const source = fs.readFileSync(path.join(root, `src/ForgeFlow.Web/Resources/Localization/${resourceSet}.${culture}.resx`), 'utf8');
    return Object.fromEntries([...source.matchAll(/<data name="([^"]+)"[^>]*><value>([^<]*)<\/value><\/data>/g)].map(match => [match[1], match[2]]));
}
const resources = { 'fa-IR': readResource('SharedResources', 'fa-IR'), 'en-US': readResource('SharedResources', 'en-US') };
const sampleResources = { 'fa-IR': readResource('SampleResources', 'fa-IR'), 'en-US': readResource('SampleResources', 'en-US') };
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
const evidence = { browser: null, checks: [], failures: [] };
try {
    for (let attempt = 0; ; attempt++) {
        try { if ((await fetch(base)).ok) break; } catch { }
        if (attempt > 100 || server.exitCode !== null) throw new Error('SmokeTest server readiness failed');
        await new Promise(resolve => setTimeout(resolve, 200));
    }
    browser = await chromium.launch({ channel: 'msedge', headless: true, args: ['--disable-gpu'] });
    evidence.browser = `Microsoft Edge ${browser.version()}`;

    for (const width of [1440, 390]) {
        for (const culture of ['fa-IR', 'en-US']) {
            const context = await browser.newContext({ locale: culture, viewport: { width, height: 900 } });
            await context.addCookies([{ name: '.AspNetCore.Culture', value: `c=${culture}|uic=${culture}`,
                url: base, httpOnly: true, sameSite: 'Lax' }]);
            const page = await context.newPage();
            const pageErrors = [];
            page.on('pageerror', error => pageErrors.push(error.message));
            await page.goto(base);
            await expect(page.locator('#sample-name')).toBeEnabled({ timeout: 20000 });
            await expect(page.locator('html')).toHaveAttribute('lang', culture);
            await expect(page.locator('html')).toHaveAttribute('dir', culture === 'fa-IR' ? 'rtl' : 'ltr');

            const nav = page.getByRole('navigation', { name: resources[culture].Navigation });
            const item = page.locator('.app-nav-panel nav a[href="/"]');
            await expect(item).toHaveAttribute('href', '/');
            await expect(item).toHaveAttribute('aria-current', 'page');

            const panel = page.locator('.app-nav-panel');
            await expect(panel).toHaveAttribute('id', 'primary-navigation');
            const toggle = page.getByRole('button', { name: resources[culture].OpenNavigation });
            if (width >= 1280) {
                await expect(nav).toBeVisible();
                await expect(item).toBeVisible();
                await expect(page.getByText(resources[culture].AppTitle, { exact: true })).toBeVisible();
                await expect(panel).toBeVisible();
                const panelBounds = await panel.boundingBox();
                if (!panelBounds || panelBounds.width < 1) throw new Error('Desktop navigation panel has no visible width');
                if (culture === 'fa-IR' ? panelBounds.x < width / 2 : panelBounds.x > width / 2)
                    throw new Error(`Desktop panel did not follow the document direction for ${culture}`);
                if (await toggle.isVisible()) throw new Error(`Mobile drawer toggle is visible at ${width}px`);
            } else {
                await expect(toggle).toBeVisible();
                await expect(toggle).toHaveAttribute('aria-controls', 'primary-navigation');
                await expect(toggle).toHaveAttribute('aria-expanded', 'false');
                await toggle.click();
                await expect(toggle).toHaveAttribute('aria-expanded', 'true');
                const dialog = page.getByRole('dialog', { name: resources[culture].Navigation });
                await expect(dialog).toBeVisible();
                await expect(nav).toBeVisible();
                await expect(page.getByText(resources[culture].AppTitle, { exact: true })).toBeVisible();
                await expect(page.getByRole('button', { name: resources[culture].CloseNavigation })).toBeVisible();
                await expect(item).toBeVisible();
                const drawerBounds = await panel.boundingBox();
                if (!drawerBounds || (culture === 'fa-IR' ? drawerBounds.x < width / 2 : drawerBounds.x > width / 2))
                    throw new Error(`Drawer did not open from the logical start edge for ${culture}`);
                await page.getByRole('button', { name: resources[culture].CloseNavigation }).click();
                await expect(toggle).toHaveAttribute('aria-expanded', 'false');
            }

            if (pageErrors.length) throw new Error(pageErrors.join('\n'));
            evidence.checks.push({ culture, direction: culture === 'fa-IR' ? 'rtl' : 'ltr', width, nav: 'localized', activeRoute: true });
            await context.close();
        }
    }

    const context = await browser.newContext({ locale: 'fa-IR', viewport: { width: 1440, height: 900 } });
    const page = await context.newPage();
    await page.goto(base);
    await expect(page.locator('#sample-name')).toBeEnabled({ timeout: 20000 });
    await page.locator('#sample-name').fill('Synthetic navigation draft');
    await page.locator('#sample-amount').fill('12..');
    await page.locator('#sample-notes').fill('Draft line one\nDraft line two');
    await page.locator('#sample-submit').click();
    await expect(page.locator('#amount-error')).toHaveText(sampleResources['fa-IR'].AmountInvalid);
    await page.locator('#language-en').click();
    await expect(page.locator('html')).toHaveAttribute('lang', 'en-US');
    await expect(page.locator('html')).toHaveAttribute('dir', 'ltr');
    await expect(page.locator('#sample-name')).toHaveValue('Synthetic navigation draft');
    await expect(page.locator('#sample-amount')).toHaveValue('12..');
    await expect(page.locator('#sample-notes')).toHaveValue('Draft line one\nDraft line two');
    await expect(page.locator('#amount-error')).toHaveText(sampleResources['en-US'].AmountInvalid);
    await expect(page.getByRole('link', { name: resources['en-US'].SampleNavigation })).toHaveAttribute('aria-current', 'page');
    evidence.checks.push({ cultureSwitch: 'fa-IR to en-US', draftValuesAndLocalizedValidationPreserved: true, activeRoutePreserved: true });
    await context.close();
} catch (error) {
    evidence.failures.push(error.stack ?? String(error));
} finally {
    await browser?.close();
    server.kill();
    fs.closeSync(log);
    fs.writeFileSync(path.join(output, 'browser-results.json'), JSON.stringify(evidence, null, 2));
}

if (evidence.failures.length) {
    console.error(evidence.failures.join('\n'));
    process.exitCode = 1;
} else {
    console.log(JSON.stringify(evidence, null, 2));
}
