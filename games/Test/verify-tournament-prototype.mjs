import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { createRequire } from 'node:module';
import { cp, mkdir, mkdtemp, readFile, rm, writeFile } from 'node:fs/promises';
import { dirname, join, resolve } from 'node:path';
import { setTimeout as delay } from 'node:timers/promises';

const [devPanelDll, browserPath, outputPath] = process.argv.slice(2);
if (!outputPath) throw new Error('Pass the DevPanel DLL, Chromium executable, and a new evidence directory.');
const { chromium } = createRequire(import.meta.url)('playwright-core');
const output = resolve(outputPath);
await mkdir(output);
const temporary = await mkdtemp(join(output, 'workspace-'));
const game = join(temporary, 'games/Test');
const base = 'http://127.0.0.1:5188';
const api = base + '/api/prototypes/Test/tournament';
const command = resolve(devPanelDll);
const report = { passed: false };
let child, browser;

async function stop() {
    if (!child) return;
    if (child.exitCode === null && child.signalCode === null) child.kill();
    await child.finished;
    child = null;
}

async function start() {
    child = spawn('dotnet', [join(temporary, 'bin', 'AwithGameFrame.DevPanel.dll')], {
        windowsHide: true, cwd: temporary, stdio: ['ignore', 'ignore', 'pipe']
    });
    child.finished = new Promise((ok, fail) => child.once('error', fail).once('close', ok));
    child.finished.catch(() => {});
    let errors = '';
    child.stderr.on('data', part => { errors += part; });
    for (let attempt = 0; attempt < 70; attempt++) {
        if (child.exitCode !== null) throw new Error(errors || 'DevPanel exited early.');
        const result = await fetch(api, { signal: AbortSignal.timeout(450) }).catch(() => null);
        if (result?.ok) return;
        await delay(100);
    }
    throw new Error('DevPanel did not start.');
}

try {
    await mkdir(game, { recursive: true });
    await writeFile(join(temporary, 'AwithGameFrame.sln'), '');
    await cp(dirname(command), join(temporary, 'bin'), { recursive: true });
    await cp('tools/DevPanel/wwwroot', join(temporary, 'tools/DevPanel/wwwroot'), { recursive: true });
    await cp('games/Test/ui-prototypes', join(game, 'ui-prototypes'), { recursive: true });
    await cp('games/Test/game.json', join(game, 'game.json'));
    const profile = JSON.parse(await readFile(join(game, 'ui-prototypes/tournament.profile.json'), 'utf8'));
    for (const asset of [profile.font, ...profile.assets.map(item => item.path)]) {
        const destination = join(game, 'client/Unity', asset);
        await mkdir(dirname(destination), { recursive: true });
        await cp(join('games/Test/client/Unity', asset), destination);
    }
    const alreadyRunning = await fetch(base + '/api/scopes', { signal: AbortSignal.timeout(400) }).catch(() => null);
    if (alreadyRunning) throw new Error('Close the separately running DevPanel before running the isolated browser check.');
    await start();
    browser = await chromium.launch({ executablePath: resolve(browserPath), headless: true });
    const page = await browser.newPage({ viewport: { width: 1440, height: 1000 } });
    await page.goto(base);
    await page.locator('#scope').selectOption('games/Test');
    await page.getByRole('link', { name: 'UI 原型' }).click();
    await page.locator('#fixture').selectOption('lobby');
    await page.locator('#nodes').selectOption('ready');
    const width = page.locator('[data-property=width]');
    await width.fill('270');
    await page.locator('[data-property=text]').fill('Confirm ready');
    assert.equal(await page.locator('[data-node=ready]').textContent(), 'Confirm ready');
    await page.locator('#fixture').selectOption('results');
    await page.locator('#nodes').selectOption('backpack-icon');
    await page.locator('[data-property=asset]').selectOption('fish-slot');
    await page.locator('#save').click();
    await page.getByText('设计已保存').waitFor();
    await stop();
    await start();
    await page.reload();
    await page.locator('#fixture').selectOption('results');
    assert.match(await page.locator('[data-node=backpack-icon] img').getAttribute('src'), /fish-slot$/);
    await page.locator('[data-node=backpack-icon] img').evaluate(image => image.decode());
    await page.screenshot({ path: join(output, 'browser-results.png') });
    await page.locator('#fixture').selectOption('lobby');
    await page.locator('#nodes').selectOption('ready');
    assert.equal(await width.inputValue(), '270');
    assert.equal(await page.locator('[data-property=text]').inputValue(), 'Confirm ready');
    const saved = await (await fetch(api)).json();
    const duplicate = structuredClone(saved.document);
    duplicate.nodes.push(duplicate.nodes[0]);
    const response = await fetch(api, {
        method: 'POST', headers: { 'Content-Type': 'application/json', 'X-Awith-Panel': '1' },
        body: JSON.stringify({ revision: saved.revision, document: duplicate })
    });
    assert.equal(response.status, 400);
    assert.match((await response.json()).error, /duplicate node ID/);
    assert.equal((await (await fetch(api)).json()).revision, saved.revision);
    report.browserRestart = true;
    report.validationRejected = true;
    report.passed = true;
} catch (error) {
    report.error = String(error.stack || error);
    process.exitCode = 1;
} finally {
    if (browser) await browser.close();
    await stop();
    await rm(temporary, { recursive: true, force: true });
    await writeFile(join(output, 'report.json'), JSON.stringify(report, null, 2));
    console.log(report.passed ? 'TOURNAMENT_WEB_PASSED' : report.error);
}
