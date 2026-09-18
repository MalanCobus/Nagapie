using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Nagapie.BraindumpLite.Api.Data;

public sealed class NagapieDbContext(DbContextOptions<NagapieDbContext> options)
    : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<UserDocument> UserDocuments => Set<UserDocument>();
    public DbSet<SavedBrainDump> BrainDumps => Set<SavedBrainDump>();
    public DbSet<Thought> Thoughts => Set<Thought>();
    public DbSet<UserCategory> Categories => Set<UserCategory>();
    public DbSet<UserDraft> Drafts => Set<UserDraft>();
    public DbSet<RelationalAccount> RelationalAccounts => Set<RelationalAccount>();

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
            entity.HasIndex(dump => new { dump.UserId, dump.SavedAtUtc, dump.Id });
            if (Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite")
                entity.Property(dump => dump.SavedAtUtc).HasConversion(value => value.UtcTicks, value => new DateTimeOffset(value, TimeSpan.Zero));
        });
        builder.Entity<RelationalAccount>(entity =>
        {
            entity.HasKey(row => row.UserId);
            entity.Property(row => row.Epoch).IsConcurrencyToken();
            entity.HasOne<ApplicationUser>().WithOne().HasForeignKey<RelationalAccount>(row => row.UserId);
        });
        builder.Entity<UserCategory>(entity =>
        {
            entity.HasKey(row => new { row.UserId, row.Id });
            entity.Property(row => row.Version).IsConcurrencyToken();
            entity.Property(row => row.CustomName).HasMaxLength(30);
            entity.Property(row => row.ColorToken).HasMaxLength(16);
            entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(row => row.UserId);
        });
        builder.Entity<UserDraft>(entity =>
        {
            entity.HasKey(row => row.UserId);
            entity.Property(row => row.Version).IsConcurrencyToken();
            entity.Property(row => row.Text).HasMaxLength(5000);
            entity.HasOne<ApplicationUser>().WithOne().HasForeignKey<UserDraft>(row => row.UserId);
        });
        builder.Entity<Thought>(entity =>
        {
            entity.HasKey(row => new { row.UserId, row.Id });
            entity.Property(row => row.Version).IsConcurrencyToken();
            entity.Property(row => row.Text).HasMaxLength(5000);
            entity.Property(row => row.PlanningHorizon).HasMaxLength(16);
            entity.Property(row => row.PlannedDate).HasColumnType("date");
            entity.Property(row => row.CompletionReason).HasMaxLength(16);
            entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(row => row.UserId);
            entity.HasOne<UserCategory>().WithMany().HasForeignKey(row => new { row.UserId, row.CategoryId })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<SavedBrainDump>().WithMany().HasForeignKey(row => new { row.UserId, row.SourceDumpId })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(row => new { row.UserId, row.CompletionReason, row.PlanningHorizon, row.CreatedAtUtc });
        });
    }
}
