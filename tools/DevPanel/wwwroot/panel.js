const scopeSelect = document.querySelector("#scope");
const scopePath = document.querySelector("#scope-path");
const toolList = document.querySelector("#tools");
const errorMessage = document.querySelector("#error");
const state = document.querySelector("#state");
const execution = document.querySelector("#execution");
const runTitle = document.querySelector("#run-title");
const runTime = document.querySelector("#run-time");
const runCommand = document.querySelector("#run-command");
const output = document.querySelector("#output");
const log = document.querySelector("#log");
const logPath = document.querySelector("#log-path");

let scopes = [];
let submitting = false;
let connected = false;
let running = false;
let requestVersion = 0;
let connectionError = "";
let actionError = "";
let displayedRun;

async function request(url, options) {
    const response = await fetch(url, options);
    if (!response.ok) {
        const problem = await response.json().catch(() => null);
        throw new Error(problem?.error ?? `请求失败（HTTP ${response.status}）`);
    }
    return response.json();
}

function element(tag, className, text) {
    const item = document.createElement(tag);
    if (className) item.className = className;
    if (text) item.textContent = text;
    return item;
}

function showError() {
    errorMessage.textContent = connectionError || actionError;
    errorMessage.hidden = !errorMessage.textContent;
}

function updateButtons() {
    const disabled = running || submitting || !connected;
    for (const button of toolList.querySelectorAll("button[data-run]"))
        button.disabled = disabled || button.dataset.run === "unavailable";
}

function renderTools() {
    const scope = scopes.find(item => item.id === scopeSelect.value);
    scopePath.textContent = scope.path;
    const prototypes = document.querySelector("#prototypes");
    prototypes.hidden = !scope.id.startsWith("games/");
    prototypes.href = "/prototypes.html?game=" + encodeURIComponent(scope.name);
    toolList.replaceChildren();

    const categories = new Map();
    for (const operation of scope.operations) {
        if (!categories.has(operation.category)) {
            const group = element("section", "group");
            const list = element("div", "tool-list");
            group.append(element("h2", "", operation.category), list);
            toolList.append(group);
            categories.set(operation.category, list);
        }

        const card = element("article", "tool");
        const details = element("div");
        details.append(element("h3", "", operation.title), element("p", "", operation.description));
        if (operation.unavailableReason)
            details.append(element("p", "unavailable", operation.unavailableReason));

        const actions = element("div", "actions");
        const addButton = (label, check, secondary) => {
            const button = element("button", secondary ? "secondary" : "", label);
            button.type = "button";
            button.dataset.run = operation.unavailableReason ? "unavailable" : "available";
            button.setAttribute("aria-label", `${label} · ${operation.title}`);
            button.addEventListener("click", () => startRun(scope.id, operation.id, check));
            actions.append(button);
        };
        if (operation.supportsCheck) {
            addButton("生成", false, false);
            addButton("检查", true, true);
        } else {
            addButton("运行", false, false);
        }
        card.append(details, actions);
        categories.get(operation.category).append(card);
    }
    updateButtons();
}

function showRun(run) {
    running = run?.status === "running";
    updateButtons();
    const labels = {
        running: "运行中",
        succeeded: "已通过",
        failed: "执行失败",
        cancelled: "已取消"
    };
    const status = run ? labels[run.status] : "空闲";
    if (state.textContent !== status) state.textContent = status;
    state.dataset.value = run?.status ?? "idle";
    execution.hidden = !run;
    log.hidden = !run?.logPath;
    if (!run) {
        if (displayedRun) output.textContent = "从工具列表启动一项操作，输出将显示在这里。";
        displayedRun = undefined;
        return;
    }

    const elapsed = `${run.durationSeconds.toFixed(1)} 秒`;
    runTitle.textContent = run.title;
    runTime.textContent = run.exitCode == null ? elapsed : `${elapsed} · 退出码 ${run.exitCode}`;
    runCommand.textContent = run.command;
    logPath.textContent = run.logPath ?? "";
    const follow = displayedRun !== run.startedAt || output.scrollHeight - output.scrollTop - output.clientHeight < 36;
    const text = run.output || "正在启动工具…";
    if (output.textContent !== text) output.textContent = text;
    if (follow) output.scrollTop = output.scrollHeight;
    displayedRun = run.startedAt;
}

async function startRun(scope, operation, check) {
    if (submitting || running) return;
    submitting = true;
    requestVersion++;
    actionError = "";
    showError();
    updateButtons();
    try {
        const run = await request("/api/run", {
            method: "POST",
            headers: { "Content-Type": "application/json", "X-Awith-Panel": "1" },
            body: JSON.stringify({ scope, operation, check })
        });
        showRun(run);
    } catch (error) {
        actionError = error.message;
        showError();
    } finally {
        submitting = false;
        updateButtons();
    }
}

async function poll() {
    const version = requestVersion;
    try {
        if (!submitting) {
            if (!scopes.length) {
                scopes = await request("/api/scopes");
                for (const scope of scopes) scopeSelect.add(new Option(scope.name, scope.id));
                scopeSelect.disabled = false;
                renderTools();
            }
            const { run } = await request("/api/run");
            if (version === requestVersion) {
                connected = true;
                connectionError = "";
                showError();
                showRun(run);
            }
        }
    } catch {
        connected = false;
        connectionError = "无法读取本地控制台。请检查启动终端，服务恢复后页面会自动重连。";
        showError();
        updateButtons();
    } finally {
        window.setTimeout(poll, 1000);
    }
}

scopeSelect.addEventListener("change", renderTools);
poll();
