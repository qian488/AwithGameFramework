const select = id => document.getElementById(id);
const game = select("game"), prototype = select("prototype"), fixture = select("fixture");
const nodes = select("nodes"), board = select("artboard"), source = select("source");
const notice = select("notice"), workspace = select("workspace"), properties = select("properties");
const mutation = { "Content-Type": "application/json", "X-Awith-Panel": "1" };
let current, previewFont, pendingEdit = false;

async function request(url, options) {
    const response = await fetch(url, options);
    const result = await response.json().catch(() => ({}));
    if (!response.ok) throw new Error(result.error || `HTTP ${response.status}`);
    return result;
}

function address() { return `/api/prototypes/${encodeURIComponent(game.value)}/${encodeURIComponent(prototype.value)}`; }

async function listPages() {
    workspace.hidden = true;
    const ids = await request(`/api/prototypes/${encodeURIComponent(game.value)}`);
    prototype.replaceChildren(...ids.map(id => new Option(id, id)));
    if (ids.length) await openPage();
    else notice.textContent = "此工作区暂无 UI 原型。";
}

async function openPage() {
    current = await request(address());
    pendingEdit = false;
    fixture.replaceChildren(...current.profile.fixtures.map(state => new Option(state.title, state.page)));
    prototype.options[prototype.selectedIndex].text = current.profile.title;
    source.value = JSON.stringify(current.document, null, 2);
    if (previewFont) document.fonts.delete(previewFont);
    previewFont = new FontFace("Prototype", `url(${address()}/assets/font)`);
    previewFont.load().then(font => {
        if (font === previewFont) document.fonts.add(font);
    }).catch(() => {});
    workspace.hidden = false;
    notice.textContent = "";
    updateNodes();
}

function updateNodes() {
    const selected = nodes.value;
    nodes.replaceChildren(new Option("面板", ""), ...current.document.nodes.map(item => new Option(item.id, item.id)));
    if ([...nodes.options].some(option => option.value === selected)) nodes.value = selected;
    editor();
    render();
}

function editor() {
    properties.replaceChildren();
    const node = current.document.nodes.find(item => item.id === nodes.value);
    const target = node || current.document;
    const fields = node
        ? [["x", "左"], ["y", "上"], ["width", "宽度"], ["height", "高度"], ["text", "文本"],
            ["color", "颜色"], ["fontSize", "字号"], ["padding", "内边距"], ["spacing", "行间距"]]
        : [["width", "宽度"], ["height", "高度"], ["background", "背景"]];
    if (node?.kind === "image") fields.push(["asset", "素材"]);
    for (const [key, label] of fields) {
        const tag = document.createElement("label");
        tag.textContent = label;
        const input = document.createElement(key === "asset" ? "select" : "input");
        if (key === "asset") {
            input.add(new Option("无", ""));
            for (const asset of current.profile.assets) input.add(new Option(asset.key, asset.key));
        } else if (["x", "y", "width", "height", "fontSize", "padding", "spacing"].includes(key)) input.type = "number";
        input.value = target[key] ?? "";
        input.dataset.property = key;
        input.addEventListener("input", () => {
            target[key] = input.type === "number" ? Number(input.value) : input.value;
            designChanged();
            render();
        });
        tag.append(input);
        properties.append(tag);
    }
}

function designChanged() {
    pendingEdit = true;
    source.value = JSON.stringify(current.document, null, 2);
    notice.textContent = "有待保存的修改";
}

function render() {
    const design = current.document;
    const sample = current.profile.fixtures.find(item => item.page === fixture.value);
    const values = new Map(sample?.values.map(item => [item.key, item.text]));
    board.replaceChildren();
    Object.assign(board.style, { width: design.width + "px", height: design.height + "px",
        backgroundColor: design.background, fontFamily: "Prototype, sans-serif" });
    const elements = new Map();
    for (const item of design.nodes) {
        if (item.pages?.length && !item.pages.includes(fixture.value) || item.parent && !elements.has(item.parent)) continue;
        const view = document.createElement("div");
        view.className = `prototype-node prototype-${item.kind}`;
        view.dataset.node = item.id;
        if (nodes.value === item.id) view.classList.add("prototype-selected");
        Object.assign(view.style, {
            left: item.x + "px", top: item.y + "px", width: item.width + "px", height: item.height + "px",
            fontSize: (item.fontSize || 22) + "px", padding: (item.padding || 0) + "px", boxSizing: "border-box",
            lineHeight: ((item.fontSize || 22) + (item.spacing || 0)) + "px"
        });
        if (["text", "list"].includes(item.kind)) view.style.color = item.color || "white";
        else view.style.backgroundColor = item.asset ? "transparent" : item.color || "transparent";
        if (item.asset) {
            const image = document.createElement("img");
            image.src = address() + "/assets/" + encodeURIComponent(item.asset);
            image.alt = item.asset;
            view.append(image);
        } else view.textContent = values.get(item.binding) ?? item.text ?? "";
        view.addEventListener("click", event => {
            event.stopPropagation();
            nodes.value = item.id;
            editor();
            render();
        });
        (elements.get(item.parent) || board).append(view);
        elements.set(item.id, view);
    }
    fit();
}

function fit() {
    if (!current || workspace.hidden) return;
    const preview = select("preview");
    const { width, height } = current.document;
    const scale = Math.min(1, (preview.clientWidth - 32) / width);
    board.style.transform = `scale(${scale})`;
    board.style.margin = `${(scale - 1) * height / 2}px ${(scale - 1) * width / 2}px`;
}

function action(handler) {
    return async () => {
        try { await handler(); }
        catch (error) { notice.textContent = error.message; }
    };
}

game.addEventListener("change", action(listPages));
prototype.addEventListener("change", action(openPage));
nodes.addEventListener("change", () => { editor(); render(); });
fixture.addEventListener("change", render);
new ResizeObserver(fit).observe(select("preview"));
select("apply-source").addEventListener("click", action(() => {
    const draft = JSON.parse(source.value);
    if (!Array.isArray(draft.nodes) || !draft.width || !draft.height) throw new Error("请输入包含面板尺寸和节点的 JSON。");
    current.document = draft;
    designChanged();
    updateNodes();
}));
select("save").addEventListener("click", action(async () => {
    const { revision } = await request(address(), { method: "POST", headers: mutation,
        body: JSON.stringify({ revision: current.revision, document: current.document }) });
    current.revision = revision;
    pendingEdit = false;
    notice.textContent = "设计已保存";
}));
select("preflight").addEventListener("click", action(async () => {
    if (pendingEdit) throw new Error("保存设计后即可预检。");
    const started = await request(address() + "/preview", { method: "POST", headers: mutation });
    const report = select("unity-report");
    report.hidden = false;
    select("preflight").disabled = true;
    try {
        for (let run = started; run?.startedAt === started.startedAt;) {
            report.textContent = run.output || "Unity 正在导入隔离工程…";
            if (run.status !== "running") {
                notice.textContent = run.status === "succeeded" ? "Unity 预检通过" : "Unity 预检失败";
                break;
            }
            await new Promise(resolve => setTimeout(resolve, 600));
            ({ run } = await request("/api/run"));
        }
    } finally { select("preflight").disabled = false; }
}));

await action(async () => {
    const scopes = (await request("/api/scopes")).filter(scope => scope.id.startsWith("games/"));
    game.replaceChildren(...scopes.map(scope => new Option(scope.name, scope.name)));
    const requested = new URLSearchParams(location.search).get("game");
    if (scopes.some(scope => scope.name === requested)) game.value = requested;
    if (game.value) await listPages();
})();
