using HyperTerm.Core.Entities;

namespace HyperTerm.Core.Models;

public sealed record SessionImportSnapshot(
    IReadOnlyList<SessionFolder> Folders,
    IReadOnlyList<Session> Sessions);
