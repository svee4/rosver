using Microsoft.AspNetCore.HttpOverrides;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Rosver;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<RosverBackgroundService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<RosverBackgroundService>());

builder.Services.AddHttpClient();

builder.Services.Configure<OpenTelemetryLoggerOptions>(options =>
{
    options.IncludeScopes = true;
    options.IncludeFormattedMessage = true;
});

builder.Logging.ClearProviders();

builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService(serviceName: RosverOtlp.ServiceName))
    .WithTracing(tracing => tracing
        .AddSource(RosverOtlp.ActivitySourceName)
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddOtlpExporter())
    .WithMetrics(metrics => metrics
        .AddMeter(RosverOtlp.MeterName)
        .AddRuntimeInstrumentation()
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddOtlpExporter());

builder.Logging.AddOpenTelemetry(logging =>
{
    logging.AddOtlpExporter();
});

builder.Logging.AddSimpleConsole();

builder.Services.AddExceptionHandler<RosverExceptionHandler>();

builder.Services.AddRazorPages();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();

    var options = new ForwardedHeadersOptions()
    {
        ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
        ForwardLimit = 1,
    };

    // allow all networks because the app is ran exclusively behind a single trusted proxy
    // and ForwardLimit is 1
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
    _ = app.UseForwardedHeaders(options);
}

app.UseHttpsRedirection();

app.UseRouting();

app.UseAuthorization();

app.MapStaticAssets();

app.MapRazorPages()
   .WithStaticAssets();

app.Run();
