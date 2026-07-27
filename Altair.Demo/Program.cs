using System;
using System.Threading.Tasks;
using AltairAp300.Driver;

Console.WriteLine("---> Altair AP-3000 Demo Started <---");

var ipAddress = args.Length > 0 ? args[0] : "10.211.55.3";
var port = args.Length > 1 && int.TryParse(args[1], out var p) ? p : 5100;

using var driver = new AltairDriver(ipAddress, port);

// Subscribe to driver state change events
driver.PowerStateChanged += state => Console.WriteLine($"[EVENT] Power state changed: {state}");
driver.SourceStateChanged += src => Console.WriteLine($"[EVENT] Source state changed: {src}");
driver.lightOutputStateChanged += val => Console.WriteLine($"[EVENT] Light output state changed: {val}");
driver.ShutterStateChanged += shutter => Console.WriteLine($"[EVENT] Shutter state changed: {shutter}");
driver.Connected += () => Console.WriteLine("[EVENT] Connected to AP-3000 Projector");
driver.Disconected += () => Console.WriteLine("[EVENT] Disconnected from AP-3000 Projector");

Console.WriteLine($"Driver initialized for {driver.IpAddress}:{driver.Port}. Target FW Version: {driver.FirmwareVersion}");

try
{
    Console.WriteLine($"Attempting connection to {driver.IpAddress}:{driver.Port}...");
    await driver.ConnectAsync();
    Console.WriteLine($"IsConnected: {driver.IsConnected}");

    // Query initial states
    await driver.QueryPowerAsync();
    await driver.QuerySourceAsync();
    await driver.QueryLightOutputAsync();
    await driver.QueryShutterAsync();
}
catch (Exception ex)
{
    Console.WriteLine($"[INFO] Connection attempt result: {ex.Message}");
}

Console.WriteLine("---> Altair AP-3000 Demo Finished <---");