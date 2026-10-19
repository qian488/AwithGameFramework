using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AwithGameFrame.Networking;
using AwithGameFrame.Samples.Protocol;
using AwithGameFrame.Tools.Configuration;
using NUnit.Framework;

namespace AwithGameFrame.Tests
{
    public class ProtocolCompilerTests
    {
        private const string Schema = "{\"namespace\":\"Example.Protocol\",\"version\":1,\"endpoints\":[{\"name\":\"Read\",\"method\":\"GET\",\"path\":\"read\",\"request\":[],\"response\":[{\"name\":\"Value\",\"wireName\":\"value\",\"type\":\"int\"}]}]}";

        [Test]
        public void CompatibilityRejectsRemovedEndpointOrWireTypeChange()
        {
            Assert.Throws<InvalidDataException>(() => ProtocolCompiler.Compile(Schema.Replace("\"int\"", "\"long\""), Schema));
            Assert.Throws<InvalidDataException>(() => ProtocolCompiler.Compile(Schema.Replace("\"read\"", "\"other\""), Schema));
            Assert.DoesNotThrow(() => ProtocolCompiler.Compile(Schema, Schema));
        }

        [Test]
        public void RollingUpdateAllowsOptionalFieldsAndRejectsRequiredAdditions()
        {
            var addition = Schema.Replace("\"type\":\"int\"}", "\"type\":\"int\"},{\"name\":\"Label\",\"wireName\":\"label\",\"type\":\"string\",\"required\":false}");
            Assert.DoesNotThrow(() => ProtocolCompiler.Compile(addition, Schema));
            Assert.Throws<InvalidDataException>(() => ProtocolCompiler.Compile(addition.Replace("false", "true"), Schema));
        }

        [Test]
        public void GeneratedDtosUseActualCodecWireNames()
        {
            var codec = new DataContractJsonProtocolCodec();
            var request = codec.Encode(new RenameProfileRequest { DisplayName = "Ada" });
            Assert.That(Encoding.UTF8.GetString(request), Does.Contain("\"displayName\":\"Ada\""));
            var reply = codec.Decode<ReadProfileResponse>(Encoding.UTF8.GetBytes("{\"status\":0,\"message\":\"ok\",\"data\":{\"coins\":42}}"));
            Assert.That(reply.Data.Coins, Is.EqualTo(42));
            Assert.That(GameProtocol.ReadProfile.HasRequestBody, Is.False);
        }
    }
}
