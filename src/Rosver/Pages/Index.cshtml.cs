using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Rosver.Pages;

public class IndexModel(RosverBackgroundService rosverBackgroundService) : PageModel
{
    private readonly RosverBackgroundService _rosverBackgroundService = rosverBackgroundService;

    public DateTimeOffset? LastUpdate { get; private set; }

    public IReadOnlyDictionary<string, string>? Map { get; private set; }

    public string? SdkInput { get; private set; }

    public string? RoslynInput { get; private set; }

    public IReadOnlyList<SdkRoslynPair>? RoslynVersions { get; private set; }

    public IReadOnlyList<SdkRoslynPair>? SdkVersions { get; private set; }

    public IReadOnlyList<SdkRoslynPair>? All { get; private set; }

    public string? ErrorMessage { get; private set; }

    public OpenGraphDto OpenGraph { get; private set; } = new();

    public void OnGet(
        string? sdk,
        string? roslyn)
    {
        LastUpdate = _rosverBackgroundService.LastUpdate;
        Map = _rosverBackgroundService.Map;
        SdkInput = sdk;
        RoslynInput = roslyn;

        ViewData["OpenGraph"] = OpenGraph;

        if (Map is null)
        {
            ErrorMessage = "Mapping not currently available, please try again later.";
            return;
        }

        if (sdk is not null && roslyn is not null)
        {
            ErrorMessage = "Provide either an SDK version or a Roslyn version, not both.";

            OpenGraph.Title = "Invalid request";
            OpenGraph.Description = ErrorMessage;
          
            return;
        }

        if (sdk is not null)
        {
            RoslynVersions = _rosverBackgroundService
                .GetRoslynVersions(sdk)
                .OrderDescending(RoslynComparer.Instance)
                .ToList();

            OpenGraph.Title = $"Roslyn versions for SDK {sdk}";
            OpenGraph.Description = string.Join(", ", RoslynVersions.Select(pair => pair.Roslyn));
        }
        else if (roslyn is not null)
        {
            SdkVersions = _rosverBackgroundService
                .GetSdkVersions(roslyn)
                .OrderDescending(SdkComparer.Instance)
                .ToList();

            OpenGraph.Title = $"SDK versions for Roslyn {roslyn}";
            OpenGraph.Description = string.Join(", ", SdkVersions.Select(pair => pair.Sdk));
            
        }
        else
        {
            All = _rosverBackgroundService.Map?
                .Select(kv => new SdkRoslynPair() { Sdk = kv.Key, Roslyn = kv.Value })
                .OrderDescending(SdkComparer.Instance)
                .ToList();

            OpenGraph.Title = "Map SDK versions to Roslyn versions and vice versa";
        }
    }
}
