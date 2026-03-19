namespace CmfBudsService;

/// <summary>
/// Thrown when a Bluetooth RFCOMM connection attempt fails.
/// </summary>
public sealed class BluetoothConnectionException : Exception
{
    public BluetoothConnectionException(string message) : base(message) { }
    public BluetoothConnectionException(string message, Exception inner) : base(message, inner) { }
}
