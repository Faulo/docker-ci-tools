using System.Diagnostics;
using System.Security;

namespace CiTools;

sealed class RuntimeCredentials {
    static readonly CredentialPair[] pairs = [
        new("Email", "EMAIL_CREDENTIALS_USR", "EMAIL_CREDENTIALS_PSW"),
        new("Steam", "STEAM_CREDENTIALS_USR", "STEAM_CREDENTIALS_PSW")
    ];

    readonly IReadOnlyDictionary<string, string> values;

    RuntimeCredentials(IReadOnlyDictionary<string, string> values) {
        this.values = values;
    }

    internal static RuntimeCredentials empty { get; } = new(new Dictionary<string, string>(StringComparer.Ordinal));

    internal static RuntimeCredentials Resolve() =>
        Resolve(Environment.GetEnvironmentVariable, File.ReadAllText);

    internal static RuntimeCredentials Resolve(
        Func<string, string?> readEnvironment,
        Func<string, string> readFile) {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in pairs) {
            ResolveValue(pair.user, values, readEnvironment, readFile);
            ResolveValue(pair.password, values, readEnvironment, readFile);

            bool hasUser = values.ContainsKey(pair.user);
            bool hasPassword = values.ContainsKey(pair.password);
            if (hasUser != hasPassword) {
                throw new InvalidOperationException(
                    $"{pair.description} credentials require both {pair.user} and {pair.password} after resolving direct and _FILE inputs.");
            }
        }

        return values.Count == 0 ? empty : new RuntimeCredentials(values);
    }

    internal void ApplyTo(ProcessStartInfo startInfo) {
        foreach (var pair in pairs) {
            ApplyValue(startInfo.Environment, pair.user);
            ApplyValue(startInfo.Environment, pair.password);
        }
    }

    static void ResolveValue(
        string name,
        IDictionary<string, string> values,
        Func<string, string?> readEnvironment,
        Func<string, string> readFile) {
        string fileName = name + "_FILE";
        string? directValue = readEnvironment(name);
        string? path = readEnvironment(fileName);
        if (directValue is not null && path is not null) {
            throw new InvalidOperationException($"{name} and {fileName} cannot both be set.");
        }

        string? value = directValue;
        if (path is not null) {
            if (string.IsNullOrWhiteSpace(path)) {
                throw new InvalidOperationException($"{fileName} must name a credential file.");
            }

            try {
                value = readFile(path).TrimEnd('\r', '\n');
            } catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SecurityException or ArgumentException or NotSupportedException) {
                throw new InvalidOperationException(
                    $"Could not read the credential file configured by {fileName} at '{Program.SanitizeText(path, 512)}'.",
                    exception);
            }

            if (value.Length == 0) {
                throw new InvalidOperationException($"The credential file configured by {fileName} is empty.");
            }
        }

        if (!string.IsNullOrEmpty(value)) {
            values[name] = value;
        }
    }

    void ApplyValue(IDictionary<string, string?> environment, string name) {
        environment.Remove(name);
        environment.Remove(name + "_FILE");
        if (values.TryGetValue(name, out string? value)) {
            environment[name] = value;
        }
    }

    sealed record CredentialPair(string description, string user, string password);
}
