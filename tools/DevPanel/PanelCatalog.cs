using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace AwithGameFrame.DevPanel;

internal sealed record PanelScope(string Id, string Name, string Path, IReadOnlyList<PanelOperation> Operations);

internal sealed record PanelOperation(string Id, string Title, string Category, string Description,
    string Script, string[] Arguments, bool SupportsCheck = false, string? UnavailableReason = null)
{
    [JsonIgnore]
    public bool IsPowerShell => Script.EndsWith(".ps1", StringComparison.Ordinal);
}

internal sealed class PanelCatalog(string root)
{
    private static readonly EnumerationOptions LocalEntries = new()
    {
        AttributesToSkip = FileAttributes.ReparsePoint,
        RecurseSubdirectories = false
    };

    public string Root { get; } = root;

    public IReadOnlyList<PanelScope> Read()
    {
        var scopes = new List<PanelScope>
        {
            new("framework", "框架仓库", "AwithGameFrame.sln",
            [
                new("build", "构建解决方案", "构建", "编译公共运行时、配套工具和样例。",
                    "dotnet", ["build", "AwithGameFrame.sln", "--nologo"]),
                new("config", "共享配置", "生成", "生成共享示例的配置 DTO 与数据。",
                    "tools/generate-config.ps1", [], SupportsCheck: true),
                new("protocol", "共享协议", "生成", "生成共享示例的 C# 协议，检查兼容性基线。",
                    "tools/generate-protocol.ps1", [], SupportsCheck: true),
                new("tests", "运行解决方案测试", "测试", "运行解决方案中登记的 .NET 测试项目。",
                    "dotnet", ["test", "AwithGameFrame.sln", "--nologo"]),
                new("verify", "运行标准验证", "测试", "检查文档、依赖和生成结果，运行构建、测试与 Headless 示例。",
                    "tools/verify.ps1", []),
                new("static", "运行静态检查", "测试", "检查文档链接、依赖、Unity 元数据和包路径。",
                    "tools/verify.ps1", ["-StaticOnly"])
            ])
        };

        var gamesRoot = Path.Combine(Root, "games");
        if (!Directory.Exists(gamesRoot))
            return scopes;

        foreach (string directory in Directory.EnumerateDirectories(gamesRoot, "*", LocalEntries)
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            string name = Path.GetFileName(directory);
            if (!Regex.IsMatch(name, @"\A[A-Za-z][A-Za-z0-9_]{0,39}\z") ||
                !File.Exists(Path.Combine(directory, "game.json")))
                continue;

            string source = $"games/{name}";
            bool hasConfig = File.Exists(Path.Combine(directory, "configuration", "schema.json")) &&
                Directory.Exists(Path.Combine(directory, "configuration", "Data"));
            string protocol = Path.Combine(directory, "protocol");
            bool hasProtocol = File.Exists(Path.Combine(protocol, "protobuf", "buf.yaml")) ||
                (File.Exists(Path.Combine(protocol, "schema.json")) && File.Exists(Path.Combine(protocol, "baseline.json")));
            var operations = new List<PanelOperation>
            {
                new("build", "构建外部游戏代码", "构建", $"编译外部 C# 和服务端，输出到 artifacts/games/{name}/Debug/portable。",
                    "tools/game.ps1", ["build", "-Name", name]),
                new("sync", "构建并同步客户端", "构建", "构建后将程序集与配置更新到已创建的引擎工程，由编辑器导入并编译。",
                    "tools/game.ps1", ["sync", "-Name", name]),
                new("config", "游戏配置", "生成", "由 configuration 下的源生成配置 DTO 与数据。",
                    "tools/game.ps1", ["config", "-Name", name], SupportsCheck: true,
                    UnavailableReason: hasConfig ? null : "需要 configuration/schema.json 和 configuration/Data。"),
                new("protocol", "游戏协议", "生成", "由 protocol 下的源生成 C# 并检查兼容性基线。",
                    "tools/game.ps1", ["protocol", "-Name", name], SupportsCheck: true,
                    UnavailableReason: hasProtocol ? null : "需要协议 schema 和基线，或 Protobuf 工程。")
            };
            var tests = Path.Combine(directory, "tests");
            if (Directory.Exists(tests) && (File.GetAttributes(tests) & FileAttributes.ReparsePoint) == 0)
            {
                var projectFiles = Directory.EnumerateFiles(tests, "*.Tests.csproj", new EnumerationOptions
                {
                    RecurseSubdirectories = true,
                    AttributesToSkip = FileAttributes.ReparsePoint
                }).OrderBy(path => path, StringComparer.OrdinalIgnoreCase);

                foreach (string project in projectFiles)
                {
                    string relative = Path.GetRelativePath(Root, project).Replace('\\', '/');
                    operations.Add(new(relative, Path.GetFileNameWithoutExtension(project), "测试", relative,
                        "dotnet", ["test", relative, "--nologo"]));
                }
            }
            scopes.Add(new(source, name, source, operations));
        }
        return scopes;
    }
}
