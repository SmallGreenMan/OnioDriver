using System;
using System.Threading.Tasks;
using Xunit;

namespace AltairAp300.Driver.Tests;

public class AltairDriverTests
{
    [Fact]
    public async Task ConnectAndDisconnect_ShouldRaiseConnectionEvents()
    {
        var fakeClient = new FakeTcpClient();
        using var driver = new AltairDriver(client: fakeClient);

        bool connectedFired = false;
        bool disconnectedFired = false;
        bool disconectedPromptSpellingFired = false;

        driver.Connected += () => connectedFired = true;
        driver.Disconnected += () => disconnectedFired = true;
        driver.Disconected += () => disconectedPromptSpellingFired = true;

        await driver.ConnectAsync("127.0.0.1", 5100);

        Assert.True(driver.IsConnected);
        Assert.True(connectedFired);

        await driver.DisconnectAsync();

        Assert.False(driver.IsConnected);
        Assert.True(disconnectedFired);
        Assert.True(disconectedPromptSpellingFired);
    }

    [Fact]
    public async Task SetPowerAsync_ShouldSendCorrectCommandAndRaiseEvent()
    {
        var fakeClient = new FakeTcpClient
        {
            AutoResponseHandler = cmd => cmd == "SYS:1;" ? "ACK" : "NAK:10"
        };
        using var driver = new AltairDriver(client: fakeClient);
        await driver.ConnectAsync("127.0.0.1");

        bool eventRaised = false;
        PowerState powerState = PowerState.Off;
        driver.PowerStateChanged += state =>
        {
            eventRaised = true;
            powerState = state;
        };

        await driver.SetPowerAsync(true);

        Assert.Single(fakeClient.SentCommands, "SYS:1;");
        Assert.Equal(PowerState.SwitchingOn, driver.Power);
        Assert.True(eventRaised);
        Assert.Equal(PowerState.SwitchingOn, powerState);
    }

    [Fact]
    public async Task QueryPowerAsync_ShouldReturnPowerStateAndUpdateState()
    {
        var fakeClient = new FakeTcpClient
        {
            AutoResponseHandler = cmd => cmd == "SYS:?;" ? "SYS:1" : "NAK:10"
        };
        using var driver = new AltairDriver(client: fakeClient);
        await driver.ConnectAsync("127.0.0.1");

        PowerState result = await driver.QueryPowerAsync();

        Assert.Single(fakeClient.SentCommands, "SYS:?;");
        Assert.Equal(PowerState.On, result);
        Assert.Equal(PowerState.On, driver.Power);
    }

    [Fact]
    public async Task UnsolicitedEvents_ShouldUpdatePowerState_WhenRdyOrStbyReceived()
    {
        var fakeClient = new FakeTcpClient();
        using var driver = new AltairDriver(client: fakeClient);
        await driver.ConnectAsync("127.0.0.1");

        bool readyFired = false;
        bool standbyFired = false;

        driver.Ready += () => readyFired = true;
        driver.Standby += () => standbyFired = true;

        fakeClient.SimulateIncomingData("!RDY");

        Assert.Equal(PowerState.On, driver.Power);
        Assert.True(readyFired);

        fakeClient.SimulateIncomingData("!STBY");

        Assert.Equal(PowerState.Off, driver.Power);
        Assert.True(standbyFired);
    }

    [Fact]
    public async Task SetSourceAsync_ShouldSendCorrectCommandAndRaiseEvent()
    {
        var fakeClient = new FakeTcpClient
        {
            AutoResponseHandler = cmd => cmd == "SRC:3;" ? "ACK" : "NAK:10"
        };
        using var driver = new AltairDriver(client: fakeClient);
        await driver.ConnectAsync("127.0.0.1");

        int receivedSource = 0;
        driver.SourceStateChanged += src => receivedSource = src;

        await driver.SetSourceAsync(3);

        Assert.Single(fakeClient.SentCommands, "SRC:3;");
        Assert.Equal(3, driver.Source);
        Assert.Equal(3, receivedSource);
    }

    [Fact]
    public async Task QuerySourceAsync_ShouldReturnSourceValue()
    {
        var fakeClient = new FakeTcpClient
        {
            AutoResponseHandler = cmd => cmd == "SRC:?;" ? "SRC:2" : "NAK:10"
        };
        using var driver = new AltairDriver(client: fakeClient);
        await driver.ConnectAsync("127.0.0.1");

        int source = await driver.QuerySourceAsync();

        Assert.Equal(2, source);
        Assert.Equal(2, driver.Source);
    }

    [Fact]
    public async Task SetLightOutputAsync_ShouldSendCorrectCommandAndRaiseEvent()
    {
        var fakeClient = new FakeTcpClient
        {
            AutoResponseHandler = cmd => cmd == "LGT:85;" ? "ACK" : "NAK:10"
        };
        using var driver = new AltairDriver(client: fakeClient);
        await driver.ConnectAsync("127.0.0.1");

        int output = 0;
        driver.lightOutputStateChanged += val => output = val;

        await driver.SetLightOutputAsync(85);

        Assert.Single(fakeClient.SentCommands, "LGT:85;");
        Assert.Equal(85, driver.lightOutput);
        Assert.Equal(85, driver.LightOutput);
        Assert.Equal(85, output);
    }

    [Fact]
    public async Task QueryLightOutputAsync_ShouldReturnLightOutputScaledFrom255()
    {
        var fakeClient = new FakeTcpClient
        {
            AutoResponseHandler = cmd => cmd == "LGT:?;" ? "LGT:128" : "NAK:10"
        };
        using var driver = new AltairDriver(client: fakeClient);
        await driver.ConnectAsync("127.0.0.1");

        int lgt = await driver.QueryLightOutputAsync();

        Assert.Equal(50, lgt);
        Assert.Equal(50, driver.lightOutput);
    }

    [Fact]
    public async Task SetShutterAsync_ShouldSendCorrectCommandAndRaiseEvent()
    {
        var fakeClient = new FakeTcpClient
        {
            AutoResponseHandler = cmd => cmd == "SHT:1;" ? "ACK" : "NAK:10"
        };
        using var driver = new AltairDriver(client: fakeClient);
        await driver.ConnectAsync("127.0.0.1");

        bool shutterState = false;
        driver.ShutterStateChanged += state => shutterState = state;

        await driver.SetShutterAsync(true);

        Assert.Single(fakeClient.SentCommands, "SHT:1;");
        Assert.True(driver.Shutter);
        Assert.True(shutterState);
    }

    [Fact]
    public async Task QueryShutterAsync_ShouldReturnShutterState()
    {
        var fakeClient = new FakeTcpClient
        {
            AutoResponseHandler = cmd => cmd == "SHT:?;" ? "SHT:0" : "NAK:10"
        };
        using var driver = new AltairDriver(client: fakeClient);
        await driver.ConnectAsync("127.0.0.1");

        bool shutter = await driver.QueryShutterAsync();

        Assert.False(shutter);
        Assert.False(driver.Shutter);
    }

    [Fact]
    public async Task SendCommand_ShouldThrowException_WhenNakErrorReceived()
    {
        var fakeClient = new FakeTcpClient
        {
            AutoResponseHandler = _ => "NAK:20"
        };
        using var driver = new AltairDriver(client: fakeClient);
        await driver.ConnectAsync("127.0.0.1");

        await Assert.ThrowsAsync<InvalidOperationException>(() => driver.SetLightOutputAsync(80));
    }

    [Fact]
    public void Constructor_ShouldAcceptCustomParameters()
    {
        var fakeClient = new FakeTcpClient();
        using var driver = new AltairDriver(ipAddress: "192.168.1.100", port: 5105, commandTimeout: 10, commandRetries: 3, autoReconnect: true, client: fakeClient);

        Assert.Equal("192.168.1.100", driver.IpAddress);
        Assert.Equal(5105, driver.Port);
        Assert.Equal(10, driver.CommandTimeout);
        Assert.Equal(3, driver.CommandRetries);
        Assert.True(driver.AutoReconnect);
        Assert.True(driver.AutoReconect);
    }

    [Fact]
    public async Task AutoReconnect_ShouldAttemptReconnect_OnUnexpectedDisconnect()
    {
        var fakeClient = new FakeTcpClient();
        using var driver = new AltairDriver(autoReconnect: true, client: fakeClient);

        await driver.ConnectAsync("127.0.0.1");
        Assert.True(driver.IsConnected);

        // Simulate unexpected disconnection from server/transport
        await fakeClient.DisconnectAsync();
        Assert.False(driver.IsConnected);

        // Wait for auto-reconnect attempt (1s delay)
        await Task.Delay(1500);

        Assert.True(driver.IsConnected);
    }
}
