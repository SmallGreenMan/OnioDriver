using System;

namespace AltairAp300.Driver;

/// Exception thrown when attempting to send a command while the driver is not connected to the AP-3000 device.
public class AltairNotConnectedException : InvalidOperationException
{
    public AltairNotConnectedException(string message = "TCP client is not connected to any AP-3000 device.")
        : base(message)
    {
    }
}
