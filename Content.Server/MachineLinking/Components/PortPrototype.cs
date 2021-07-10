using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Robust.Shared.Serialization.Manager.Attributes;

namespace Content.Server.MachineLinking.Components
{
    [DataDefinition]
    public class PortPrototype
    {
        [DataField("name", required: true)] public string Name { get; } = default!;
        [DataField("type")] public Type? Type { get; }
        /// <summary>
        /// Maximum connections of the port. 0 means infinite.
        /// </summary>
        [DataField("maxConnections")] public int MaxConnections { get; } = 0;

        public object? Signal;
    }

    public static class PortPrototypeExtensions{
        public static bool ContainsPort(this IReadOnlyList<PortPrototype> ports, string port)
        {
            foreach (var portPrototype in ports)
            {
                if (portPrototype.Name == port)
                {
                    return true;
                }
            }

            return false;
        }

        public static IEnumerable<string> GetPortStrings(this IReadOnlyList<PortPrototype> ports)
        {
            foreach (var portPrototype in ports)
            {
                yield return portPrototype.Name;
            }
        }

        public static IEnumerable<KeyValuePair<string, bool>> GetValidatedPorts(this IReadOnlyList<PortPrototype> ports, Type? validType)
        {
            foreach (var portPrototype in ports)
            {
                yield return new KeyValuePair<string, bool>(portPrototype.Name, portPrototype.Type == validType);
            }
        }

        public static bool TryGetPort(this IReadOnlyList<PortPrototype> ports, string name, [NotNullWhen(true)] out PortPrototype? port)
        {
            foreach (var portPrototype in ports)
            {
                if (portPrototype.Name == name)
                {
                    port = portPrototype;
                    return true;
                }
            }

            port = null;
            return false;
        }
    }
}
