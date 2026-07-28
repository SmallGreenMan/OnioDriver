using System;

namespace AltairAp300.Driver;

/// Exception thrown when the Altair AP-3000 projector returns a NAK error response.
public class AltairNakException : InvalidOperationException
{
    /// Gets the raw NAK error code (e.g., "10", "20", "30").
    public string NakCode { get; }

    public AltairNakException(string nakCode, string description)
        : base($"Projector returned NAK:{nakCode} - {description}")
    {
        NakCode = nakCode;
    }

    /// Parses NAK response string (e.g., "NAK:10") and creates corresponding AltairNakException.
    public static AltairNakException FromResponse(string response)
    {
        string nakCode = response.StartsWith("NAK:", StringComparison.OrdinalIgnoreCase)
            ? response.Substring(4).Trim()
            : response.Trim();

        string description = nakCode switch
        {
            "10" => "Unrecognized command",
            "20" => "Parameter error",
            "30" => "Command is available only if device is on",
            _ => $"Unknown NAK error code '{nakCode}'"
        };

        return new AltairNakException(nakCode, description);
    }
}
