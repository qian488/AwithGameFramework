using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AwithGameFrame.Tools.UiPrototype;

namespace AwithGameFrame.DevPanel;

internal static class PrototypeEndpoints
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        IncludeFields = true, WriteIndented = true, MaxDepth = 24
    };
    private static readonly object WriteGate = new();

    public static void MapPrototypes(this WebApplication app, PanelCatalog catalog)
    {
        app.MapGet("/api/prototypes/{game}", (string game) =>
        {
            var scope = FindGame(catalog, game);
            if (scope == null) return Results.NotFound();
            string folder = Path.Combine(catalog.Root, scope.Path, "ui-prototypes");
            var ids = Directory.Exists(folder) && IsLocal(folder, catalog.Root)
                ? Directory.GetFiles(folder, "*.profile.json")
                    .Select(path => Path.GetFileName(path).Replace(".profile.json", ""))
                    .Where(id => PrototypeValidation.IsId(id) && Find(catalog, game, id) != null).Order().ToArray()
                : Array.Empty<string>();
            return Results.Ok(ids);
        });
        app.MapGet("/api/prototypes/{game}/{id}", (string game, string id) =>
        {
            var page = Find(catalog, game, id);
            if (page == null) return Results.NotFound();
            var bytes = File.ReadAllBytes(page.Design);
            return Results.Json(new { revision = Revision(bytes), document = JsonDocument.Parse(bytes).RootElement,
                page.Profile }, Json);
        });
        app.MapGet("/api/prototypes/{game}/{id}/assets/{key}", (string game, string id, string key) =>
        {
            var page = Find(catalog, game, id);
            if (page == null) return Results.NotFound();
            string? asset = key == "font" ? page.Profile.font :
                page.Profile.assets.FirstOrDefault(item => item.key == key)?.path;
            var file = page.Asset(asset);
            if (file == null) return Results.NotFound();
            string? type = Path.GetExtension(file).ToLowerInvariant() switch
            {
                ".png" => "image/png", ".jpg" or ".jpeg" => "image/jpeg", ".ttf" => "font/ttf", ".otf" => "font/otf", _ => null
            };
            return type == null ? Results.NotFound() : Results.File(file, type);
        });
        app.MapPost("/api/prototypes/{game}/{id}", (HttpRequest request, string game, string id, PrototypeWrite input) =>
        {
            if (request.Headers["X-Awith-Panel"] != "1") return Results.StatusCode(403);
            var page = Find(catalog, game, id);
            if (page == null) return Results.NotFound();
            if (input.Document.ValueKind != JsonValueKind.Object || input.Document.GetRawText().Length > 96_000)
                return Results.BadRequest(new { error = "原型文件需要是小于 96 KB 的 JSON 对象。" });
            PrototypeDocument? design;
            try { design = input.Document.Deserialize<PrototypeDocument>(Json); }
            catch (JsonException ex) { return Results.BadRequest(new { error = ex.Message }); }
            var problems = PrototypeValidation.Check(design!, page.Profile, path => page.Asset(path) != null);
            if (problems.Length != 0) return Results.BadRequest(new { error = string.Join("\n", problems), problems });
            lock (WriteGate)
            {
                if (Revision(File.ReadAllBytes(page.Design)) != input.Revision)
                    return Results.Conflict(new { error = "原型已被修改，请刷新后合并当前编辑。" });
                string temporary = page.Design + "." + Guid.NewGuid().ToString("N") + ".tmp";
                var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(input.Document, Json) + "\n");
                try
                {
                    File.WriteAllBytes(temporary, bytes);
                    File.Move(temporary, page.Design, overwrite: true);
                }
                finally { File.Delete(temporary); }
                return Results.Ok(new { revision = Revision(bytes) });
            }
        });
        app.MapPost("/api/prototypes/{game}/{id}/preview", (HttpRequest request, string game, string id, PanelRunner runner) =>
        {
            if (request.Headers["X-Awith-Panel"] != "1") return Results.StatusCode(403);
            if (Find(catalog, game, id) == null) return Results.NotFound();
            string? editor = Environment.GetEnvironmentVariable("AWITH_UNITY_EDITOR");
            if (string.IsNullOrWhiteSpace(editor) || !File.Exists(editor))
                return Results.BadRequest(new { error = "请设置 AWITH_UNITY_EDITOR 指向此项目版本的 Unity 可执行文件。" });
            var operation = new PanelOperation("prototype-preview", "预检 Unity 原型", "设计", "",
                "tools/import-ui-prototype.ps1", ["-Name", game, "-Prototype", id, "-EditorPath", editor]);
            var run = runner.TryStart(operation, check: false);
            return run == null ? Results.Conflict(new { error = "已有工具运行中。" }) : Results.Accepted("/api/run", run);
        });
    }

    private static PanelScope? FindGame(PanelCatalog catalog, string game) =>
        catalog.Read().FirstOrDefault(scope => scope.Id == "games/" + game);

    private static PrototypePage? Find(PanelCatalog catalog, string game, string id)
    {
        if (!PrototypeValidation.IsId(id)) return null;
        var scope = FindGame(catalog, game);
        if (scope == null) return null;
        string gameRoot = Path.Combine(catalog.Root, scope.Path);
        string design = Path.Combine(gameRoot, "ui-prototypes", id + ".json");
        string profile = Path.Combine(gameRoot, "ui-prototypes", id + ".profile.json");
        if (!File.Exists(design) || !File.Exists(profile) || !IsLocal(design, gameRoot) || !IsLocal(profile, gameRoot)) return null;
        var spec = JsonSerializer.Deserialize<PrototypeProfile>(File.ReadAllBytes(profile), Json);
        return spec?.assets == null ? null : new PrototypePage(design, Path.Combine(gameRoot, "client", "Unity"), spec);
    }

    private static bool IsLocal(string path, string root)
    {
        path = Path.GetFullPath(path);
        root = Path.GetFullPath(root);
        for (string? entry = path; entry != root; entry = Path.GetDirectoryName(entry))
            if (entry == null || (File.GetAttributes(entry) & FileAttributes.ReparsePoint) != 0) return false;
        return true;
    }

    private static string Revision(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    internal sealed record PrototypeWrite(string Revision, JsonElement Document);

    private sealed record PrototypePage(string Design, string Unity, PrototypeProfile Profile)
    {
        public string? Asset(string? path)
        {
            if (!PrototypeValidation.IsAssetPath(path)) return null;
            string local = Path.Combine(Unity, path!.Replace('/', Path.DirectorySeparatorChar));
            return File.Exists(local) && IsLocal(local, Unity) ? local : null;
        }
    }
}
