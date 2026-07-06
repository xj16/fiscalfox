using FiscalFox.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FiscalFox.Api.Data;

public class FiscalFoxDbContext : DbContext
{
    public FiscalFoxDbContext(DbContextOptions<FiscalFoxDbContext> options) : base(options)
    {
    }

    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<Instrument> Instruments => Set<Instrument>();
    public DbSet<PriceBar> PriceBars => Set<PriceBar>();
    public DbSet<Holding> Holdings => Set<Holding>();
    public DbSet<Transaction> Transactions => Set<Transaction>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Account>(e =>
        {
            e.Property(a => a.Name).HasMaxLength(120).IsRequired();
            e.Property(a => a.Currency).HasMaxLength(3).IsRequired();
            e.Property(a => a.CashBalance).HasPrecision(18, 4);
        });

        b.Entity<Instrument>(e =>
        {
            e.Property(i => i.Symbol).HasMaxLength(32).IsRequired();
            e.Property(i => i.Name).HasMaxLength(160);
            e.Property(i => i.Currency).HasMaxLength(3);
            e.HasIndex(i => i.Symbol).IsUnique();
        });

        b.Entity<PriceBar>(e =>
        {
            e.Property(p => p.Open).HasPrecision(18, 6);
            e.Property(p => p.High).HasPrecision(18, 6);
            e.Property(p => p.Low).HasPrecision(18, 6);
            e.Property(p => p.Close).HasPrecision(18, 6);
            e.HasOne(p => p.Instrument)
                .WithMany(i => i.Prices)
                .HasForeignKey(p => p.InstrumentId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(p => new { p.InstrumentId, p.Date }).IsUnique();
        });

        b.Entity<Holding>(e =>
        {
            e.Property(h => h.Quantity).HasPrecision(18, 8);
            e.Property(h => h.AverageCost).HasPrecision(18, 6);
            e.Property(h => h.TargetWeight).HasPrecision(9, 6);
            e.HasOne(h => h.Account)
                .WithMany(a => a.Holdings)
                .HasForeignKey(h => h.AccountId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(h => h.Instrument)
                .WithMany(i => i.Holdings)
                .HasForeignKey(h => h.InstrumentId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(h => new { h.AccountId, h.InstrumentId }).IsUnique();
        });

        b.Entity<Transaction>(e =>
        {
            e.Property(t => t.Quantity).HasPrecision(18, 8);
            e.Property(t => t.Price).HasPrecision(18, 6);
            e.Property(t => t.CashImpact).HasPrecision(18, 4);
            e.Property(t => t.Fee).HasPrecision(18, 4);
            e.Property(t => t.Note).HasMaxLength(500);
            e.HasOne(t => t.Account)
                .WithMany(a => a.Transactions)
                .HasForeignKey(t => t.AccountId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(t => t.Instrument)
                .WithMany()
                .HasForeignKey(t => t.InstrumentId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
