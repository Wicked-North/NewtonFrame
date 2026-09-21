namespace RoggenCore.Core;

public static class RecoveryConfirmation
{
    public static string RequiredPhrase(ulong deviceId) =>
        deviceId == 0 ? "ERASE LOCKED NRF52811" : $"ERASE {deviceId:X16}";

    public static void Validate(string supplied, ulong deviceId)
    {
        if (!string.Equals(supplied.Trim(), RequiredPhrase(deviceId), StringComparison.Ordinal))
            throw new InvalidOperationException("Recovery erase confirmation did not match the connected target.");
    }
}
