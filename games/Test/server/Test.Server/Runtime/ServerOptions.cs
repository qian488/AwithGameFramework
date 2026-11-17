using System;
using System.Globalization;
using System.IO;
using System.Text.Json;

namespace Test.Server
{
    internal sealed class ServerOptions
    {
        public int TickRate { get; set; } = 30;
        public int DispatchBudgetPerTick { get; set; } = 64;

        internal static ServerOptions Load(string[] args, out double? runForSeconds)
        {
            string path = Path.Combine(AppContext.BaseDirectory, "server.json");
            runForSeconds = null;
            for (int i = 0; i < args.Length; i++)
            {
                string key = args[i];
                if (i + 1 >= args.Length) throw new ArgumentException($"Missing value for '{key}'.");
                string value = args[++i];
                switch (key)
                {
                    case "--config": path = Path.GetFullPath(value); break;
                    case "--run-for-seconds":
                        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds)
                            || !double.IsFinite(seconds) || seconds <= 0)
                            throw new ArgumentException("--run-for-seconds must be a finite positive number.");
                        runForSeconds = seconds;
                        break;
                    default: throw new ArgumentException($"Unknown option '{key}'.");
                }
            }

            var options = JsonSerializer.Deserialize<ServerOptions>(File.ReadAllText(path), new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow
            }) ?? throw new InvalidDataException("Server configuration cannot be null.");
            if (options.TickRate < 1 || options.TickRate > 240)
                throw new InvalidDataException("TickRate must be between 1 and 240.");
            if (options.DispatchBudgetPerTick < 1 || options.DispatchBudgetPerTick > 65536)
                throw new InvalidDataException("DispatchBudgetPerTick must be between 1 and 65536.");
            return options;
        }
    }
}
