using System.Net;
using AwithGameFrame.DevPanel;

const int port = 5188;
string authority = $"127.0.0.1:{port}";
var root = new DirectoryInfo(AppContext.BaseDirectory);
while (root != null && !File.Exists(Path.Combine(root.FullName, "AwithGameFrame.sln")))
    root = root.Parent;
if (root == null)
    throw new InvalidOperationException("Start DevPanel from an AwithGameFrame checkout.");

var catalog = new PanelCatalog(root.FullName);
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    ContentRootPath = Path.Combine(root.FullName, "tools", "DevPanel")
});
builder.WebHost.ConfigureKestrel(server => server.Listen(IPAddress.Loopback, port));
builder.Services.AddSingleton(catalog);
builder.Services.AddSingleton<PanelRunner>();
builder.Services.AddHostedService(services => services.GetRequiredService<PanelRunner>());
var app = builder.Build();

app.Use(async (context, next) =>
{
    if (!string.Equals(context.Request.Host.Value, authority, StringComparison.Ordinal))
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        return;
    }

    context.Response.Headers.CacheControl = "no-store";
    await next(context);
});
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapGet("/api/scopes", () => catalog.Read());
app.MapGet("/api/run", (PanelRunner runner) => new { run = runner.Latest() });
app.MapPost("/api/run", (HttpRequest request, RunRequest input, PanelRunner runner) =>
{
    // A custom header keeps cross-origin browser forms from starting commands.
    if (request.Headers["X-Awith-Panel"] != "1")
        return Results.Json(new { error = "请从控制台页面启动工具。" }, statusCode: 403);

    var operation = catalog.Read().FirstOrDefault(scope => scope.Id == input.Scope)?
        .Operations.FirstOrDefault(item => item.Id == input.Operation);
    if (operation == null)
        return Results.BadRequest(new { error = "找不到该操作，请刷新工作区列表。" });
    if (operation.UnavailableReason != null)
        return Results.BadRequest(new { error = operation.UnavailableReason });
    if (input.Check && !operation.SupportsCheck)
        return Results.BadRequest(new { error = "该操作没有独立的产物检查模式。" });

    var run = runner.TryStart(operation, input.Check);
    return run == null
        ? Results.Conflict(new { error = "已有工具运行中，结束后可再次执行。" })
        : Results.Accepted("/api/run", run);
});
app.Run();

internal sealed record RunRequest(string? Scope, string? Operation, bool Check);
