using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using HyperTerm.Core.Entities;

namespace HyperTerm.Infrastructure.Persistence.Configurations;

internal sealed class SessionConfiguration : IEntityTypeConfiguration<Session>
{
    public void Configure(EntityTypeBuilder<Session> builder)
    {
        builder.ToTable("Sessions");

        builder.HasKey(session => session.Id);
        builder.Property(session => session.Id).ValueGeneratedNever();

        builder.Property(session => session.Name).HasMaxLength(200).IsRequired();
        builder.Property(session => session.Host).HasMaxLength(253).IsRequired();
        builder.Property(session => session.Port).IsRequired();
        builder.Property(session => session.Username).HasMaxLength(128).IsRequired();
        builder.Property(session => session.PrivateKey).HasMaxLength(1024);
        builder.Property(session => session.Folder).HasMaxLength(500).IsRequired();
        builder.Property(session => session.Notes).HasMaxLength(4000);
        builder.Property(session => session.CreatedAt).IsRequired();
        builder.Property(session => session.UpdatedAt).IsRequired();

        builder.HasIndex(session => session.Folder);
        builder.HasIndex(session => session.Name);
    }
}
