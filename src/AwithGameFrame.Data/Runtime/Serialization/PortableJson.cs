using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace AwithGameFrame.Foundation.DataPersistence
{
    internal static class PortableJson
    {
        internal static JsonSerializerSettings CreateSettings(bool prettyPrint = false)
        {
            return new JsonSerializerSettings
            {
                Formatting = prettyPrint ? Formatting.Indented : Formatting.None,
                TypeNameHandling = TypeNameHandling.None,
                ContractResolver = new PersistedMembersResolver()
            };
        }

        // Save public fields and writable DTO properties. Computed getter-only properties
        // can recursively construct objects and were never persisted by Unity JsonUtility.
        private sealed class PersistedMembersResolver : DefaultContractResolver
        {
            protected override IList<JsonProperty> CreateProperties(Type type, MemberSerialization memberSerialization)
            {
                return base.CreateProperties(type, memberSerialization).Where(property => property.Writable).ToList();
            }
        }
    }
}
