using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Nagapie.BraindumpLite.Api.Data;

public sealed class NagapieDbContext(DbContextOptions<NagapieDbContext> options)
    : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<UserDocument> UserDocuments => Set<UserDocument>();
    public DbSet<SavedBrainDump> BrainDumps => Set<SavedBrainDump>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.Entity<UserDocument>(entity =>
        {
            entity.HasKey(document => new { document.UserId, document.Key });
            entity.Property(document => document.Key).HasMaxLength(32);
            entity.Property(document => document.Version).IsConcurrencyToken();
            entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(document => document.UserId);
        });
        builder.Entity<SavedBrainDump>(entity =>
        {
            entity.HasKey(dump => new { dump.UserId, dump.Id });
            entity.Property(dump => dump.Text).HasMaxLength(5000);
            entity.Property(dump => dump.InputMethod).HasMaxLength(16);
            entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(dump => dump.UserId);
        });
    }
}

