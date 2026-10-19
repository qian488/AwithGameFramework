using System;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading.Tasks;
using AwithGameFrame.Foundation.DataPersistence;
using NUnit.Framework;

namespace AwithGameFrame.Tests.Unit
{
    [TestFixture]
    public class SerializerCompatibilityTests
    {
        [TestCase(SerializationFormat.Json, false)]
        [TestCase(SerializationFormat.Binary, false)]
        [TestCase(SerializationFormat.Binary, true)]
        public async Task CurrentCodecs_ShouldRoundTripPrimitivesAndDtoFields(SerializationFormat format, bool compressed)
        {
            var serializer = SerializerFactory.CreateSerializer(format, new SerializationConfig { EnableCompression = compressed });
            var previousCulture = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
                await AssertRoundTrip(serializer, true);
                await AssertRoundTrip(serializer, 134);
                await AssertRoundTrip(serializer, long.MinValue);
                await AssertRoundTrip(serializer, ulong.MaxValue);
                await AssertRoundTrip(serializer, 1.25f);
                await AssertRoundTrip(serializer, 1.25d);
                await AssertRoundTrip(serializer, "literal \\n; newline \n; quote \"");
                var bytes = await serializer.SerializeAsync(new Profile { Level = 8, Name = "玩家", Inventory = new[] { 2, 5 } });
                var loaded = await serializer.DeserializeAsync<Profile>(bytes);
                Assert.That(loaded.Level, Is.EqualTo(8));
                Assert.That(loaded.Name, Is.EqualTo("玩家"));
                Assert.That(loaded.Inventory, Is.EqualTo(new[] { 2, 5 }));
                Assert.That(serializer.IsValidData(bytes), Is.True);
                if (format == SerializationFormat.Binary) Assert.That(BitConverter.ToInt32(bytes, 0), Is.EqualTo(2));
            }
            finally { CultureInfo.CurrentCulture = previousCulture; }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task BinaryCodec_ShouldReadVersionOnePrimitiveSemantics(bool compressed)
        {
            var serializer = new CompressedJsonSerializer();
            Assert.That(await serializer.DeserializeAsync<bool>(VersionOneEnvelope("True", compressed)), Is.True);
            Assert.That(await serializer.DeserializeAsync<string>(VersionOneEnvelope("\"C:\\new\"", compressed)), Is.EqualTo(@"C:\new"));
            var previousCulture = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
                Assert.That(await serializer.DeserializeAsync<double>(VersionOneEnvelope("1,25", compressed)), Is.EqualTo(1.25d));
            }
            finally { CultureInfo.CurrentCulture = previousCulture; }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task JsonFactory_ShouldReadLegacyBinaryAndBase64Saves(bool compressed)
        {
            var serializer = SerializerFactory.CreateDefaultSerializer();
            var legacy = VersionOneEnvelope("\"C:\\new\"", compressed);
            Assert.That(await serializer.DeserializeAsync<string>(legacy), Is.EqualTo(@"C:\new"));
            Assert.That(await serializer.DeserializeFromStringAsync<string>(Convert.ToBase64String(legacy)), Is.EqualTo(@"C:\new"));
            var current = await serializer.SerializeToStringAsync("new save");
            Assert.That(current, Is.EqualTo("\"new save\""));
            Assert.Throws<Newtonsoft.Json.JsonReaderException>(() => serializer.DeserializeFromStringAsync<string>("invalid plain data"));
        }

        private static async Task AssertRoundTrip<T>(ISerializer serializer, T value)
        {
            var bytes = await serializer.SerializeAsync(value);
            Assert.That(await serializer.DeserializeAsync<T>(bytes), Is.EqualTo(value));
            Assert.That(await serializer.GetSerializedSizeAsync(value), Is.EqualTo(bytes.Length));
            Assert.That(await serializer.DeserializeFromStringAsync<T>(await serializer.SerializeToStringAsync(value)), Is.EqualTo(value));
        }

        internal static byte[] VersionOneEnvelope(string content, bool compressed)
        {
            var bytes = Encoding.UTF8.GetBytes(content);
            if (compressed)
            {
                using (var output = new MemoryStream())
                {
                    using (var gzip = new GZipStream(output, CompressionMode.Compress, leaveOpen: true))
                        gzip.Write(bytes, 0, bytes.Length);
                    bytes = output.ToArray();
                }
            }
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(1);
                writer.Write(compressed);
                writer.Write(bytes.Length);
                writer.Write(bytes);
                return stream.ToArray();
            }
        }

        public class Profile
        {
            public int Level;
            public string Name;
            public int[] Inventory;
            public Profile Computed => throw new InvalidOperationException("Computed properties must not be persisted.");
        }
    }
}
