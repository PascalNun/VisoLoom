namespace VisoLoom.ReferenceServer;

internal sealed class ProtocolFailure(int status, string code, string safeMessage) : Exception(safeMessage)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
}
