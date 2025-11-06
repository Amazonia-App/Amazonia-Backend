using AmazoniaApi.Core.Models;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace AmazoniaApi.Server.DBContext;

public class AppDbContext(DbContextOptions<AppDbContext> options) : IdentityDbContext<AppUser>(options)
{
    public DbSet<Ticket> Tickets { get; set; }
    public DbSet<Message> Messages { get; set; }
    public DbSet<Transaction> Transactions { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Configure AppUser
        modelBuilder.Entity<AppUser>(entity =>
        {
            // Index on MinecraftUsername if used in queries
            entity.HasIndex(u => u.MinecraftUsername)
                .HasDatabaseName("IX_AppUser_MinecraftUsername");
        });

        // Configure Ticket to AppUser relationship (many-to-one, using explicit foreign key CreatorId)
        modelBuilder.Entity<Ticket>()
            .HasOne<AppUser>()
            .WithMany()
            .HasForeignKey(t => t.CreatorId)
            .OnDelete(DeleteBehavior.Restrict); // Prevent deleting user if they created tickets

        // Configure Ticket to Message relationship (one-to-many, one-way navigation, using shadow foreign key TicketId on Message)
        modelBuilder.Entity<Ticket>()
            .HasMany(t => t.Messages)
            .WithOne()
            .HasForeignKey("TicketId")
            .OnDelete(DeleteBehavior.Cascade); // Messages are deleted when ticket is deleted

        // Configure Message to AppUser relationship (many-to-one, using explicit foreign key SenderId)
        modelBuilder.Entity<Message>()
            .HasOne<AppUser>()
            .WithMany()
            .HasForeignKey(m => m.SenderId)
            .OnDelete(DeleteBehavior.Restrict); // Prevent deleting user if they sent messages

        // Indexes for Transaction queries
        modelBuilder.Entity<Transaction>(entity =>
        {
            entity.HasIndex(t => t.SenderId).HasDatabaseName("IX_Transaction_SenderId");
            entity.HasIndex(t => t.ReceiverId).HasDatabaseName("IX_Transaction_ReceiverId");
            entity.Property(t => t.Timestamp).HasConversion(
                v => v,
                v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
        });
    }
}