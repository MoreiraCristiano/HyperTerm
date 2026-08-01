using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using HyperTerm.Core.Entities;

namespace HyperTerm.Infrastructure.Persistence.Configurations;

internal sealed class SessionFolderConfiguration : IEntityTypeConfiguration<SessionFolder>
{
    public void Configure(EntityTypeBuilder<SessionFolder> builder)
    {
        builder.ToTable("SessionFolders");
        builder.HasKey(folder => folder.Id);
        builder.Property(folder => folder.Path).HasMaxLength(500).UseCollation("NOCASE").IsRequired();
        builder.Property(folder => folder.CreatedAt).IsRequired();
        builder.HasIndex(folder => folder.Path).IsUnique();
    }
}
