using System;
using System.Threading;
using System.Threading.Tasks;
using AltairAp300.Driver;
using Altair.Demo;

Console.WriteLine("---> Altair AP-3000 Demo Started <---");

// When true, hosts AltairPresenter (WebSocket server on port 10001) instead of running the console demo below.
const bool DebugRect = true;

var ipAddress = args.Length > 0 ? args[0] : "localhost"; // "10.211.55.3";
var port = args.Length > 1 && int.TryParse(args[1], out var p) ? p : 5100;

if (DebugRect)
{
    await RunPresenterAsync(ipAddress, port);
    return;
}

using var driver = new AltairDriver(ipAddress, port);
driver.Debug = true;

// Subscribe to driver state change events
driver.PowerStateChanged += state => Console.WriteLine($"[EVENT] Power state changed: {state} ({(state.HasValue ? (int)state.Value : "null")})");
driver.Ready += () => Console.WriteLine("[EVENT] Device System Ready (!RDY)");
driver.Standby += () => Console.WriteLine("[EVENT] Device System Standby (!STBY)");
driver.DeviceIsReadyChanged += state => Console.WriteLine($"[EVENT] Device is Ready changed: {state}");

driver.SourceStateChanged += src => Console.WriteLine($"[EVENT] Source state changed: {src}");
driver.LightOutputStateChanged += val => Console.WriteLine($"[EVENT] Light output state changed: {val}");
driver.ShutterStateChanged += shutter => Console.WriteLine($"[EVENT] Shutter state changed: {shutter}");
driver.Connected += () => Console.WriteLine("[EVENT] Connected to AP-3000 Projector");
driver.Disconnected += () => Console.WriteLine("[EVENT] Disconnected from AP-3000 Projector");
driver.SyncEvent += (source, status) => Console.WriteLine($"[EVENT] Sync event: Source={source}, Status={status}");

Console.WriteLine($"Driver initialized for {driver.IpAddress}:{driver.Port}. Target FW Version: {driver.FirmwareVersion}");

try
{
    // This Query should fail with the massage
    await driver.QueryPowerAsync(); 
    
    Console.WriteLine($"Attempting connection to {driver.IpAddress}:{driver.Port}...");
    await driver.ConnectAsync();
    Console.WriteLine($"IsConnected: {driver.IsConnected}");
    Console.WriteLine($"FirmwareVersion: {driver.FirmwareVersion}");
    
    Console.WriteLine($"Power is: {driver.Power}");
    Console.WriteLine($"Source is: {driver.Source}");
    Console.WriteLine($"LightOutput is: {driver.LightOutput}");
    Console.WriteLine($"Shutter is: {driver.Shutter}");

    // --- on
    Console.WriteLine($"----> On section");
    
    await driver.QueryPowerAsync();
    Console.WriteLine($"Power is: {driver.Power}");
    
    if (driver.IsConnected && driver.Power != PowerState.On)
    {
        await driver.PowerOnAsync();
    }

    Console.WriteLine($"----");
    await driver.QuerySourceAsync();
    Console.WriteLine($"Source is: {driver.Source}");
    await driver.SetSourceAsync(1);
    await driver.SetSourceAsync(2);
    await driver.SetSourceAsync(3);
    await driver.SetSourceAsync(4);
    Console.WriteLine($"Source is: {driver.Source}");
    
    Console.WriteLine($"----");
    await driver.QueryLightOutputAsync();
    Console.WriteLine($"LightOutput is: {driver.LightOutput}");
    await driver.SetLightOutputAsync(0);
    await driver.SetLightOutputAsync(50);
    await driver.SetLightOutputAsync(100);
    Console.WriteLine($"LightOutput is: {driver.LightOutput}");
    
    Console.WriteLine($"----");
    await driver.QueryShutterAsync();
    Console.WriteLine($"Shutter is: {driver.Shutter}");
    await driver.SetShutterAsync(false);
    await driver.SetShutterAsync(true);
    Console.WriteLine($"Shutter is: {driver.Shutter}");
    
    // --- off
    Console.WriteLine($"----> Off section");
       
    await driver.PowerOffAsync();
    
    await driver.QueryPowerAsync();
    Console.WriteLine($"Power is: {driver.Power}");
    
    Console.WriteLine($"----");
    await driver.QuerySourceAsync();
    Console.WriteLine($"Source is: {driver.Source}");
    await driver.SetSourceAsync(3);
    await driver.SetSourceAsync(2);
    await driver.SetSourceAsync(1);
    Console.WriteLine($"Source is: {driver.Source}");
    
    Console.WriteLine($"----");
    await driver.QueryLightOutputAsync();
    Console.WriteLine($"LightOutput is: {driver.LightOutput}");
    await driver.SetLightOutputAsync(100);
    await driver.SetLightOutputAsync(50);
    await driver.SetLightOutputAsync(0);
    Console.WriteLine($"LightOutput is: {driver.LightOutput}");
    
    Console.WriteLine($"----");
    await driver.QueryShutterAsync();
    Console.WriteLine($"Shutter is: {driver.Shutter}");
    await driver.SetShutterAsync(true);
    await driver.SetShutterAsync(false);
    Console.WriteLine($"Shutter is: {driver.Shutter}");
}
catch (Exception ex)
{
    Console.WriteLine($"[INFO] Operation result: {ex.Message}");
}

Console.WriteLine("\nPress any key to exit...");
if (!Console.IsInputRedirected)
{
    Console.ReadKey(true);
}

Console.WriteLine("---> Altair AP-3000 Demo Finished <---");

static async Task RunPresenterAsync(string ipAddress, int port)
{
    await using var presenter = new AltairPresenter(ipAddress, port);
    await presenter.StartAsync();

    Console.WriteLine("Presenter running. Press any key to stop...");
    if (!Console.IsInputRedirected)
    {
        Console.ReadKey(true);
    }
    else
    {
        await Task.Delay(Timeout.Infinite);
    }

    Console.WriteLine("---> Altair AP-3000 Demo Finished <---");
}