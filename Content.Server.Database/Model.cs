using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Net;
using System.Text.Json;
using Content.Shared.Database;
using Microsoft.EntityFrameworkCore;

namespace Content.Server.Database
{
    public abstract class ServerDbContext : DbContext
    {
        protected ServerDbContext(DbContextOptions options) : base(options)
        {
        }

        public DbSet<Preference> Preference { get; set; } = null!;
        public DbSet<Profile> Profile { get; set; } = null!;
        public DbSet<AssignedUserId> AssignedUserId { get; set; } = null!;
        public DbSet<Player> Player { get; set; } = default!;
        public DbSet<Admin> Admin { get; set; } = null!;
        public DbSet<AdminRank> AdminRank { get; set; } = null!;
        public DbSet<Round> Round { get; set; } = null!;
        public DbSet<Server> Server { get; set; } = null!;
        public DbSet<AdminLog> AdminLog { get; set; } = null!;
        public DbSet<AdminLogPlayer> AdminLogPlayer { get; set; } = null!;
        public DbSet<Whitelist> Whitelist { get; set; } = null!;
        public DbSet<ServerBan> Ban { get; set; } = default!;
        public DbSet<ServerUnban> Unban { get; set; } = default!;
        public DbSet<ConnectionLog> ConnectionLog { get; set; } = default!;
        public DbSet<ServerBanHit> ServerBanHit { get; set; } = default!;
        public DbSet<ServerRoleBan> RoleBan { get; set; } = default!;
        public DbSet<ServerRoleUnban> RoleUnban { get; set; } = default!;
        public DbSet<UploadedResourceLog> UploadedResourceLog { get; set; } = default!;
        public DbSet<AdminNote> AdminNotes { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Preference>()
                .HasIndex(p => p.UserId)
                .IsUnique();

            modelBuilder.Entity<Profile>()
                .HasIndex(p => new {p.Slot, PrefsId = p.PreferenceId})
                .IsUnique();

            modelBuilder.Entity<Antag>()
                .HasIndex(p => new {HumanoidProfileId = p.ProfileId, p.AntagName})
                .IsUnique();

            modelBuilder.Entity<Job>()
                .HasIndex(j => j.ProfileId);

            modelBuilder.Entity<Job>()
                .HasIndex(j => j.ProfileId, "IX_job_one_high_priority")
                .IsUnique()
                .HasFilter("priority = 3");

            modelBuilder.Entity<Job>()
                .HasIndex(j => new { j.ProfileId, j.JobName })
                .IsUnique();

            modelBuilder.Entity<AssignedUserId>()
                .HasIndex(p => p.UserName)
                .IsUnique();

            // Can't have two usernames with the same user ID.
            modelBuilder.Entity<AssignedUserId>()
                .HasIndex(p => p.UserId)
                .IsUnique();

            modelBuilder.Entity<Admin>()
                .HasOne(p => p.AdminRank)
                .WithMany(p => p!.Admins)
                .OnDelete(DeleteBehavior.SetNull);

            modelBuilder.Entity<AdminFlag>()
                .HasIndex(f => new {f.Flag, f.AdminId})
                .IsUnique();

            modelBuilder.Entity<AdminRankFlag>()
                .HasIndex(f => new {f.Flag, f.AdminRankId})
                .IsUnique();

            modelBuilder.Entity<AdminLog>()
                .HasKey(log => new {log.Id, log.RoundId});

            modelBuilder.Entity<AdminLog>()
                .Property(log => log.Id)
                .ValueGeneratedOnAdd();

            modelBuilder.Entity<AdminLog>()
                .HasIndex(log => log.Date);

            modelBuilder.Entity<AdminLogPlayer>()
                .HasOne(player => player.Player)
                .WithMany(player => player.AdminLogs)
                .HasForeignKey(player => player.PlayerUserId)
                .HasPrincipalKey(player => player.UserId);

            modelBuilder.Entity<AdminLogPlayer>()
                .HasKey(logPlayer => new {logPlayer.PlayerUserId, logPlayer.LogId, logPlayer.RoundId});

            modelBuilder.Entity<ServerBan>()
                .HasIndex(p => p.UserId);

            modelBuilder.Entity<ServerBan>()
                .HasIndex(p => p.Address);

            modelBuilder.Entity<ServerBan>()
                .HasIndex(p => p.UserId);

            modelBuilder.Entity<ServerUnban>()
                .HasIndex(p => p.BanId)
                .IsUnique();

            modelBuilder.Entity<ServerBan>()
                .HasCheckConstraint("HaveEitherAddressOrUserIdOrHWId", "address IS NOT NULL OR user_id IS NOT NULL OR hwid IS NOT NULL");

            modelBuilder.Entity<ServerRoleBan>()
                .HasIndex(p => p.UserId);

            modelBuilder.Entity<ServerRoleBan>()
                .HasIndex(p => p.Address);

            modelBuilder.Entity<ServerRoleBan>()
                .HasIndex(p => p.UserId);

            modelBuilder.Entity<ServerRoleUnban>()
                .HasIndex(p => p.BanId)
                .IsUnique();

            modelBuilder.Entity<ServerRoleBan>()
                .HasCheckConstraint("HaveEitherAddressOrUserIdOrHWId", "address IS NOT NULL OR user_id IS NOT NULL OR hwid IS NOT NULL");

            modelBuilder.Entity<Player>()
                .HasIndex(p => p.UserId)
                .IsUnique();

            modelBuilder.Entity<Player>()
                .HasIndex(p => p.LastSeenUserName);

            modelBuilder.Entity<ConnectionLog>()
                .HasIndex(p => p.UserId);

            modelBuilder.Entity<AdminNote>()
                .HasOne(note => note.Player)
                .WithMany(player => player.AdminNotesReceived)
                .HasForeignKey(note => note.PlayerUserId)
                .HasPrincipalKey(player => player.UserId);

            modelBuilder.Entity<AdminNote>()
                .HasOne(version => version.CreatedBy)
                .WithMany(author => author.AdminNotesCreated)
                .HasForeignKey(note => note.CreatedById)
                .HasPrincipalKey(author => author.UserId);

            modelBuilder.Entity<AdminNote>()
                .HasOne(version => version.LastEditedBy)
                .WithMany(author => author.AdminNotesLastEdited)
                .HasForeignKey(note => note.LastEditedById)
                .HasPrincipalKey(author => author.UserId);

            modelBuilder.Entity<AdminNote>()
                .HasOne(version => version.DeletedBy)
                .WithMany(author => author.AdminNotesDeleted)
                .HasForeignKey(note => note.DeletedById)
                .HasPrincipalKey(author => author.UserId);
        }

        public virtual IQueryable<AdminLog> SearchLogs(IQueryable<AdminLog> query, string searchText)
        {
            return query.Where(log => EF.Functions.Like(log.Message, "%" + searchText + "%"));
        }

        public abstract int CountAdminLogs();
    }

    public class Preference
    {
        // NOTE: on postgres there SHOULD be an FK ensuring that the selected character slot always exists.
        // I had to use a migration to implement it and as a result its creation is a finicky mess.
        // Because if I let EFCore know about it it would explode on a circular reference.
        // Also it has to be DEFERRABLE INITIALLY DEFERRED so that insertion of new preferences works.
        // Also I couldn't figure out how to create it on SQLite.
        public int Id { get; set; }
        public Guid UserId { get; set; }
        public int SelectedCharacterSlot { get; set; }
        public string AdminOOCColor { get; set; } = null!;
        public List<Profile> Profiles { get; } = new();
    }

    public class Profile
    {
        public int Id { get; set; }
        public int Slot { get; set; }
        [Column("char_name")] public string CharacterName { get; set; } = null!;
        public string FlavorText { get; set; } = null!;
        public int Age { get; set; }
        public string Sex { get; set; } = null!;
        public string Gender { get; set; } = null!;
        public string Species { get; set; } = null!;
        [Column(TypeName = "jsonb")] public JsonDocument? Markings { get; set; } = null!;
        public string HairName { get; set; } = null!;
        public string HairColor { get; set; } = null!;
        public string FacialHairName { get; set; } = null!;
        public string FacialHairColor { get; set; } = null!;
        public string EyeColor { get; set; } = null!;
        public string SkinColor { get; set; } = null!;
        public string Clothing { get; set; } = null!;
        public string Backpack { get; set; } = null!;
        public List<Job> Jobs { get; } = new();
        public List<Antag> Antags { get; } = new();

        [Column("pref_unavailable")] public DbPreferenceUnavailableMode PreferenceUnavailable { get; set; }

        public int PreferenceId { get; set; }
        public Preference Preference { get; set; } = null!;
    }

    public class Job
    {
        public int Id { get; set; }
        public Profile Profile { get; set; } = null!;
        public int ProfileId { get; set; }

        public string JobName { get; set; } = null!;
        public DbJobPriority Priority { get; set; }
    }

    public enum DbJobPriority
    {
        // These enum values HAVE to match the ones in JobPriority in Content.Shared
        Never = 0,
        Low = 1,
        Medium = 2,
        High = 3
    }

    public class Antag
    {
        public int Id { get; set; }
        public Profile Profile { get; set; } = null!;
        public int ProfileId { get; set; }

        public string AntagName { get; set; } = null!;
    }

    public enum DbPreferenceUnavailableMode
    {
        // These enum values HAVE to match the ones in PreferenceUnavailableMode in Shared.
        StayInLobby = 0,
        SpawnAsOverflow,
    }

    public class AssignedUserId
    {
        public int Id { get; set; }
        public string UserName { get; set; } = null!;

        public Guid UserId { get; set; }
    }

    [Table("player")]
    public class Player
    {
        public int Id { get; set; }

        // Permanent data
        public Guid UserId { get; set; }
        public DateTime FirstSeenTime { get; set; }

        // Data that gets updated on each join.
        public string LastSeenUserName { get; set; } = null!;
        public DateTime LastSeenTime { get; set; }
        public IPAddress LastSeenAddress { get; set; } = null!;
        public byte[]? LastSeenHWId { get; set; }

        // Data that changes with each round
        public List<Round> Rounds { get; set; } = null!;
        public List<AdminLogPlayer> AdminLogs { get; set; } = null!;

        public DateTime? LastReadRules { get; set; }

        public List<AdminNote> AdminNotesReceived { get; set; } = null!;
        public List<AdminNote> AdminNotesCreated { get; set; } = null!;
        public List<AdminNote> AdminNotesLastEdited { get; set; } = null!;
        public List<AdminNote> AdminNotesDeleted { get; set; } = null!;

        public Dictionary<string, TimeSpan> TimeSpentOnRoles { get; set; } = null!;
    }

    [Table("whitelist")]
    public class Whitelist
    {
        [Required, Key] public Guid UserId { get; set; }
    }

    public class Admin
    {
        [Key] public Guid UserId { get; set; }
        public string? Title { get; set; }

        public int? AdminRankId { get; set; }
        public AdminRank? AdminRank { get; set; }
        public List<AdminFlag> Flags { get; set; } = default!;
    }

    public class AdminFlag
    {
        public int Id { get; set; }
        public string Flag { get; set; } = default!;
        public bool Negative { get; set; }

        public Guid AdminId { get; set; }
        public Admin Admin { get; set; } = default!;
    }

    public class AdminRank
    {
        public int Id { get; set; }
        public string Name { get; set; } = default!;

        public List<Admin> Admins { get; set; } = default!;
        public List<AdminRankFlag> Flags { get; set; } = default!;
    }

    public class AdminRankFlag
    {
        public int Id { get; set; }
        public string Flag { get; set; } = default!;

        public int AdminRankId { get; set; }
        public AdminRank Rank { get; set; } = default!;
    }

    public class Round
    {
        [Key, DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        public List<Player> Players { get; set; } = default!;

        public List<AdminLog> AdminLogs { get; set; } = default!;

        [ForeignKey("Server")] public int ServerId { get; set; }
        public Server Server { get; set; } = default!;
    }

    public class Server
    {
        [Key, DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        public string Name { get; set; } = default!;

        [InverseProperty(nameof(Round.Server))]
        public List<Round> Rounds { get; set; } = default!;
    }

    [Index(nameof(Type))]
    public class AdminLog
    {
        [Key, DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        [Key, ForeignKey("Round")] public int RoundId { get; set; }
        public Round Round { get; set; } = default!;

        [Required] public LogType Type { get; set; }

        [Required] public LogImpact Impact { get; set; }

        [Required] public DateTime Date { get; set; }

        [Required] public string Message { get; set; } = default!;

        [Required, Column(TypeName = "jsonb")] public JsonDocument Json { get; set; } = default!;

        public List<AdminLogPlayer> Players { get; set; } = default!;

        public List<AdminLogEntity> Entities { get; set; } = default!;
    }

    public class AdminLogPlayer
    {
        [Required, Key, ForeignKey("Player")] public Guid PlayerUserId { get; set; }
        public Player Player { get; set; } = default!;

        [Required, Key] public int LogId { get; set; }
        [Required, Key] public int RoundId { get; set; }
        [ForeignKey("LogId,RoundId")] public AdminLog Log { get; set; } = default!;
    }

    public class AdminLogEntity
    {
        [Required, Key] public int Uid { get; set; }
        public string? Name { get; set; } = default!;
    }

    [Table("server_ban")]
    public class ServerBan
    {
        public int Id { get; set; }
        public Guid? UserId { get; set; }
        [Column(TypeName = "inet")] public (IPAddress, int)? Address { get; set; }
        public byte[]? HWId { get; set; }

        public DateTime BanTime { get; set; }

        public DateTime? ExpirationTime { get; set; }

        public string Reason { get; set; } = null!;
        public Guid? BanningAdmin { get; set; }

        public ServerUnban? Unban { get; set; }

        public List<ServerBanHit> BanHits { get; set; } = null!;
    }

    [Table("server_unban")]
    public class ServerUnban
    {
        [Column("unban_id")] public int Id { get; set; }

        public int BanId { get; set; }
        public ServerBan Ban { get; set; } = null!;

        public Guid? UnbanningAdmin { get; set; }

        public DateTime UnbanTime { get; set; }
    }

    [Table("connection_log")]
    public class ConnectionLog
    {
        public int Id { get; set; }

        public Guid UserId { get; set; }
        public string UserName { get; set; } = null!;

        public DateTime Time { get; set; }

        public IPAddress Address { get; set; } = null!;
        public byte[]? HWId { get; set; }

        public ConnectionDenyReason? Denied { get; set; }

        public List<ServerBanHit> BanHits { get; set; } = null!;
    }

    public enum ConnectionDenyReason : byte
    {
        Ban = 0,
        Whitelist = 1,
        Full = 2,
    }

    public class ServerBanHit
    {
        public int Id { get; set; }

        public int BanId { get; set; }
        public int ConnectionId { get; set; }

        public ServerBan Ban { get; set; } = null!;
        public ConnectionLog Connection { get; set; } = null!;
    }

    [Table("server_role_ban")]
    public sealed class ServerRoleBan
    {
        public int Id { get; set; }
        public Guid? UserId { get; set; }
        [Column(TypeName = "inet")] public (IPAddress, int)? Address { get; set; }
        public byte[]? HWId { get; set; }

        public DateTime BanTime { get; set; }

        public DateTime? ExpirationTime { get; set; }

        public string Reason { get; set; } = null!;
        public Guid? BanningAdmin { get; set; }

        public ServerRoleUnban? Unban { get; set; }

        public string RoleId { get; set; } = null!;
    }

    [Table("server_role_unban")]
    public sealed class ServerRoleUnban
    {
        [Column("role_unban_id")] public int Id { get; set; }

        public int BanId { get; set; }
        public ServerRoleBan Ban { get; set; } = null!;

        public Guid? UnbanningAdmin { get; set; }

        public DateTime UnbanTime { get; set; }
    }

    [Table("role_timer")]
    public sealed class RoleTimer
    {
        [Column("player")]
        [Required, ForeignKey("Player")]
        public Guid Player { get; set; }
        [Required, ForeignKey("Role")]
        public string Role { get; set; } = string.Empty;
        [Required, ForeignKey("TimeSpent")]
        public TimeSpan TimeSpent { get; set; }
    }

    [Table("uploaded_resource_log")]
    public sealed class UploadedResourceLog
    {
        [Key, DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        public DateTime Date { get; set; }

        public Guid UserId { get; set; }

        public string Path { get; set; } = string.Empty;

        public byte[] Data { get; set; } = default!;
    }

    [Index(nameof(PlayerUserId))]
    public class AdminNote
    {
        [Required, Key, DatabaseGenerated(DatabaseGeneratedOption.Identity)] public int Id { get; set; }

        [ForeignKey("Round")] public int? RoundId { get; set; }
        public Round? Round { get; set; }

        [Required, ForeignKey("Player")] public Guid PlayerUserId { get; set; }
        public Player Player { get; set; } = default!;

        [Required, MaxLength(4096)] public string Message { get; set; } = string.Empty;

        [Required, ForeignKey("CreatedBy")] public Guid CreatedById { get; set; }
        [Required] public Player CreatedBy { get; set; } = default!;

        [Required] public DateTime CreatedAt { get; set; }

        [Required, ForeignKey("LastEditedBy")] public Guid LastEditedById { get; set; }
        [Required] public Player LastEditedBy { get; set; } = default!;

        [Required] public DateTime LastEditedAt { get; set; }

        public bool Deleted { get; set; }
        [ForeignKey("DeletedBy")] public Guid? DeletedById { get; set; }
        public Player? DeletedBy { get; set; }
        public DateTime? DeletedAt { get; set; }

        public bool ShownToPlayer { get; set; }
    }
}
