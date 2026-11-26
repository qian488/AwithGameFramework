import { spawn } from 'node:child_process';
import { randomUUID } from 'node:crypto';
import { once } from 'node:events';
import { copyFile, mkdir, mkdtemp, readFile, realpath, rename, stat, writeFile } from 'node:fs/promises';
import { createServer } from 'node:http';
import { createServer as createTcpServer } from 'node:net';
import { dirname, join, resolve, sep } from 'node:path';
import { fileURLToPath } from 'node:url';
import { setTimeout as delay } from 'node:timers/promises';

const [playerPath, publicationPath, legacySave, evidencePath, mode, scoreServerPath] = process.argv.slice(2);
const settings = mode === '--settings';
const scores = mode === '--scores';
const leaderboard = mode === '--leaderboard';
const replay = mode === '--replay';
if (!evidencePath || !legacySave || !publicationPath || !playerPath)
    throw new Error('Pass the Windows Player, publication directory, legacy slot-1.save and a new evidence directory.');
const player = await realpath(playerPath);
const boot = await readFile(join(dirname(player), 'Test_Data', 'boot.config'), 'utf8');
if (/^player-connection-mode=/m.test(boot)) throw new Error('Build the verification Player with -ReleasePlayer.');
const publication = await realpath(publicationPath);
const scoreServer = scores || leaderboard ? await realpath(scoreServerPath) : null;
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
let leaderboardOffers;
let attempts = 0;
const server = createServer(async (request, response) => {
    try {
        const pathname = decodeURIComponent(new URL(request.url, 'http://127.0.0.1').pathname).slice(1);
        const entry = { pathname, range: request.headers.range ?? null, phase };
        requests.push(entry);
        if (pathname === 'index.json') {
            response.setHeader('Content-Type', 'application/json');
            response.end(JSON.stringify(leaderboardOffers ?? (phase === 'initial' ? [{ ...salt, version: 'v1' }] : offers)));
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

async function run(name, offline = false, scoreStage = null, profile = data, contract = null) {
    const output = join(evidence, name);
    await mkdir(output);
    const evidenceFlag = replay ? '-contract-replay-evidence' : leaderboard && scoreStage !== 'leaderboard' ? '-leaderboard-evidence' :
        scores || leaderboard ? '-scores-evidence' : settings ? '-settings-evidence' : '-contract-evidence';
    const args = ['-batchmode', '-nographics', '-contract-feed', feed, '-contract-data', profile,
        evidenceFlag, output, '-logFile', join(output, 'Player.log')];
    if (offline) args.push(settings ? '-settings-reopen' : '-contract-offline', 'true');
    if (replay) args.push('-replay-stage', scoreStage);
    if (scores) args.push('-contract-scores', scoreAddress, '-scores-stage', scoreStage);
    if (leaderboard && scoreStage === 'leaderboard')
        args.push('-contract-scores', scoreAddress, '-scores-stage', scoreStage, '-leaderboard-contract', contract);
    if (leaderboard && scoreStage !== 'leaderboard') args.push('-contract-leaderboard', scoreAddress);
    if (leaderboard && scoreStage === 'cached') args.push('-leaderboard-reopen', 'true');
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
                phase = scores || replay ? 'healthy' : 'faults';
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
            if (leaderboard && !processedScoreMarker && scoreStage === 'query' && await exists(join(output, 'query-loaded'))) {
                await stopScoreServer();
                await writeFile(join(output, 'server-stopped'), '');
                processedScoreMarker = true;
            }
            if (leaderboard && !processedScoreMarker && scoreStage === 'cached' && await exists(join(output, 'cache-visible'))) {
                await startScoreServer('LeaderboardServer-Reopened');
                await writeFile(join(output, 'server-restored'), '');
                processedScoreMarker = true;
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

const report = { passed: false, feed, player, data, requests,
    scenario: replay ? 'contract-replay' : leaderboard ? 'leaderboard' : scores ? 'contract-scores' : settings ? 'player-settings' : 'contract-downloads' };
try {
    if (replay) {
        report.record = await run('Record', false, 'record');
        closeFeed();
        report.offline = await run('Offline', true, 'offline');
        const fingerprint = (await readFile(join(evidence, 'Record', 'fingerprint.txt'), 'utf8')).trim();
        if (!/^[a-f0-9]{64}$/.test(fingerprint)) throw new Error('Invalid recorded content fingerprint.');
        const retained = join(data, 'ContractDownloads', 'salt-causeway', 'versions', fingerprint);
        await rename(retained, join(data, 'removed-v1'));
        report.missing = await run('Missing', true, 'missing');
    } else if (leaderboard) {
        phase = 'healthy';
        await startScoreServer('LeaderboardServer');
        const profiles = await Promise.all(Array.from({ length: 3 }, () =>
            mkdtemp(fileURLToPath(new URL('../../artifacts/Lb-', import.meta.url)))));
        const play = (name, index, offer) => {
            leaderboardOffers = [offer];
            return run(name, false, 'leaderboard', profiles[index], offer.id);
        };
        const saltV1 = { ...salt, version: 'v1' };
        const ember = offers.find(offer => offer.id === 'ember-foundry');
        report.plays = [
            await play('FirstPlayer', 0, saltV1),
            await play('SecondPlayerSameBoard', 1, saltV1),
            await play('SecondPlayerNewVersion', 1, salt),
            await play('ThirdPlayer', 2, ember)
        ];
        const playerId = await readFile(join(profiles[0], 'RainPassProfile', 'player-id.txt'), 'utf8');
        const post = async (path, payload) => {
            const response = await fetch(scoreAddress + path, {
                method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify(payload),
                signal: AbortSignal.timeout(5000)
            });
            if (!response.ok) throw new Error(path + ' returned ' + response.status);
            return response.json();
        };
        const query = async (contractId, contentVersion) => (await post('contract-scores/leaderboard',
            { playerId, contractId, contentVersion, limit: 10 })).data;
        const original = await query(salt.id, 'v1');
        const sorted = original.entries[0]?.elapsedTicks <= original.entries[1]?.elapsedTicks;
        if (original.entries.length !== 2 || !original.ownRank || !sorted ||
            (await query(salt.id, 'v2')).entries.length !== 1 || (await query(ember.id, 'v1')).entries.length !== 1 ||
            (await query(ember.id, 'v2')).entries.length !== 0)
            throw new Error('Expected two v1 players, one v2 player, one Ember player and an empty unreleased version.');
        const submit = elapsedTicks => post('contract-scores/submissions', {
            submissionId: randomUUID().replaceAll('-', ''), playerId, contractId: salt.id, contentVersion: 'v1', elapsedTicks
        });
        await submit(original.ownBestTicks + 120);
        const worse = await query(salt.id, 'v1');
        if (JSON.stringify(worse) !== JSON.stringify(original)) throw new Error('A slower run changed the board.');
        const fasterTicks = original.entries[0].elapsedTicks - 1;
        await submit(fasterTicks);
        const improved = await query(salt.id, 'v1');
        if (improved.ownRank !== 1 || improved.ownBestTicks !== fasterTicks || improved.entries.length !== 2)
            throw new Error('A faster run did not update the existing rank.');
        report.boards = { original, worse, improved };
        leaderboardOffers = [saltV1];
        report.cache = await run('LeaderboardCache', false, 'query', profiles[0]);
        report.reopened = await run('LeaderboardReopened', false, 'cached', profiles[0]);
        if (JSON.stringify(await query(salt.id, 'v1')) !== JSON.stringify(improved))
            throw new Error('The board changed after server restart.');
        for (let index = 11; index > 0; index--)
            await post('contract-scores/submissions', {
                submissionId: randomUUID().replaceAll('-', ''), playerId: 'fixture-' + String(index).padStart(2, '0'),
                contractId: ember.id, contentVersion: 'v2', elapsedTicks: 100
            });
        await post('contract-scores/submissions', {
            submissionId: randomUUID().replaceAll('-', ''), playerId, contractId: ember.id, contentVersion: 'v2', elapsedTicks: 200
        });
        const fullBoard = await query(ember.id, 'v2');
        if (fullBoard.entries.length !== 10 || fullBoard.ownRank !== 12 || fullBoard.ownBestTicks !== 200)
            throw new Error('Expected a top ten and a separate rank for the local player outside the top ten.');
        if (fullBoard.entries.some((entry, index) => entry.isMine || entry.rank !== index + 1 ||
            entry.playerId !== 'fixture-' + String(index + 1).padStart(2, '0')))
            throw new Error('Tied scores must sort by player ID, independent of submission order.');
        report.topTen = fullBoard;
    } else if (scores) {
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
