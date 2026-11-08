import { chromium, expect } from '../.tools/browser/node_modules/@playwright/test/index.mjs';
import { spawn } from 'node:child_process';
import fs from 'node:fs';
import net from 'node:net';
import path from 'node:path';
import { randomUUID } from 'node:crypto';

const root = path.resolve(import.meta.dirname, '..');
const output = path.join(root, 'verification/dialog-06');
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
    [path.join(root, '.tools/dialog-output/ForgeFlow.Web.dll'), '--urls', base], {
        cwd: path.join(root, 'src/ForgeFlow.Web'), stdio: ['ignore', log, log],
        env: { ...process.env, DOTNET_ROOT: path.join(root, '.tools/dotnet'),
            DOTNET_CLI_HOME: path.join(root, '.tools/cli-home'), NUGET_PACKAGES: path.join(root, '.tools/packages'),
            DOTNET_ENVIRONMENT: 'SmokeTest', ASPNETCORE_ENVIRONMENT: 'SmokeTest',
            ForgeFlow__Smoke__KeyDirectory: path.join(runDirectory, 'keys') }
    });
let browser;
const evidence = { environment: 'SmokeTest', checks: [], failures: [] };
try {
  for (let i=0;;i++) {
    try { if ((await fetch(base)).ok) break; } catch {}
    if (i>100) throw new Error('Server readiness failed');
    await new Promise(resolve=>setTimeout(resolve,200));
  }
  browser = await chromium.launch({channel:'msedge',headless:true,args:['--disable-gpu']});
  evidence.browser = browser.version();
  for (const culture of ['fa-IR','en-US']) for (const width of [1440,320]) {
    const context = await browser.newContext({locale:culture,viewport:{width,height:900}});
    await context.addCookies([{name:'.AspNetCore.Culture',value:`c=${culture}|uic=${culture}`,url:base}]);
    const page=await context.newPage();
    await page.goto(base);
    await expect(page.locator('#sample-name')).toBeEnabled({timeout:20000});
    await page.locator('#sample-name').fill('Synthetic dialog value');
    const opener=page.locator('#sample-dialog-open');
    await opener.focus(); await page.keyboard.press('Enter');
    const dialog=page.getByRole('dialog',{name:sampleResources[culture].DialogTitle});
    await expect(dialog).toBeVisible();
    await expect(dialog).toContainText('Synthetic dialog value');
    await expect(dialog).toContainText(sampleResources[culture].DialogContent);
    await expect.poll(()=>dialog.evaluate(el=>el.contains(document.activeElement))).toBe(true);
    const controls=dialog.locator('button:visible');
    await controls.last().focus(); await page.keyboard.press('Tab');
    await expect.poll(()=>dialog.evaluate(el=>el.contains(document.activeElement))).toBe(true);
    await controls.first().focus(); await page.keyboard.press('Shift+Tab');
    await expect.poll(()=>dialog.evaluate(el=>el.contains(document.activeElement))).toBe(true);
    const layout=await dialog.evaluate(el=>({dir:getComputedStyle(el).direction,left:el.getBoundingClientRect().left,right:el.getBoundingClientRect().right,scroll:document.documentElement.scrollWidth,width:innerWidth}));
    if(layout.dir!==(culture==='fa-IR'?'rtl':'ltr')||layout.left<0||layout.right>width||layout.scroll>width) throw new Error(JSON.stringify(layout));
    await page.screenshot({path:path.join(output,`dialog-${culture}-${width}.png`)});
    await page.keyboard.press('Escape'); await expect(dialog).toBeHidden();
    await expect(opener).toBeFocused();
    await opener.click(); await expect(dialog).toBeVisible();
    await dialog.getByRole('button',{name:sampleResources[culture].DialogClose}).last().click();
    await expect(dialog).toBeHidden(); await expect(opener).toBeFocused();
    await expect(page.locator('#sample-name')).toHaveValue('Synthetic dialog value');
    evidence.checks.push({culture,width,layout,keyboard:'passed',closeAndFocusRestoration:'passed'});
    await context.close();
  }
} catch(error) { evidence.failures.push(error.stack); process.exitCode=1; }
finally { await browser?.close(); server.kill(); fs.closeSync(log); fs.writeFileSync(path.join(output,'browser-results.json'),JSON.stringify(evidence,null,2)); }
console.log(JSON.stringify(evidence,null,2));