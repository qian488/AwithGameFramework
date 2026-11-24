using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Threading.Channels;

namespace AwithGameFrame.DevPanel;

internal sealed record RunSnapshot(string Title, string Command, string Status, DateTimeOffset StartedAt,
    int? ExitCode, double DurationSeconds, string? LogPath, string Output);

internal sealed class PanelRunner(PanelCatalog catalog, ILogger<PanelRunner> logger) : BackgroundService
{
    private const int TailLimit = 32000;
    private readonly object gate = new();
    private readonly Channel<Run> pending = Channel.CreateUnbounded<Run>(
        new UnboundedChannelOptions { SingleReader = true });
    private Run? latest;

    public RunSnapshot? Latest()
    {
        lock (gate)
            return latest?.Snapshot();
    }

    public RunSnapshot? TryStart(PanelOperation operation, bool check)
    {
        var arguments = new List<string>();
        string executable = operation.Script;
        if (operation.IsPowerShell)
        {
            executable = OperatingSystem.IsWindows() ? "powershell.exe" : "pwsh";
            arguments.AddRange(["-NoLogo", "-NoProfile", "-NonInteractive", "-File", operation.Script]);
        }
        arguments.AddRange(operation.Arguments);
        if (check)
            arguments.Add("-Check");

        lock (gate)
        {
            if (latest?.Status == "running")
                return null;

            string title = check ? $"检查{operation.Title}产物" : operation.Title;
            var run = new Run(title, executable, arguments);
            latest = run;
            pending.Writer.TryWrite(run);
            return run.Snapshot();
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var run in pending.Reader.ReadAllAsync(stoppingToken))
            await RunAsync(run, stoppingToken);
    }

    private async Task RunAsync(Run run, CancellationToken stoppingToken)
    {
        var start = new ProcessStartInfo(run.Executable)
        {
            WorkingDirectory = catalog.Root,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        if (run.Executable == "powershell.exe")
        {
            // Windows PowerShell uses the system OEM code page for its redirected host output.
            int codePage = CultureInfo.InstalledUICulture.TextInfo.OEMCodePage;
            var encoding = CodePagesEncodingProvider.Instance.GetEncoding(codePage) ?? Encoding.UTF8;
            start.StandardOutputEncoding = encoding;
            start.StandardErrorEncoding = encoding;
        }
        foreach (string argument in run.Arguments)
            start.ArgumentList.Add(argument);
        start.Environment["NO_COLOR"] = "1";
        start.Environment["TERM"] = "dumb";
        start.Environment["DOTNET_CLI_UI_LANGUAGE"] = "en-US";
        start.Environment["MSBUILDTERMINALLOGGER"] = "off";

        using var process = new Process { StartInfo = start };
        StreamWriter? log = null;
        string status = "failed";
        int? exitCode = null;
        bool started = false;
        try
        {
            string relativeLog = $"artifacts/DevPanel/{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.log";
            string fullPath = Path.Combine(catalog.Root, relativeLog);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            log = new StreamWriter(new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
                { AutoFlush = true };
            lock (gate)
                run.LogPath = relativeLog;
            Write($"> {run.Command}{Environment.NewLine}");

            stoppingToken.ThrowIfCancellationRequested();
            started = process.Start();
            await Task.WhenAll(process.WaitForExitAsync(stoppingToken),
                CaptureAsync(process.StandardOutput, "", stoppingToken),
                CaptureAsync(process.StandardError, "[stderr] ", stoppingToken));
            exitCode = process.ExitCode;
            status = exitCode == 0 ? "succeeded" : "failed";
            Write($"{Environment.NewLine}退出码 {exitCode}{Environment.NewLine}");
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            status = "cancelled";
            Write($"控制台关闭，执行取消。{Environment.NewLine}");
        }
        catch (Exception error) when (error is Win32Exception or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            logger.LogError(error, "Could not run {Command}", run.Command);
            Write($"执行失败。{error.Message}{Environment.NewLine}");
        }
        finally
        {
            try
            {
                if (started && !process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync();
                }
            }
            catch (Exception error) when (error is Win32Exception or InvalidOperationException)
            {
                logger.LogError(error, "Could not stop {Command}", run.Command);
                Write($"停止进程失败。{error.Message}{Environment.NewLine}");
            }
            finally
            {
                CloseLog();
                lock (gate)
                {
                    run.ExitCode = exitCode;
                    run.Status = status;
                    run.FinishedAt = DateTimeOffset.UtcNow;
                }
            }
        }

        async Task CaptureAsync(StreamReader reader, string prefix, CancellationToken token)
        {
            while (await reader.ReadLineAsync(token) is { } line)
                Write(prefix + line + Environment.NewLine);
        }

        void Write(string value)
        {
            lock (gate)
            {
                run.Append(value);
                try
                {
                    log?.Write(value);
                }
                catch (IOException error)
                {
                    run.Append($"写入日志失败。{error.Message}{Environment.NewLine}");
                    CloseLog();
                }
            }
        }

        void CloseLog()
        {
            try
            {
                log?.Dispose();
            }
            catch (IOException error)
            {
                lock (gate)
                    run.Append($"关闭日志失败。{error.Message}{Environment.NewLine}");
            }
            finally
            {
                log = null;
            }
        }
    }

    private sealed class Run(string title, string executable, List<string> arguments)
    {
        private readonly Queue<string> tail = new();
        private int tailLength;
        private bool trimmed;

        public string Executable { get; } = executable;
        public IReadOnlyList<string> Arguments { get; } = arguments;
        public string Command { get; } = string.Join(" ", arguments.Prepend(executable)
            .Select(argument => argument.Any(char.IsWhiteSpace) ? $"\"{argument}\"" : argument));
        public DateTimeOffset StartedAt { get; } = DateTimeOffset.UtcNow;
        public DateTimeOffset? FinishedAt { get; set; }
        public string Status { get; set; } = "running";
        public int? ExitCode { get; set; }
        public string? LogPath { get; set; }

        public void Append(string value)
        {
            if (value.Length > TailLimit)
            {
                trimmed = true;
                value = value[^TailLimit..];
            }
            tail.Enqueue(value);
            tailLength += value.Length;
            while (tailLength > TailLimit)
            {
                trimmed = true;
                tailLength -= tail.Dequeue().Length;
            }
        }

        public RunSnapshot Snapshot() => new(title, Command, Status, StartedAt, ExitCode,
            ((FinishedAt ?? DateTimeOffset.UtcNow) - StartedAt).TotalSeconds, LogPath,
            (trimmed ? $"较早的输出见日志文件。{Environment.NewLine}" : "") + string.Concat(tail));
    }
}
