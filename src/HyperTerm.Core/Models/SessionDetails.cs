namespace HyperTerm.Core.Models;

public sealed record SessionDetails(
    string Name,
    string Host,
    int Port,
    string Username,
    string? PrivateKey,
    string Folder,
    string? Notes);
