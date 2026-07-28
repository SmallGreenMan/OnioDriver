using System;
using System.Threading.Tasks;
using AltairAp300.Driver;

Console.WriteLine("---> Altair AP-3000 Demo Started <---");

var ipAddress = args.Length > 0 ? args[0] : "10.211.55.3";
var port = args.Length > 1 && int.TryParse(args[1], out var p) ? p : 5100;

using var driver = new AltairDriver(ipAddress, port);
driver.Debug = true;

// Subscribe to driver state change events
driver.PowerStateChanged += state => Console.WriteLine($"[EVENT] Power state changed: {state} ({(int)state})");
driver.Ready += () => Console.WriteLine("[EVENT] Device System Ready (!RDY)");
driver.Standby += () => Console.WriteLine("[EVENT] Device System Standby (!STBY)");
driver.SourceStateChanged += src => Console.WriteLine($"[EVENT] Source state changed: {src}");
driver.lightOutputStateChanged += val => Console.WriteLine($"[EVENT] Light output state changed: {val}");
driver.ShutterStateChanged += shutter => Console.WriteLine($"[EVENT] Shutter state changed: {shutter}");
driver.Connected += () => Console.WriteLine("[EVENT] Connected to AP-3000 Projector");
driver.Disconnected += () => Console.WriteLine("[EVENT] Disconnected from AP-3000 Projector");

Console.WriteLine($"Driver initialized for {driver.IpAddress}:{driver.Port}. Target FW Version: {driver.FirmwareVersion}");

try
{
    Console.WriteLine($"Attempting connection to {driver.IpAddress}:{driver.Port}...");
    await driver.ConnectAsync();
    Console.WriteLine($"IsConnected: {driver.IsConnected}");

    await driver.QueryPowerAsync();
    Console.WriteLine($"Power is: {driver.Power}");
    
    if (driver.IsConnected && driver.Power != PowerState.On)
    {
        await driver.PowerOnAsync();
    }

    await driver.QuerySourceAsync();
    Console.WriteLine($"Source is: {driver.Source}");
    
    await driver.QueryLightOutputAsync();
    Console.WriteLine($"LightOutput is: {driver.LightOutput}");
    
    await driver.QueryShutterAsync();
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