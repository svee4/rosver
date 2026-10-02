using System.Diagnostics.CodeAnalysis;

namespace Rosver;

public sealed class ConfigurationException : Exception
{
    private ConfigurationException(string message) : base(message) { }

    public static ConfigurationException CreateMissingKey(string key) =>
        new($"Expected configuration value '{key}' was not present in the configuration");

    [DoesNotReturn]
    public static void ThrowMissingKey(string key) =>
        throw CreateMissingKey(key);

    public static ConfigurationException CreateUnparsable<T>(string key) where T : IParsable<T> =>
        new($"Unable to parse configuration value '{key}' into {typeof(T).FullName}");

    [DoesNotReturn]
    public static void ThrowUnparsable<T>(string key) where T : IParsable<T> =>
        throw CreateUnparsable<T>(key);
}

public static class ConfigurationExtensions
{
    public static string GetRequiredValue(this IConfiguration configuration, string key)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrEmpty(key);

        var value = configuration[key];
        if (string.IsNullOrEmpty(value))
        {
            ConfigurationException.ThrowMissingKey(key);
        }

        return value;
    }

    public static T GetRequiredParsedValue<T>(
        this IConfiguration configuration,
        string key,
        IFormatProvider? formatProvider = null) where T : IParsable<T>
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var stringValue = configuration.GetRequiredValue(key);

        if (!T.TryParse(stringValue, formatProvider, out var parsedValue))
        {
            ConfigurationException.ThrowUnparsable<T>(key);
        }

        return parsedValue;
    }
}