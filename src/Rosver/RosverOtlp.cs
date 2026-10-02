using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Rosver;

public static class RosverOtlp
{
    public const string ServiceName = "Rosver";
    public const string ActivitySourceName = "Rosver";
    public const string MeterName = "Rosver";

    public static readonly ActivitySource ActivitySource = new(ActivitySourceName);
    public static readonly Meter Meter = new(MeterName);

    public static readonly Histogram<double> RefreshDuration = Meter.CreateHistogram<double>(
        "rosver.refresh.duration", unit: "s", description: "Duration of a full map refresh");

    public static readonly Counter<long> RefreshFailures = Meter.CreateCounter<long>(
        "rosver.refresh.failures", description: "Failed map refreshes");
}