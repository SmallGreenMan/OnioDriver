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
        PowerState? powerState = PowerState.Off;
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
    public async Task PowerOn_And_PowerOff_Methods_ShouldSendCorrectCommands()
    {
        var fakeClient = new FakeTcpClient
        {
            AutoResponseHandler = cmd => cmd is "SYS:1;" or "SYS:0;" or "SYS:?;" ? "ACK" : "NAK:10"
        };
        using var driver = new AltairDriver(client: fakeClient);
        await driver.ConnectAsync("127.0.0.1");

        await driver.PowerOnAsync();
        Assert.Contains("SYS:1;", fakeClient.SentCommands);

        await driver.PowerOffAsync();
        Assert.Contains("SYS:0;", fakeClient.SentCommands);
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

        PowerState? result = await driver.QueryPowerAsync();

        Assert.Contains("SYS:?;", fakeClient.SentCommands);
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
            AutoResponseHandler = cmd => cmd switch
            {
                "SRC:3;" => "ACK",
                "SRC:?;" => "SRC:3",
                _ => "NAK:10"
            }
        };
        using var driver = new AltairDriver(client: fakeClient);
        await driver.ConnectAsync("127.0.0.1");

        int? receivedSource = 0;
        driver.SourceStateChanged += src => receivedSource = src;

        await driver.SetSourceAsync(3);

        Assert.Contains("SRC:3;", fakeClient.SentCommands);
        Assert.Contains("SRC:?;", fakeClient.SentCommands);
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

        int? source = await driver.QuerySourceAsync();

        Assert.Equal(2, source);
        Assert.Equal(2, driver.Source);
    }

    [Fact]
    public async Task SetLightOutputAsync_ShouldSendCorrectCommandAndRaiseEvent()
    {
        var fakeClient = new FakeTcpClient
        {
            AutoResponseHandler = cmd => cmd switch
            {
                "LGT:85;" => "ACK",
                "LGT:?;" => "LGT:217",
                _ => "NAK:10"
            }
        };
        using var driver = new AltairDriver(client: fakeClient);
        await driver.ConnectAsync("127.0.0.1");

        int? output = 0;
        driver.lightOutputStateChanged += val => output = val;

        await driver.SetLightOutputAsync(85);

        Assert.Contains("LGT:85;", fakeClient.SentCommands);
        Assert.Contains("LGT:?;", fakeClient.SentCommands);
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

        int? lgt = await driver.QueryLightOutputAsync();

        Assert.Equal(50, lgt);
        Assert.Equal(50, driver.lightOutput);
    }

    [Fact]
    public async Task SetShutterAsync_ShouldSendCorrectCommandAndRaiseEvent()
    {
        var fakeClient = new FakeTcpClient
        {
            AutoResponseHandler = cmd => cmd switch
            {
                "SHT:1;" => "ACK",
                "SHT:?;" => "SHT:1",
                _ => "NAK:10"
            }
        };
        using var driver = new AltairDriver(client: fakeClient);
        await driver.ConnectAsync("127.0.0.1");

        bool? shutterState = false;
        driver.ShutterStateChanged += state => shutterState = state;

        await driver.SetShutterAsync(true);

        Assert.Contains("SHT:1;", fakeClient.SentCommands);
        Assert.Contains("SHT:?;", fakeClient.SentCommands);
        Assert.Equal(true, driver.Shutter);
        Assert.Equal(true, shutterState);
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

        bool? shutter = await driver.QueryShutterAsync();

        Assert.Equal(false, shutter);
        Assert.Equal(false, driver.Shutter);
    }

    [Theory]
    [InlineData("NAK:10", "10", "Unrecognized command")]
    [InlineData("NAK:20", "20", "Parameter error")]
    [InlineData("NAK:30", "30", "Command is available only if device is on")]
    public void AltairNakException_FromResponse_ShouldCreateCorrectCodeAndDescription(string nakResponse, string expectedCode, string expectedDescription)
    {
        var ex = AltairNakException.FromResponse(nakResponse);

        Assert.Equal(expectedCode, ex.NakCode);
        Assert.Contains(expectedCode, ex.Message);
        Assert.Contains(expectedDescription, ex.Message);
    }

    [Fact]
    public async Task SendCommand_ShouldHandleNotConnectedGracefully_WithoutCrashing()
    {
        var fakeClient = new FakeTcpClient { AutoGreeting = false };
        using var driver = new AltairDriver(client: fakeClient);

        // Calling query/control while disconnected logs error without throwing unhandled crash
        var state = await driver.QueryPowerAsync();
        Assert.Equal(PowerState.Off, state);
    }

    [Fact]
    public async Task ConnectAsync_ShouldThrowException_WhenDenyReceivedAndAutoReconnectDisabled()
    {
        var fakeClient = new FakeTcpClient
        {
            SimulateDenyOnConnect = true
        };
        using var driver = new AltairDriver(autoReconnect: false, client: fakeClient);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => driver.ConnectAsync("127.0.0.1"));

        Assert.Equal("[CONNECT] Connection denied: Another client is Connected", ex.Message);
        Assert.False(driver.IsConnected);
    }

    [Fact]
    public async Task ConnectAsync_ShouldRetryReconnect_WhenDenyReceivedAndAutoReconnectEnabled()
    {
        var fakeClient = new FakeTcpClient
        {
            SimulateDenyOnConnect = true
        };
        using var driver = new AltairDriver(autoReconnect: true, client: fakeClient);
        driver.InitialRecoveryReconnectDelaySeconds = 1;

        // Start ConnectAsync in task
        Task connectTask = driver.ConnectAsync("127.0.0.1");

        // Allow initial deny attempt to process
        await Task.Delay(200);

        // Switch fake client to allow successful connection on next retry
        fakeClient.SimulateDenyOnConnect = false;

        // Wait for retry loop to complete
        await Task.WhenAny(connectTask, Task.Delay(2500));

        Assert.True(connectTask.IsCompletedSuccessfully);
        Assert.True(driver.IsConnected);
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

    [Fact]
    public async Task NonPowerCommands_ShouldWait_DuringTransitionState()
    {
        var fakeClient = new FakeTcpClient
        {
            AutoResponseHandler = cmd => cmd switch
            {
                "SYS:1;" => "ACK",
                "SRC:2;" => "ACK",
                "SRC:?;" => "SRC:2",
                _ => "NAK:10"
            }
        };
        using var driver = new AltairDriver(client: fakeClient);
        await driver.ConnectAsync("127.0.0.1");

        // Send power on -> power state transitions to SwitchingOn
        await driver.SetPowerAsync(true);
        Assert.Equal(PowerState.SwitchingOn, driver.Power);

        // Start non-power command in task
        Task sourceTask = driver.SetSourceAsync(2);
        Assert.False(sourceTask.IsCompleted);

        // Simulate device state becoming Ready (!RDY)
        fakeClient.SimulateIncomingData("!RDY");

        await sourceTask;
        Assert.Equal(PowerState.On, driver.Power);
        Assert.Equal(2, driver.Source);
    }

    [Fact]
    public async Task DeviceIdentification_ShouldUpdateFwAndQueryAllStates_StartingWithSysPower()
    {
        var fakeClient = new FakeTcpClient
        {
            AutoResponseHandler = cmd => cmd switch
            {
                "SYS:?;" => "SYS:1",
                "SRC:?;" => "SRC:2",
                "LGT:?;" => "LGT:128",
                "SHT:?;" => "SHT:0",
                _ => "ACK"
            }
        };
        using var driver = new AltairDriver(client: fakeClient);
        await driver.ConnectAsync("127.0.0.1");

        fakeClient.SimulateIncomingData("!ID:AP-3000:1.07");

        // Allow async QueryAllStatesTask to run
        await Task.Delay(200);

        Assert.Equal("1.07", driver.FW);
        Assert.Equal("1.07", driver.FirmwareVersion);

        // Verify sent commands started with SYS:?;
        Assert.True(fakeClient.SentCommands.Count >= 4);
        Assert.Equal("SYS:?;", fakeClient.SentCommands[0]);
        Assert.Contains("SRC:?;", fakeClient.SentCommands);
        Assert.Contains("LGT:?;", fakeClient.SentCommands);
        Assert.Contains("SHT:?;", fakeClient.SentCommands);
    }

    [Fact]
    public async Task MultiThreaded_ConcurrentCommands_ShouldExecuteSafely_WithoutRaceConditions()
    {
        var fakeClient = new FakeTcpClient
        {
            AutoResponseHandler = cmd => cmd switch
            {
                "SYS:?;" => "SYS:1",
                "SRC:?;" => "SRC:3",
                "LGT:?;" => "LGT:255",
                "SHT:?;" => "SHT:0",
                "SRC:1;" => "ACK",
                "SRC:2;" => "ACK",
                "SRC:3;" => "ACK",
                "SRC:4;" => "ACK",
                "LGT:50;" => "ACK",
                "SHT:1;" => "ACK",
                _ => "ACK"
            }
        };
        using var driver = new AltairDriver(client: fakeClient);
        await driver.ConnectAsync("127.0.0.1");

        // Set power to ON state first
        fakeClient.SimulateIncomingData("!RDY");

        // Spawn 40 parallel tasks across multiple ThreadPool threads calling driver concurrently
        var tasks = new List<Task>();
        for (int i = 0; i < 40; i++)
        {
            int index = i;
            tasks.Add(Task.Run(async () =>
            {
                switch (index % 5)
                {
                    case 0:
                        await driver.QueryPowerAsync();
                        break;
                    case 1:
                        await driver.SetSourceAsync((index % 4) + 1);
                        break;
                    case 2:
                        await driver.QuerySourceAsync();
                        break;
                    case 3:
                        await driver.SetLightOutputAsync(50);
                        break;
                    case 4:
                        await driver.QueryShutterAsync();
                        break;
                }
            }));
        }

        await Task.WhenAll(tasks);

        Assert.Equal(PowerState.On, driver.Power);
        Assert.True(fakeClient.SentCommands.Count >= 40);
    }

    [Fact]
    public async Task CommandsDuringInitialStateQuery_ShouldWaitUntilInitialQueryCompletes()
    {
        var fakeClient = new FakeTcpClient
        {
            AutoGreeting = false,
            AutoResponseHandler = cmd => cmd switch
            {
                "SYS:?;" => "SYS:1",
                "SRC:?;" => "SRC:1",
                "LGT:?;" => "LGT:255",
                "SHT:?;" => "SHT:0",
                "SRC:2;" => "ACK",
                _ => "ACK"
            }
        };
        using var driver = new AltairDriver(client: fakeClient);
        await driver.ConnectAsync("127.0.0.1");

        // Trigger initial state query via !ID:
        fakeClient.SimulateIncomingData("!ID:AP-3000:1.07");

        // Immediately attempt user command during initial query
        await driver.SetSourceAsync(2);

        // Verify sent commands order: initial query commands come first, then SRC:2;
        Assert.Equal("SYS:?;", fakeClient.SentCommands[0]);
        Assert.Equal("SRC:?;", fakeClient.SentCommands[1]);
        Assert.Equal("LGT:?;", fakeClient.SentCommands[2]);
        Assert.Equal("SHT:?;", fakeClient.SentCommands[3]);
        Assert.Equal("SRC:2;", fakeClient.SentCommands[4]);
    }

    [Fact]
    public async Task DeviceIsReady_ShouldFireTrueAfterInitialQuery_AndFalseOnDisconnect()
    {
        var fakeClient = new FakeTcpClient
        {
            AutoResponseHandler = cmd => cmd switch
            {
                "SYS:?;" => "SYS:1",
                "SRC:?;" => "SRC:1",
                "LGT:?;" => "LGT:255",
                "SHT:?;" => "SHT:0",
                _ => "ACK"
            }
        };
        using var driver = new AltairDriver(client: fakeClient);

        bool? readyState = null;
        driver.DeviceIsReadyChanged += isReady => readyState = isReady;

        await driver.ConnectAsync("127.0.0.1");

        // Simulate initial ID response
        fakeClient.SimulateIncomingData("!ID:AP-3000:1.07");

        await Task.Delay(200);

        Assert.True(driver.DeviceIsReady);
        Assert.True(readyState);

        await driver.DisconnectAsync();

        Assert.False(driver.DeviceIsReady);
        Assert.False(readyState);
    }

    [Fact]
    public async Task RemoteDisconnect_ShouldResetDeviceIsReady_AndTriggerAutoReconnect()
    {
        var fakeClient = new FakeTcpClient
        {
            AutoResponseHandler = cmd => cmd switch
            {
                "SYS:?;" => "SYS:1",
                "SRC:?;" => "SRC:1",
                "LGT:?;" => "LGT:255",
                "SHT:?;" => "SHT:0",
                _ => "ACK"
            }
        };
        using var driver = new AltairDriver(client: fakeClient, autoReconnect: true)
        {
            InitialRecoveryReconnectDelaySeconds = 1
        };

        bool? readyState = null;
        driver.DeviceIsReadyChanged += isReady => readyState = isReady;

        await driver.ConnectAsync("127.0.0.1");

        // Allow initial query to complete
        await Task.Delay(200);
        Assert.True(driver.DeviceIsReady);
        Assert.True(readyState);

        // Simulate unexpected connection drop from remote device side
        fakeClient.SimulateRemoteDisconnect();

        Assert.False(driver.DeviceIsReady);
        Assert.False(readyState);

        // Wait for auto-reconnect task to reconnect
        await Task.Delay(1300);

        Assert.True(fakeClient.IsConnected);
        Assert.True(driver.DeviceIsReady);
    }

    [Fact]
    public async Task InitialStates_ShouldBeNull_AndResetToNullOnDisconnect()
    {
        var fakeClient = new FakeTcpClient
        {
            AutoResponseHandler = cmd => cmd switch
            {
                "SYS:?;" => "SYS:1",
                "SRC:?;" => "SRC:2",
                "LGT:?;" => "LGT:255",
                "SHT:?;" => "SHT:0",
                _ => "ACK"
            }
        };
        using var driver = new AltairDriver(client: fakeClient);

        // Verify initial state values are null before connection & query
        Assert.Null(driver.Power);
        Assert.Null(driver.Source);
        Assert.Null(driver.LightOutput);
        Assert.Null(driver.Shutter);

        await driver.ConnectAsync("127.0.0.1");
        fakeClient.SimulateIncomingData("!ID:AP-3000:1.07");
        await Task.Delay(200);

        // Verify states were populated
        Assert.Equal(PowerState.On, driver.Power);
        Assert.Equal(2, driver.Source);
        Assert.Equal(100, driver.LightOutput);
        Assert.Equal(false, driver.Shutter);

        // Disconnect and verify states reset to null
        await driver.DisconnectAsync();

        Assert.Null(driver.Power);
        Assert.Null(driver.Source);
        Assert.Null(driver.LightOutput);
        Assert.Null(driver.Shutter);
    }
}
