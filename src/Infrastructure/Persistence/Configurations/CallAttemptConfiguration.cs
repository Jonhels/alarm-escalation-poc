using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

public class CallAttemptConfiguration : IEntityTypeConfiguration<CallAttemptEntity>
{
    public void Configure(EntityTypeBuilder<CallAttemptEntity> builder)
    {
        builder.ToTable("call_attempts");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .ValueGeneratedNever();

        builder.Property(e => e.PhoneNumber)
            .IsRequired()
            .HasMaxLength(20);

        builder.Property(e => e.AlarmMessage)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(e => e.Status)
            .IsRequired()
            .HasMaxLength(20);

        builder.Property(e => e.TwilioCallSid)
            .HasMaxLength(100);

        builder.Property(e => e.CreatedAt)
            .IsRequired();

        builder.HasIndex(e => e.Status);
        builder.HasIndex(e => e.CreatedAt);
        builder.HasIndex(e => e.TwilioCallSid)
            .IsUnique()
            .HasFilter(null);
    }
}
