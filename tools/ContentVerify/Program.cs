using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using AwithGameFrame.Networking.Content;

namespace AwithGameFrame.Tools.ContentVerification
{
    /// <summary>Local HTTP acceptance tool for actual published engine files. Not a production HTTP server.</summary>
    internal static class Program
    {
        private static async Task<int> Main(string[] arguments)
        {
            if (arguments.Length == 3 && arguments[0] == "sign")
            {
                var bytes = File.ReadAllBytes(arguments[1]);
                ContentManifest.FromJson(bytes);
                using var signer = ReadKey(arguments[2]);
                var signature = signer.SignData(bytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                using var file = new FileStream(Path.ChangeExtension(arguments[1], ".sig"), FileMode.CreateNew);
                file.Write(signature);
                return 0;
            }
            if (arguments.Length is not (3 or 4))
                throw new ArgumentException("Usage: ContentVerify <published-directory> <installation-directory> <report.json> [public-key.pem]");
            string root = NormalizePath(arguments[0]);
            string installation = NormalizePath(arguments[1]);
            string reportPath = NormalizePath(arguments[2]);
            if (ContainsPath(root, installation) || ContainsPath(installation, root))
            {
                Console.Error.WriteLine("Published and installation directories must be separate trees.");
                return 1;
            }
            if (ContainsPath(root, reportPath) || ContainsPath(installation, reportPath))
            {
                Console.Error.WriteLine("The report must be outside the published and installation trees.");
                return 1;
            }
            using var cancellation = new CancellationTokenSource();
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var endpoint = (IPEndPoint)listener.LocalEndpoint;
            var uri = new Uri("http://127.0.0.1:" + endpoint.Port + "/");
            int requests = 0;
            var server = ServeAsync(listener, root, cancellation.Token, () => Interlocked.Increment(ref requests));
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
                var bytes = await http.GetByteArrayAsync(new Uri(uri, "content-manifest.json"));
                ContentManifest manifest;
                bool signatureVerified = arguments.Length == 4;
                if (signatureVerified)
                {
                    using var key = ReadKey(arguments[3]);
                    var signature = await http.GetByteArrayAsync(new Uri(uri, "content-manifest.sig"));
                    manifest = ContentManifest.FromSignedJson(bytes, signature, key);
                }
                else manifest = ContentManifest.FromJson(bytes);
                await using var updater = new ContentUpdateService(installation);
                var snapshot = await updater.UpdateAsync(manifest, uri);
                var current = updater.OpenCurrent();
                if (current == null || current.Manifest.Fingerprint != manifest.Fingerprint) throw new InvalidDataException("Installed content pointer differs.");
                foreach (var file in manifest.Files)
                    if (!File.Exists(snapshot.GetPath(file.Key))) throw new FileNotFoundException("Installed content file is missing: " + file.Key);
                var report = new { version = manifest.Version, fingerprint = manifest.Fingerprint,
                    directory = snapshot.Directory, files = manifest.Files.Count, requests, signatureVerified, transport = "localhost HTTP" };
                File.WriteAllText(reportPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
                Console.WriteLine("CONTENT_HTTP_INSTALL_PASSED files=" + manifest.Files.Count + " requests=" + requests + " directory=" + snapshot.Directory);
                return 0;
            }
            finally
            {
                cancellation.Cancel(); listener.Stop();
                try { await server; }
                catch (OperationCanceledException) { }
                catch (SocketException) when (cancellation.IsCancellationRequested) { }
                catch (ObjectDisposedException) when (cancellation.IsCancellationRequested) { }
            }
        }

        private static RSA ReadKey(string path)
        {
            var key = RSA.Create();
            try
            {
                key.ImportFromPem(File.ReadAllText(path));
                if (key.KeySize < 2048) throw new CryptographicException("Use an RSA key of at least 2048 bits.");
                return key;
            }
            catch { key.Dispose(); throw; }
        }

        private static string NormalizePath(string path)
        {
            string fullPath = Path.GetFullPath(path);
            return fullPath.Length == Path.GetPathRoot(fullPath).Length ? fullPath
                : fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        private static bool ContainsPath(string directory, string path)
        {
            var comparison = Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if (string.Equals(directory, path, comparison)) return true;
            string prefix = directory.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
                ? directory : directory + Path.DirectorySeparatorChar;
            return path.StartsWith(prefix, comparison);
        }

        private static async Task ServeAsync(TcpListener listener, string root, CancellationToken token, Action served)
        {
            while (!token.IsCancellationRequested)
            {
                using var client = await listener.AcceptTcpClientAsync(token);
                using var stream = client.GetStream();
                using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, leaveOpen: true);
                var line = await reader.ReadLineAsync(token);
                if (line == null) continue;
                var request = line.Split(' ');
                string header;
                int headerBytes = line.Length;
                do
                {
                    header = await reader.ReadLineAsync(token);
                    headerBytes += header?.Length ?? 0;
                    if (headerBytes > 16384) throw new InvalidDataException("HTTP acceptance request is too large.");
                } while (!string.IsNullOrEmpty(header));
                string relative = request.Length == 3 ? Uri.UnescapeDataString(request[1].TrimStart('/')) : "";
                string path = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
                if (request.Length != 3 || request[0] != "GET" ||
                    !ContainsPath(root, path) ||
                    !File.Exists(path))
                {
                    var rejected = Encoding.ASCII.GetBytes("HTTP/1.1 404 Not Found\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
                    await stream.WriteAsync(rejected, token);
                    continue;
                }
                using var input = File.OpenRead(path);
                var response = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: " + input.Length + "\r\nConnection: close\r\n\r\n");
                await stream.WriteAsync(response, token);
                await input.CopyToAsync(stream, token);
                served();
            }
        }
    }
}
