using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using AwithGameFrame.Core.Hosting;
using Newtonsoft.Json;

namespace AwithGameFrame.Data.Diagnostics
{
    /// <summary>Optional JSONL lifecycle sink, owned independently of any host.</summary>
    public sealed class HostEventJournal : IDisposable
    {
        private readonly object sync = new object();
        private readonly HashSet<GameHost> hosts = new HashSet<GameHost>();
        private readonly FileStream stream;
        private readonly StreamWriter writer;
        private bool disposed;
        private Exception lastError;

        public HostEventJournal(string path, params GameHost[] hosts)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("An explicit journal path is required.", nameof(path));
            var fullPath = Path.GetFullPath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
            stream = new FileStream(fullPath, FileMode.Append, FileAccess.Write, FileShare.Read);
            writer = new StreamWriter(stream, new UTF8Encoding(false), 4096, leaveOpen: true) { AutoFlush = true };
            try { if (hosts != null) foreach (var host in hosts) Attach(host); }
            catch { Dispose(); throw; }
        }

        public Exception LastError { get { lock (sync) return lastError; } }

        public void Attach(GameHost host)
        {
            if (host == null) throw new ArgumentNullException(nameof(host));
            lock (sync)
            {
                if (disposed) throw new ObjectDisposedException(nameof(HostEventJournal));
                if (hosts.Add(host)) host.Lifecycle += Record;
            }
        }

        private void Record(HostEvent value)
        {
            lock (sync)
            {
                if (disposed) return;
                try
                {
                    writer.WriteLine(JsonConvert.SerializeObject(new
                    {
                        hostId = value.HostId, phase = value.Phase.ToString(), status = value.Status.ToString(),
                        moduleId = value.ModuleId, elapsedSeconds = value.ElapsedSeconds, timestampUtc = value.TimestampUtc,
                        state = value.State.ToString(), isRollback = value.IsRollback,
                        errorType = value.Error?.GetType().FullName, errorMessage = value.Error?.Message
                    }, Formatting.None));
                }
                catch (Exception error) { lastError = error; }
            }
        }

        public void Dispose()
        {
            lock (sync)
            {
                if (disposed) return;
                disposed = true;
                foreach (var host in hosts) host.Lifecycle -= Record;
                hosts.Clear();
                try { writer.Dispose(); }
                catch (Exception error) { lastError = error; }
                finally
                {
                    try { stream.Dispose(); }
                    catch (Exception error) { lastError = error; }
                }
            }
        }
    }
}
