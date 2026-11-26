import { createReadStream } from 'node:fs';
import { mkdir, realpath, stat, writeFile } from 'node:fs/promises';
import { createServer } from 'node:http';
import { createRequire } from 'node:module';
import { extname, isAbsolute, join, relative, resolve } from 'node:path';
import { parseArgs } from 'node:util';

const { values: options } = parseArgs({
    options: {
        player: { type: 'string' },
        browser: { type: 'string' },
        output: { type: 'string' }
    }
});
if (!options.player || !options.browser || !options.output) {
    throw new Error('Pass --player <WebGL directory> --browser <Chromium executable> --output <new evidence directory>.');
}

const require = createRequire(new URL('../../artifacts/UnityWebGLTools/package.json', import.meta.url));
const { chromium } = require('playwright-core');
const player = await realpath(options.player);
if (!(await stat(join(player, 'index.html'))).isFile()) throw new Error('Missing WebGL index.html.');
const output = resolve(options.output);
await mkdir(output);

const mime = {
    '.html': 'text/html; charset=utf-8',
    '.js': 'application/javascript',
    '.wasm': 'application/wasm',
    '.css': 'text/css; charset=utf-8',
    '.png': 'image/png',
    '.svg': 'image/svg+xml',
    '.ico': 'image/x-icon',
    '.json': 'application/json'
};
const server = createServer(async (request, response) => {
    try {
        const url = new URL(request.url, 'http://127.0.0.1');
        const path = await realpath(resolve(player, '.' + decodeURIComponent(url.pathname === '/' ? '/index.html' : url.pathname)));
        const pathInPlayer = relative(player, path);
        if (!pathInPlayer || pathInPlayer.startsWith('..') || isAbsolute(pathInPlayer)) {
            response.writeHead(404).end();
            return;
        }
        const info = await stat(path);
        if (!info.isFile()) {
            response.writeHead(404).end();
            return;
        }
        const suffix = extname(path);
        const encoding = suffix === '.gz' ? 'gzip' : suffix === '.br' ? 'br' : undefined;
        const extension = encoding ? extname(path.slice(0, -suffix.length)) : suffix;
        response.setHeader('Content-Type', mime[extension] ?? 'application/octet-stream');
        response.setHeader('Content-Length', info.size);
        if (encoding) response.setHeader('Content-Encoding', encoding);
        const stream = createReadStream(path);
        stream.on('error', () => response.destroy());
        response.on('close', () => stream.destroy());
        stream.pipe(response);
    } catch (error) {
        response.writeHead(error.code === 'ENOENT' ? 404 : 500).end();
    }
});
await new Promise((done, fail) => server.once('error', fail).listen(0, '127.0.0.1', done));

const report = { player, passed: false, runtimeVerified: false, messages: [], errors: [] };
let browser;
try {
    browser = await chromium.launch({ executablePath: resolve(options.browser), headless: true, args: [
        '--use-angle=swiftshader', '--enable-unsafe-swiftshader'
    ] });
    report.browserVersion = browser.version();
    const page = await browser.newPage({ viewport: { width: 1080, height: 720 } });
    page.on('pageerror', error => report.errors.push(error.message));
    page.on('console', message => {
        report.messages.push({ type: message.type(), text: message.text() });
        if (message.type() === 'error') report.errors.push(message.text());
    });
    page.on('requestfailed', request => report.errors.push(request.method() + ' ' + request.url() + ' ' + request.failure()?.errorText));
    page.on('dialog', async dialog => {
        report.errors.push(dialog.type() + ' ' + dialog.message());
        await dialog.dismiss();
    });
    const url = new URL('http://127.0.0.1:' + server.address().port + '/');
    await page.goto(url.href);
    await page.locator('#unity-canvas').waitFor({ state: 'visible' });
    await page.locator('#unity-loading-bar').waitFor({ state: 'hidden', timeout: 90000 });
    const canvas = page.locator('#unity-canvas');
    const box = await canvas.boundingBox();
    if (!box || box.width !== 960 || box.height !== 600) {
        throw new Error('Expected the authored 960 × 600 WebGL canvas.');
    }
    const capture = async name => {
        await canvas.screenshot({ path: join(output, name + '.png') });
        (report.steps ??= []).push(name);
    };
    const waitForHeading = async (name, x, y, width, height, minimumBrightPixels = 100) => {
        const deadline = Date.now() + 30000;
        do {
            const png = await canvas.screenshot();
            const bright = await page.evaluate(async ({ data, x, y, width, height }) => {
                const bytes = Uint8Array.from(atob(data), char => char.charCodeAt(0));
                const image = await createImageBitmap(new Blob([bytes], { type: 'image/png' }), x, y, width, height);
                const context = new OffscreenCanvas(width, height).getContext('2d');
                context.drawImage(image, 0, 0);
                const pixels = context.getImageData(0, 0, width, height).data;
                let count = 0;
                for (let index = 0; index < pixels.length; index += 4)
                    if (pixels[index] > 150 && pixels[index + 1] > 150 && pixels[index + 2] > 150) count++;
                return count;
            }, { data: png.toString('base64'), x, y, width, height });
            if (bright > minimumBrightPixels) {
                await writeFile(join(output, name + '.png'), png);
                (report.steps ??= []).push(name);
                return;
            }
            await page.waitForTimeout(250);
        } while (Date.now() < deadline);
        await canvas.screenshot({ path: join(output, name + '-timeout.png') });
        throw new Error('Heading did not render: ' + name);
    };
    const click = async (x, y) => page.mouse.click(box.x + x, box.y + y);
    const nextRouteReady = () => page.waitForEvent('console', {
        predicate: message => message.text().includes('Rain Pass stage=route-ready map=Cedar Grove boss=Cedar Sentinel'),
        timeout: 25000
    });
    await waitForHeading('01-camp', 125, 150, 710, 75);
    await click(581, 355);
    await waitForHeading('02-expedition', 185, 65, 420, 45);
    await Promise.all([nextRouteReady(), click(480, 292)]);
    await page.waitForTimeout(600);
    await capture('03-cedar-grove-training');
    await page.keyboard.press('Escape');
    await waitForHeading('04-paused', 125, 159, 133, 49, 180);
    await click(662, 413);
    await page.waitForTimeout(1200);
    await waitForHeading('05-returned-to-camp', 125, 150, 710, 75);
    await click(581, 355);
    await waitForHeading('06-reopened-expedition', 185, 65, 420, 45);
    await Promise.all([nextRouteReady(), click(480, 292)]);
    await page.waitForTimeout(600);
    await capture('07-cedar-grove-training-again');
    report.passed = report.errors.length === 0;
    report.runtimeVerified = report.passed;
} catch (error) {
    report.errors.push(error.message);
} finally {
    try { await browser?.close(); }
    finally { server.closeAllConnections(); server.close(); }
    await writeFile(join(output, 'report.json'), JSON.stringify(report, null, 2) + '\n');
}
console.log((report.passed ? 'WEBGL_SMOKE_PASSED ' : 'WEBGL_SMOKE_FAILED ') + join(output, 'report.json'));
if (!report.passed) process.exitCode = 1;
