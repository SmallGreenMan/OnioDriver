namespace AltairAp300.Driver;

/// <summary>
/// Represents the power state of the Altair AP-3000 projector.
/// </summary>
public enum PowerState
{
    /// <summary>
    /// Device is powered off / standby (0).
    /// </summary>
    Off = 0,

    /// <summary>
    /// Device is powered on and operational (1).
    /// </summary>
    On = 1,

    /// <summary>
    /// Device is switching on / warming up - approx 8 seconds (3).
    /// </summary>
    SwitchingOn = 3,

    /// <summary>
    /// Device is switching off / cooling down - approx 5 seconds (4).
    /// </summary>
    SwitchingOff = 4
}
