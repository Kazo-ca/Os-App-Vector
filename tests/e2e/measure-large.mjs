// Measures a large drawing in Kazo Vector (doc/en/plan.md, work item 3): upload through the File
// Explorer, open from the Terminal, save in place, Save As .vectorcraft; time and memory of
// Chromium's renderer processes. Not a test: `node tests/e2e/measure-large.mjs <file.svg>` after
// `dotnet build Kazo.Vector.slnx -c Debug`, the results printed as JSON lines.
import { execSync } from 'node:child_process';
import path from 'node:path';
import { openApp, runInTerminal, startHost } from './host.mjs';
import { answerSaveDialog, bootPage, documentsOf, execute, frameOf, launchBrowser, waitForVectorCraft, waitForWindow } from './common.mjs';

const LONG_MS = Number(process.env.MEASURE_TIMEOUT_MS ?? 900_000);
const file = process.argv[2];
if (!file) {
    console.error('usage: node tests/e2e/measure-large.mjs <file.svg>');
    process.exit(2);
}
const name = path.basename(file);

/** Resident memory of Chromium's renderer processes, in MiB. */
const rendererMiB = () => {
    const lines = execSync('ps -eo rss=,args=').toString().split('\n').filter(line => line.includes('--type=renderer'));
    return Math.round(lines.reduce((sum, line) => sum + Number(line.trim().split(/\s+/)[0]), 0) / 1024);
};

const report = (step, values) => console.log(JSON.stringify({ step, ...values }));

/**
 * Runs a command and does not wait for its answer: with a drawing this large on a software
 * renderer, one frame can take longer than the 30 s VectorCraft's page API waits.
 */
const send = (frame, command, params) => {
    execute(frame, command, params).catch(error => report('late answer', { command, error: error.message.split('\n')[0] }));
};

/** Polls a frame's drawings until one matches; returns the elapsed milliseconds from `start`. */
async function until(frame, predicate, start) {
    const deadline = Date.now() + LONG_MS;
    while (Date.now() < deadline) {
        const docs = await documentsOf(frame).catch(() => []);
        if (docs.some(predicate)) {
            return Date.now() - start;
        }
        await new Promise(resolve => setTimeout(resolve, 250));
    }
    const docs = await documentsOf(frame).catch(error => String(error));
    throw new Error(`timed out; drawings: ${JSON.stringify(docs)}`);
}

const host = await startHost();
const browser = await launchBrowser({ webGpu: false });
try {
    const { page } = await bootPage(browser, host.baseUrl);
    report('booted', { rendererMiB: rendererMiB() });

    await openApp(page, 'File Explorer');
    // The folder listed first: an upload chosen before the Explorer is ready is lost.
    await page.getByText('Desktop', { exact: true }).first().waitFor({ timeout: LONG_MS });
    await page.waitForTimeout(3_000);
    let start = Date.now();
    await page.locator('#fileInput').setInputFiles(file);
    await page.getByText(name, { exact: true }).first().waitFor({ timeout: LONG_MS });
    report('uploaded', { ms: Date.now() - start, rendererMiB: rendererMiB() });

    // Kazo Vector started on the file, as a double click does (each file the OS opens gets its own
    // window, decision 7): the start of VectorCraft, then the reading of the drawing.
    start = Date.now();
    await runInTerminal(page, `launch ca.kazo.vector --webgl /home/neo/${name}`);
    const id = await waitForWindow(page, 1);
    const frame = await frameOf(page, id);
    await waitForVectorCraft(frame);
    report('started', { ms: Date.now() - start, rendererMiB: rendererMiB() });
    const opened = await until(frame, doc => doc.active && doc.title?.startsWith(path.parse(name).name), start);
    report('opened', { ms: opened, rendererMiB: rendererMiB() });

    send(frame, 'shape.rectangle', { x: 10, y: 10, width: 50, height: 50 });
    report('edited', { ms: await until(frame, doc => doc.active && doc.dirty, Date.now()) });
    start = Date.now();
    send(frame, 'file.save', {});
    const saved = await until(frame, doc => doc.active && !doc.dirty, start);
    report('saved in place', { ms: saved, rendererMiB: rendererMiB() });

    send(frame, 'shape.rectangle', { x: 80, y: 10, width: 50, height: 50 });
    await until(frame, doc => doc.active && doc.dirty, Date.now());
    const copy = `${path.parse(name).name}.vectorcraft`;
    start = Date.now();
    send(frame, 'file.saveAs', { path: copy, options: {} });
    const dialog = page.locator('section.file-dialog', { hasText: 'Save the drawing' });
    await dialog.waitFor({ state: 'visible', timeout: LONG_MS });
    await answerSaveDialog(page, 'Save the drawing', copy);
    const savedAs = await until(frame, doc => doc.active && !doc.dirty, start);
    report('saved as .vectorcraft', { ms: savedAs, rendererMiB: rendererMiB() });
} catch (error) {
    const page = browser.contexts()[0]?.pages()[0];
    const notice = await page?.locator('.vector-notice').allTextContents().catch(() => []);
    const loading = await page?.frames().map(frame => frame.evaluate(() => document.getElementById('vectorcraft_loading')?.textContent ?? null).catch(() => null));
    report('failed', { error: error.message, notice, loading: loading && await Promise.all(loading), rendererMiB: rendererMiB() });
    if (process.env.VECTOR_E2E_SHOTS) {
        await page?.screenshot({ path: path.join(process.env.VECTOR_E2E_SHOTS, 'measure-large.png') });
    }
    process.exitCode = 1;
} finally {
    await browser.close();
    host.stop();
}
