using MiControl.Service;

// Maintenance mode used by uninstall.ps1 before the service is removed.
if (args.Contains("--reset-defaults"))
{
    using var mifs = new MifsClient();
    bool charge = mifs.SetChargeLevel(ChargeLevel.Percent100);
    bool perf = mifs.SetPerfMode(PerfMode.Smart);
    Console.WriteLine($"reset charge->100%: {charge}, perf->Smart: {perf}");
    return;
}

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddWindowsService(options => options.ServiceName = "MiControlService");

builder.Services.AddSingleton<StateStore>();
builder.Services.AddSingleton<MifsClient>();
builder.Services.AddHostedService<IpcServer>();
builder.Services.AddHostedService<ChargeGuard>();
builder.Services.AddHostedService<HotkeyListener>();

var host = builder.Build();

try
{
    host.Run();
}
catch (Exception ex)
{
    var file = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "MiControl", "service-crash.txt");
    Directory.CreateDirectory(Path.GetDirectoryName(file)!);
    File.WriteAllText(file, $"{DateTime.Now:O}{Environment.NewLine}{ex}");
    throw;
}
