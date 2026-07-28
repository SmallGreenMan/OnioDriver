namespace AltairAp300.Driver;

/// Represents the power state of the Altair AP-3000 projector.
public enum PowerState
{
    /// Device is powered off / standby (0).
    Off = 0,

    /// Device is powered on and operational (1).
    On = 1,

    /// Device is switching on / warming up (2).
    SwitchingOn = 2,

    /// Device is switching off / cooling down (3).
    SwitchingOff = 3
}
