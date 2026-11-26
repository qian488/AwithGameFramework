import { spawn } from 'node:child_process';
import { once } from 'node:events';
import { createReadStream, createWriteStream } from 'node:fs';
import { mkdir, mkdtemp, readFile, realpath, stat, writeFile } from 'node:fs/promises';
import { createServer } from 'node:http';
import { createServer as createTcpServer } from 'node:net';
import { dirname, isAbsolute, join, relative, resolve, sep } from 'node:path';
import { pipeline } from 'node:stream/promises';
import { fileURLToPath } from 'node:url';
import { setTimeout as delay } from 'node:timers/promises';
import { parseArgs } from 'node:util';

const { positionals, values } = parseArgs({
    allowPositionals: true,
    options: { scores: { type: 'string' }, route: { type: 'string', default: 'salt-causeway' } }
});
const [playerPath, publicationPath, evidencePath] = positionals;
const contractId = values.route;
if (positionals.length !== 3 || !['salt-causeway', 'ember-foundry'].includes(contractId)) {
    throw new Error('Usage: node games/Test/verify-expedition.mjs <Player.exe> <publicationDir> <newEvidenceDir> [--scores <Test.Server.dll>] [--route <salt-causeway|ember-foundry>]');
}
const upgrade = contractId === 'salt-causeway';
const player = await realpath(playerPath);
const publication = await realpath(publicationPath);
const scoreAssembly = values.scores ? await realpath(values.scores) : null;
const evidence = resolve(evidencePath);
await mkdir(evidence);
const profile = await mkdtemp(fileURLToPath(new URL('../../artifacts/ExpeditionProfile-', import.meta.url)));

const report = {
    scenario: 'rain-pass-expedition', passed: false, player, publication, profile, contractId, upgrade,
    stages: {}, requests: []
};
let contentServer;
let scoreServer;
let activeVersion = 'v1';

async function readText(path) {
    try { return await readFile(path, 'utf8'); }
    catch (error) {
        if (error.code === 'ENOENT') return null;
        throw error;
    }
}

async function listen(server) {
    const listening = once(server, 'listening');
    server.listen(0, '127.0.0.1');
    await listening;
    return 'http://127.0.0.1:' + server.address().port + '/';
}

function stopContent() {
    if (!contentServer?.listening) return;
    return new Promise((done, fail) => {
        contentServer.close(error => error ? fail(error) : done());
        contentServer.closeAllConnections();
    });
}

async function startContent() {
    const offers = JSON.parse(await readFile(join(publication, 'index.json'), 'utf8'));
    const offer = offers.find(candidate => candidate.id === contractId);
    if (!offer?.title) throw new Error('Publication index must include ' + contractId + '.');
    for (const version of upgrade ? ['v1', 'v2'] : ['v1']) {
        const path = join(publication, contractId, version, 'content-manifest.json');
        const manifest = JSON.parse(await readFile(path, 'utf8'));
        if (manifest.version !== version) throw new Error('Unexpected version in ' + path);
    }

    contentServer = createServer(async (request, response) => {
        try {
            const pathname = decodeURIComponent(new URL(request.url, report.feed).pathname).slice(1);
            const entry = { pathname, activeVersion, range: request.headers.range ?? null };
            report.requests.push(entry);
            response.once('finish', () => { entry.status = response.statusCode; });
            if (request.method !== 'GET') {
                response.writeHead(405).end();
                return;
            }
            if (pathname === 'index.json') {
                const bytes = Buffer.from(JSON.stringify([{ ...offer, version: activeVersion }]));
                response.writeHead(200, { 'Content-Type': 'application/json', 'Content-Length': bytes.length });
                response.end(bytes);
                return;
            }
            const [id, version] = pathname.split('/');
            if (id !== contractId || (version !== 'v1' && version !== activeVersion)) {
                response.writeHead(404).end();
                return;
            }
            const file = await realpath(resolve(publication, pathname));
            const fromPublication = relative(publication, file);
            if (!fromPublication || fromPublication === '..' || fromPublication.startsWith('..' + sep) ||
                isAbsolute(fromPublication)) {
                response.writeHead(404).end();
                return;
            }
            const fileInfo = await stat(file);
            if (!fileInfo.isFile()) {
                response.writeHead(404).end();
                return;
            }
            const requestedRange = request.headers.range;
            const range = requestedRange?.match(/^bytes=(\d+)-$/);
            const start = range ? Number(range[1]) : 0;
            if (requestedRange && (!range || start >= fileInfo.size)) {
                response.writeHead(416, { 'Content-Range': 'bytes */' + fileInfo.size }).end();
                return;
            }
            const headers = { 'Accept-Ranges': 'bytes', 'Content-Length': fileInfo.size - start };
            if (range) headers['Content-Range'] = 'bytes ' + start + '-' + (fileInfo.size - 1) + '/' + fileInfo.size;
            response.writeHead(range ? 206 : 200, headers);
            await pipeline(createReadStream(file, { start }), response);
        } catch (error) {
            if (!response.headersSent && !response.destroyed) {
                const status = ['ENOENT', 'ENOTDIR'].includes(error.code) ? 404 : error instanceof URIError ? 400 : 500;
                response.writeHead(status).end();
            } else {
                response.destroy();
            }
        }
    });
    report.feed = await listen(contentServer);
}

function launch(executable, args, options) {
    const child = spawn(executable, args, { windowsHide: true, ...options });
    let outcome;
    child.once('error', error => { outcome = { error }; });
    const closed = new Promise(done => child.once('close', (code, signal) => {
        outcome ??= { code, signal };
        done(outcome);
    }));
    return {
        outcome: () => outcome,
        async stop() {
            if (!outcome) child.kill();
            return closed;
        }
    };
}

async function startScores() {
    const reservation = createTcpServer();
    const url = await listen(reservation);
    await new Promise((done, fail) => reservation.close(error => error ? fail(error) : done()));
    const config = join(evidence, 'ScoreServer.json');
    const log = join(evidence, 'ScoreServer.log');
    await writeFile(config, JSON.stringify({
        ScorePort: Number(new URL(url).port), ScoreDirectory: join(evidence, 'Scores')
    }, null, 2));
    // Redirect both streams to the same file without waiting for the server to exit to see startup errors.
    const output = createWriteStream(log, { flags: 'wx' });
    await once(output, 'open');
    try {
        scoreServer = launch('dotnet', [scoreAssembly, '--config', config], { stdio: ['ignore', output.fd, output.fd] });
    } finally {
        output.end();
    }
    const deadline = Date.now() + 15_000;
    while (Date.now() < deadline) {
        const outcome = scoreServer.outcome();
        if (outcome?.error) throw outcome.error;
        if (outcome) break;
        const response = await fetch(url + 'contract-scores/recovery?after=0', {
            signal: AbortSignal.timeout(1_000)
        }).catch(() => null);
        if (response?.ok) {
            await response.arrayBuffer();
            report.scoreUrl = url;
            return;
        }
        await response?.body?.cancel();
        await delay(100);
    }
    throw new Error('Score server did not start. See ' + log);
}

async function runStage(stage) {
    const output = join(evidence, stage);
    const log = join(output, 'Player.log');
    const feedListening = contentServer.listening;
    await mkdir(output);
    const result = report.stages[stage] = { passed: false, output, log, feedListening };
    const args = [
        '-batchmode', '-nographics', '-logFile', log,
        '-expedition-evidence', output, '-expedition-stage', stage,
        '-expedition-route', contractId,
        '-contract-data', profile, '-contract-feed', report.feed
    ];
    if (stage === 'sync') args.push('-contract-leaderboard', report.scoreUrl, '-contract-scores', report.scoreUrl);
    const process = launch(player, args, { cwd: dirname(player), stdio: 'ignore' });
    const failure = join(output, 'failure.txt');
    const deadline = Date.now() + 160_000;
    try {
        while (!process.outcome() && Date.now() < deadline) {
            const message = await readText(failure);
            if (message !== null) throw new Error(message.trim() || 'Player wrote failure.txt.');
            if (upgrade && stage === 'play' && activeVersion === 'v1' && await readText(join(output, 'publish-v2')) !== null) {
                activeVersion = 'v2';
                await writeFile(join(output, 'v2-available'), '');
            }
            await delay(100);
        }
        const outcome = process.outcome();
        if (!outcome) throw new Error('Player timed out.');
        if (outcome.error) throw outcome.error;
        result.exitCode = outcome.code;
        const message = await readText(failure);
        if (message !== null) throw new Error(message.trim() || 'Player wrote failure.txt.');
        if (outcome.code !== 0 || await readText(join(output, 'passed.txt')) === null) {
            throw new Error('Expected passed.txt and exit 0, received exit ' + (outcome.signal ?? outcome.code) + '.');
        }
        result.passed = true;
    } catch (error) {
        throw new Error(stage + ': ' + error.message + ' See ' + output, { cause: error });
    } finally {
        const outcome = await process.stop();
        result.exitCode ??= outcome.code;
        const playerLog = await readText(log);
        const renderer = playerLog?.match(/^\s*(?:NullGfxDevice:.*|Renderer:\s*Null Device\b.*)$/m)?.[0].trim();
        result.noGraphics = Boolean(renderer);
        result.renderer = renderer ?? null;
        result.unityVersion = playerLog?.match(/Initialize engine version:\s*(\S+)/)?.[1] ?? null;
    }
    if (!result.noGraphics) {
        result.passed = false;
        throw new Error(stage + ': Unity did not report a null graphics device. See ' + log);
    }
}

try {
    await startContent();
    await runStage('play');
    await stopContent();
    await runStage('resume');
    if (scoreAssembly) {
        await startScores();
        await runStage('sync');
    }
    report.passed = true;
} catch (error) {
    report.error = error.message;
    throw error;
} finally {
    await Promise.all([stopContent(), scoreServer?.stop()]);
    await writeFile(join(evidence, 'summary.json'), JSON.stringify(report, null, 2) + '\n');
}
console.log('EXPEDITION_PASSED ' + join(evidence, 'summary.json'));
