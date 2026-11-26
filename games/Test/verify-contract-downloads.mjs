import { spawn } from 'node:child_process';
import { once } from 'node:events';
import { copyFile, mkdir, mkdtemp, readFile, realpath, stat, writeFile } from 'node:fs/promises';
import { createServer } from 'node:http';
import { createServer as createTcpServer } from 'node:net';
import { dirname, join, resolve, sep } from 'node:path';
import { fileURLToPath } from 'node:url';
import { setTimeout as delay } from 'node:timers/promises';

const [playerPath, publicationPath, legacySave, evidencePath, mode, scoreServerPath] = process.argv.slice(2);
const settings = mode === '--settings';
const scores = mode === '--scores';
if (!evidencePath || !legacySave || !publicationPath || !playerPath)
    throw new Error('Pass the Windows Player, publication directory, legacy slot-1.save and a new evidence directory.');
const player = await realpath(playerPath);
const boot = await readFile(join(dirname(player), 'Test_Data', 'boot.config'), 'utf8');
if (/^player-connection-mode=/m.test(boot)) throw new Error('Build the verification Player with -ReleasePlayer.');
const publication = await realpath(publicationPath);
const scoreServer = scores ? await realpath(scoreServerPath) : null;
const evidence = resolve(evidencePath);
await mkdir(evidence);
const data = await mkdtemp(fileURLToPath(new URL('../../artifacts/ContractData-', import.meta.url)));
const saves = join(data, 'RainPassContract');
await mkdir(saves, { recursive: true });
await copyFile(legacySave, join(saves, 'slot-1.save'));

const offers = JSON.parse(await readFile(join(publication, 'index.json'), 'utf8'));
const salt = offers.find(offer => offer.id === 'salt-causeway');
const saltV2 = 'salt-causeway/v2/salt-causeway-v2-scene';
const requests = [];
let phase = settings ? 'healthy' : 'initial';
let attempts = 0;
const server = createServer(async (request, response) => {
    try {
        const pathname = decodeURIComponent(new URL(request.url, 'http://127.0.0.1').pathname).slice(1);
        const entry = { pathname, range: request.headers.range ?? null, phase };
        requests.push(entry);
        if (pathname === 'index.json') {
            response.setHeader('Content-Type', 'application/json');
            response.end(JSON.stringify(phase === 'initial' ? [{ ...salt, version: 'v1' }] : offers));
            return;
        }
        const file = await realpath(resolve(publication, pathname));
        if (!file.startsWith(publication + sep) || !(await stat(file)).isFile()) {
            response.writeHead(404).end();
            return;
        }
        const bytes = await readFile(file);
        const from = request.headers.range?.match(/^bytes=(\d+)-$/);
        const start = from ? Number(from[1]) : 0;
        const payload = bytes.subarray(start);
        if (start >= bytes.length) {
            response.writeHead(416).end();
            return;
        }
        response.setHeader('Content-Length', payload.length);
        response.setHeader('Accept-Ranges', 'bytes');
        if (from) {
            response.statusCode = 206;
            response.setHeader('Content-Range', `bytes ${start}-${bytes.length - 1}/${bytes.length}`);
        }
        if (phase === 'faults' && pathname === saltV2) {
            attempts++;
            if (attempts === 1) {
                entry.fault = 'interrupted';
                response.write(payload.subarray(0, Math.floor(payload.length / 2)));
                await delay(500);
                response.destroy();
                return;
            }
            entry.fault = 'corrupt';
            payload[0] ^= 255;
        }
        response.end(payload);
    } catch (error) {
        if (!response.headersSent) response.writeHead(error.code === 'ENOENT' ? 404 : 500).end();
        else response.destroy();
    }
});
server.listen(0, '127.0.0.1');
await once(server, 'listening');
const feed = `http://127.0.0.1:${server.address().port}/`;
let scoreAddress;
let scoreProcess;
let scoreOutcome;
const scoreEvidence = {};

async function until(predicate, description, deadline = 15_000) {
    const start = Date.now();
    while (Date.now() - start < deadline) {
        const value = await predicate();
        if (value) return value;
        await delay(100);
    }
    throw new Error('Timed out waiting for ' + description);
}

async function startScoreServer(name, fault = false) {
    if (!scoreAddress) {
        const reserved = createTcpServer().listen(0, '127.0.0.1');
        await once(reserved, 'listening');
        scoreAddress = `http://127.0.0.1:${reserved.address().port}/`;
        await new Promise((done, fail) => reserved.close(error => error ? fail(error) : done()));
    }
    const port = Number(new URL(scoreAddress).port);
    const config = join(evidence, name + '.json');
    await writeFile(config, JSON.stringify({
        ScorePort: port, ScoreDirectory: join(data, 'Scores'), DropFirstScoreConfirmation: fault
    }));
    let log = '';
    scoreProcess = spawn('dotnet', [scoreServer, '--config', config], { windowsHide: true, stdio: ['ignore', 'pipe', 'pipe'] });
    scoreProcess.stdout.on('data', chunk => { log += chunk.toString(); });
    scoreProcess.stderr.on('data', chunk => { log += chunk.toString(); });
    scoreOutcome = once(scoreProcess, 'close').then(() => writeFile(join(evidence, name + '.log'), log));
    await until(async () => {
        if (scoreProcess.exitCode != null) throw new Error('Score server exited. See ' + name + '.log');
        const response = await readRecovery(0);
        return response?.ok;
    }, name);
}

async function stopScoreServer() {
    if (!scoreProcess) return;
    scoreProcess.kill();
    await scoreOutcome;
    scoreProcess = null;
}

async function readRecovery(after) {
    try { return await fetch(scoreAddress + 'contract-scores/recovery?after=' + after, { signal: AbortSignal.timeout(1500) }); }
    catch { return null; }
}

async function confirmDeduplication() {
    await until(async () => (await readRecovery(2))?.status === 503, 'server commit followed by dropped confirmation');
    const submission = JSON.parse(await readFile(join(evidence, 'Online', 'offline-score.json'), 'utf8'));
    const submit = data => fetch(scoreAddress + 'contract-scores/submissions', {
        method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify(data)
    });
    const repeated = await submit(submission);
    const receipt = await repeated.json();
    if (!repeated.ok || receipt.sequence !== 3 || receipt.submissionId !== submission.submissionId)
        throw new Error('Duplicate submission must return the original receipt.');
    const conflicting = await submit({ ...submission, elapsedTicks: submission.elapsedTicks + 1 });
    if (conflicting.status !== 409) throw new Error('Reusing a submission ID with a different score must fail.');
    scoreEvidence.duplicateReceipt = receipt;
    scoreEvidence.conflictingSubmissionStatus = conflicting.status;
    await stopScoreServer();
    await startScoreServer('ScoreServer-Recovery');
}

async function exists(path) {
    return stat(path).then(() => true, error => {
        if (error.code === 'ENOENT') return false;
        throw error;
    });
}

async function run(name, offline = false, scoreStage = null) {
    const output = join(evidence, name);
    await mkdir(output);
    const args = ['-batchmode', '-nographics', '-contract-feed', feed, '-contract-data', data,
        scores ? '-scores-evidence' : settings ? '-settings-evidence' : '-contract-evidence', output, '-logFile', join(output, 'Player.log')];
    if (offline) args.push(settings ? '-settings-reopen' : '-contract-offline', 'true');
    if (scores) args.push('-contract-scores', scoreAddress, '-scores-stage', scoreStage);
    const child = spawn(player, args, { cwd: dirname(player), windowsHide: true, stdio: 'ignore' });
    const outcome = once(child, 'exit').then(([code]) => code);
    let exited = false;
    outcome.then(() => { exited = true; }, () => { exited = true; });
    const deadline = Date.now() + 160_000;
    let processedScoreMarker = false;
    try {
        while (!exited && Date.now() < deadline) {
            const failure = join(output, 'failure.txt');
            if (await exists(failure)) throw new Error(await readFile(failure, 'utf8'));
            if (phase === 'initial' && await exists(join(output, 'publish-v2'))) {
                phase = scores ? 'healthy' : 'faults';
                await writeFile(join(output, 'v2-available'), '');
            }
            if (phase === 'faults' && await exists(join(output, 'restore-v2'))) {
                phase = 'healthy';
                await writeFile(join(output, 'v2-restored'), '');
            }
            if (scores && !processedScoreMarker) {
                if (scoreStage === 'online' && await exists(join(output, 'go-offline'))) {
                    await stopScoreServer();
                    await writeFile(join(output, 'server-stopped'), '');
                    processedScoreMarker = true;
                } else if (scoreStage === 'reopen' && await exists(join(output, 'pending-visible'))) {
                    await confirmDeduplication();
                    processedScoreMarker = true;
                }
            }
            await delay(100);
        }
        if (!exited) throw new Error(`Player timed out. See ${output}.`);
        const code = await outcome;
        if (code !== 0 || !await exists(join(output, 'passed.txt'))) {
            const failure = join(output, 'failure.txt');
            throw new Error(await exists(failure) ? await readFile(failure, 'utf8') : `Player exited ${code}. See ${output}.`);
        }
        return (await readFile(join(output, 'steps.txt'), 'utf8')).trim().split(/\r?\n/);
    } finally {
        if (!exited) {
            child.kill();
            await outcome;
        }
    }
}

function closeFeed() {
    server.close();
    server.closeAllConnections();
}

const report = { passed: false, feed, player, data, requests, scenario: scores ? 'contract-scores' : settings ? 'player-settings' : 'contract-downloads' };
try {
    if (scores) {
        report.scores = scoreEvidence;
        await startScoreServer('ScoreServer-Initial');
        report.online = await run('Online', false, 'online');
        await startScoreServer('ScoreServer-DropReceipt', true);
        report.recovery = await run('Recovery', false, 'reopen');
        await stopScoreServer();
        await startScoreServer('ScoreServer-Reopened');
        report.reopened = await run('Reopened', false, 'confirmed');
        const result = await (await readRecovery(0)).json();
        if (result.ThroughSequence !== 3 || result.Receipts.length !== 3)
            throw new Error('Exactly three score receipts should survive server restart.');
        scoreEvidence.results = result.Receipts.map(receipt => ({
            contract: receipt.contractId, version: receipt.contentVersion,
            elapsedTicks: receipt.elapsedTicks, sequence: receipt.sequence
        }));
        const expected = ['salt-causeway/v1', 'salt-causeway/v2', 'ember-foundry/v1'];
        if (scoreEvidence.results.some((score, index) => `${score.contract}/${score.version}` !== expected[index] || score.elapsedTicks <= 0))
            throw new Error('The durable scores must belong to the three played content versions.');
    } else {
        report.online = await run('Online');
        if (!settings && (attempts !== 2 || !requests.some(request => request.fault === 'corrupt' && request.range)))
            throw new Error('Expected an interrupted download, a Range retry, and an integrity failure.');
        closeFeed();
        report.offline = await run('Offline', true);
    }
    report.passed = true;
} catch (error) {
    report.failure = error.message;
    process.exitCode = 1;
} finally {
    await stopScoreServer();
    closeFeed();
    await writeFile(join(evidence, 'report.json'), JSON.stringify(report, null, 2) + '\n');
    console.log(`${report.passed ? 'PASS' : report.failure} | ${join(evidence, 'report.json')}`);
}
