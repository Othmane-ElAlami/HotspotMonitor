using HotspotMonitorService;

var builder = Host.CreateApplicationBuilder(args);

if (OperatingSystem.IsWindows()) builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
