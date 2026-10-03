using System.Diagnostics;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Rosver;

public sealed record SdkRoslynPair
{
    public required string Sdk { get; init; }

    public required string Roslyn { get; init; }
}

public sealed partial class RosverBackgroundService(
    IServiceScopeFactory serviceScopeFactory,
    IConfiguration configuration,
    IHostEnvironment hostEnvironment,
    ILogger<RosverBackgroundService> logger) : BackgroundService
{
    private readonly IServiceScopeFactory _serviceScopeFactory = serviceScopeFactory;
    private readonly ILogger<RosverBackgroundService> _logger = logger;

    /// <summary>
    ///     Maps an SDK version to a Roslyn version.
    /// </summary>
    private Dictionary<string, string>? _map;

    private string CacheFilePath { get; } = hostEnvironment.IsDevelopment()
        ? (configuration["CacheFilePath"] ?? AppContext.BaseDirectory + "./cache.json")
        : configuration.GetRequiredValue("CacheFilePath");

    private TimeSpan CacheTime { get; } = configuration.GetRequiredParsedValue<TimeSpan>("CacheTime");

    public DateTimeOffset? LastUpdate { get; private set; }

    public IReadOnlyDictionary<string, string>? Map => _map;

    public IEnumerable<SdkRoslynPair> GetRoslynVersions(string sdkVersion)
    {
        var map = _map;

        if (map is null)
        {
            throw new RosverException("Map not initialized", HttpStatusCode.ServiceUnavailable);
        }

        var matchingVersions = map
            .Where(kv => kv.Key.StartsWith(sdkVersion, StringComparison.Ordinal))
            .Select(kv => new SdkRoslynPair() { Sdk = kv.Key, Roslyn = kv.Value });

        return matchingVersions;
    }

    public IEnumerable<SdkRoslynPair> GetSdkVersions(string roslynVersion)
    {
        var map = _map;

        if (map is null)
        {
            throw new RosverException("Map not initialized", HttpStatusCode.ServiceUnavailable);
        }

        var sdkVersions = map
            .Where(kv => kv.Value.StartsWith(roslynVersion, StringComparison.Ordinal))
            .Select(kv => new SdkRoslynPair() { Sdk = kv.Key, Roslyn = kv.Value });

        return sdkVersions;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        RosverOtlp.Meter.CreateObservableGauge(
            "rosver.map.entries",
            () => _map?.Count ?? -1,
            description: "Number of SDK to Roslyn version mappings");

        RosverOtlp.Meter.CreateObservableGauge(
            "rosver.cache.age",
            () => LastUpdate is { } lastUpdate ? (DateTimeOffset.UtcNow - lastUpdate).TotalSeconds : -1,
            unit: "s",
            description: "Age of the cached SDK to Roslyn version mapping in seconds");

        while (!stoppingToken.IsCancellationRequested)
        {
            LastUpdate = File.Exists(CacheFilePath)
                ? DateTime.SpecifyKind(File.GetLastWriteTimeUtc(CacheFilePath), DateTimeKind.Utc)
                : null;

            try
            {
                await Run(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error in {nameof(Run)}()");
            }

            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
    }

    private async Task Run(CancellationToken token)
    {
        if (LastUpdate is not null && DateTimeOffset.UtcNow < LastUpdate.Value.Add(CacheTime))
        {
            try
            {
                var json = await File.ReadAllTextAsync(CacheFilePath, token);
                _map = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                return;
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Error reading cache, refreshing");
                LastUpdate = null;
            }
        }

        using var activity = RosverOtlp.ActivitySource.StartActivity("RefreshMap");
        var startTime = Stopwatch.GetTimestamp();

        try
        {
            await using var scope = _serviceScopeFactory.CreateAsyncScope();

            var httpClientFactory = scope.ServiceProvider.GetRequiredService<IHttpClientFactory>();
            var httpClient = httpClientFactory.CreateClient();
            httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Rosver");

            var tags = new List<string>();
            for (int page = 1; ; page++)
            {
                var json = await httpClient.GetStringAsync($"https://api.github.com/repos/dotnet/dotnet/tags?per_page=100&page={page}", token);

                using var doc = JsonDocument.Parse(json);
                using var arr = doc.RootElement.EnumerateArray();

                var tags2 = arr.Select(e => e.GetProperty("name").GetString()!).ToList();

                if (tags2.Count == 0)
                {
                    break;
                }

                tags.AddRange(tags2);
            }

            activity?.SetTag("rosver.tags.total", tags.Count);

            var sdkTags = tags.Where(x => ValidSdkVersionRegex().IsMatch(x)).ToList();

            activity?.SetTag("rosver.tags.valid", sdkTags.Count);

            var result = new System.Collections.Concurrent.ConcurrentDictionary<string, string>();

            await Parallel.ForEachAsync(
                sdkTags,
                new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = token, },
                async (tag, ct) =>
                {
                    var manifestUrl = $"https://raw.githubusercontent.com/dotnet/dotnet/{tag}/src/source-manifest.json";

                    string str;

                    try
                    {
                        str = await httpClient.GetStringAsync(manifestUrl, ct);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error fetching {url}", manifestUrl);
                        return;
                    }

                    using var doc = JsonDocument.Parse(str);

                    string? roslynCommit = null;

                    foreach (var repo in doc.RootElement.GetProperty("repositories").EnumerateArray())
                    {
                        if (repo.GetProperty("path").GetString() == "roslyn")
                        {
                            roslynCommit = repo.GetProperty("commitSha").GetString();
                        }
                    }

                    if (roslynCommit is null)
                    {
                        return;
                    }

                    var roslynPropsUrl = $"https://raw.githubusercontent.com/dotnet/roslyn/{roslynCommit}/eng/Versions.props";

                    var roslynXml = await httpClient.GetStringAsync(roslynPropsUrl, token);
                    var roslynDoc = XDocument.Parse(roslynXml);

                    var properties = roslynDoc.Descendants("PropertyGroup")
                        .Elements()
                        .Where(e =>
                            e.Name == "MajorVersion" ||
                            e.Name == "MinorVersion" ||
                            e.Name == "PatchVersion")
                        .ToDictionary(e => e.Name.LocalName, e => e.Value);

                    if (properties.Count != 3)
                    {
                        _logger.LogWarning("Missing version properties for {tag}", tag);
                        return;
                    }

                    result.TryAdd(
                        tag,
                        $"{properties["MajorVersion"]}.{properties["MinorVersion"]}.{properties["PatchVersion"]}");                  
                });

            var map = result.OrderBy(kv => kv.Key).ToDictionary(kv => kv.Key, kv => kv.Value);

            activity?.SetTag("rosver.map.count", map.Count);

            if (File.Exists(CacheFilePath))
            {
                File.Move(CacheFilePath, CacheFilePath + "2", overwrite: true);
            }

            File.WriteAllText(CacheFilePath, JsonSerializer.Serialize(map));

            LastUpdate = DateTimeOffset.UtcNow;
            _map = map;
        }
        catch (Exception e)
        {
            activity?.AddException(e);
            RosverOtlp.RefreshFailures.Add(1);
            throw;
        }
        finally
        {
            var elapsed = Stopwatch.GetElapsedTime(startTime);
            RosverOtlp.RefreshDuration.Record(elapsed.TotalSeconds);
        }
    }

    [GeneratedRegex(
        @"^v\d+\.\d+\.\d{3}(-(rc|preview).+)?$",
        RegexOptions.None,
        matchTimeoutMilliseconds: 200)]
    private static partial Regex ValidSdkVersionRegex();
}
