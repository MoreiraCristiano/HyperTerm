namespace HyperTerm.Core.Models;

public sealed record ApplicationUpdateInfo(
    Version Version,
    Uri ReleasePageUri,
    Uri PackageUri,
    Uri ChecksumUri,
    string PackageName);

public sealed record ApplicationUpdateProgress(long BytesReceived, long? TotalBytes)
{
    public int Percentage => TotalBytes is > 0
        ? (int)Math.Clamp(BytesReceived * 100 / TotalBytes.Value, 0, 100)
        : 0;
}

public sealed record PreparedApplicationUpdate(
    Version Version,
    string StagingDirectory,
    string ExecutablePath);
