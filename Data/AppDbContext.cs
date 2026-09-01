using MdfTracker.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace MdfTracker.Api.Data;

/// <summary>
/// Database is snake_case end to end; the API layer is camelCase. Mapping happens here
/// and in the DTOs, nowhere else.
/// </summary>
public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<TrackingSession> TrackingSessions => Set<TrackingSession>();

    public DbSet<SessionFrame> SessionFrames => Set<SessionFrame>();

    public DbSet<SessionEvent> SessionEvents => Set<SessionEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TrackingSession>(entity =>
        {
            entity.ToTable("tracking_sessions");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.SessionNumber).HasColumnName("session_number").HasMaxLength(40).IsRequired();
            entity.Property(e => e.StartTime).HasColumnName("start_time").IsRequired();
            entity.Property(e => e.EndTime).HasColumnName("end_time");
            entity.Property(e => e.CameraType).HasColumnName("camera_type").HasConversion(EnumConverters.CameraType).HasMaxLength(10).IsRequired();
            entity.Property(e => e.TrackerAlgorithm).HasColumnName("tracker_algorithm").HasConversion(EnumConverters.TrackerAlgorithm).HasMaxLength(10).IsRequired();
            entity.Property(e => e.AverageFps).HasColumnName("average_fps").HasPrecision(8, 2);
            entity.Property(e => e.Status).HasColumnName("status").HasConversion(EnumConverters.SessionStatus).HasMaxLength(10).IsRequired();
            entity.Property(e => e.IsSuccessful).HasColumnName("is_successful").IsRequired();
            entity.Property(e => e.ObjectDescription).HasColumnName("object_description").HasMaxLength(500);
            entity.Property(e => e.AutoClosed).HasColumnName("auto_closed").IsRequired();
            entity.Property(e => e.ImuEnabled).HasColumnName("imu_enabled").IsRequired();
            entity.Property(e => e.Latitude).HasColumnName("latitude").HasPrecision(9, 6);
            entity.Property(e => e.Longitude).HasColumnName("longitude").HasPrecision(9, 6);
            entity.Property(e => e.LocationAccuracyMeters).HasColumnName("location_accuracy_m").HasPrecision(8, 2);
            entity.Property(e => e.DeviceModel).HasColumnName("device_model").HasMaxLength(120);
            entity.Property(e => e.OsVersion).HasColumnName("os_version").HasMaxLength(60);
            entity.Property(e => e.AppVersion).HasColumnName("app_version").HasMaxLength(30);
            entity.Property(e => e.ProcessingScale).HasColumnName("processing_scale").HasPrecision(3, 2);
            entity.Property(e => e.ScreenWidth).HasColumnName("screen_width").IsRequired();
            entity.Property(e => e.ScreenHeight).HasColumnName("screen_height").IsRequired();

            entity.HasIndex(e => e.SessionNumber).IsUnique().HasDatabaseName("ix_tracking_sessions_session_number");
            // History and the live room both read newest-first.
            entity.HasIndex(e => e.StartTime).HasDatabaseName("ix_tracking_sessions_start_time");
            entity.HasIndex(e => e.Status).HasDatabaseName("ix_tracking_sessions_status");
        });

        modelBuilder.Entity<SessionFrame>(entity =>
        {
            entity.ToTable("session_frames");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.SessionId).HasColumnName("session_id").IsRequired();
            entity.Property(e => e.FrameTimestamp).HasColumnName("frame_timestamp").IsRequired();
            entity.Property(e => e.XCoordinate).HasColumnName("x_coordinate").IsRequired();
            entity.Property(e => e.YCoordinate).HasColumnName("y_coordinate").IsRequired();
            entity.Property(e => e.Width).HasColumnName("width").IsRequired();
            entity.Property(e => e.Height).HasColumnName("height").IsRequired();
            entity.Property(e => e.Fps).HasColumnName("fps").HasPrecision(6, 2);

            entity.HasOne(e => e.Session)
                .WithMany(s => s.Frames)
                .HasForeignKey(e => e.SessionId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => new { e.SessionId, e.FrameTimestamp })
                .HasDatabaseName("ix_session_frames_session_id_frame_timestamp");
        });

        modelBuilder.Entity<SessionEvent>(entity =>
        {
            entity.ToTable("session_events");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.SessionId).HasColumnName("session_id").IsRequired();
            entity.Property(e => e.EventType).HasColumnName("event_type").HasConversion(EnumConverters.SessionEventType).HasMaxLength(15).IsRequired();
            entity.Property(e => e.OccurredAt).HasColumnName("occurred_at").IsRequired();

            entity.HasOne(e => e.Session)
                .WithMany(s => s.Events)
                .HasForeignKey(e => e.SessionId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => new { e.SessionId, e.OccurredAt })
                .HasDatabaseName("ix_session_events_session_id_occurred_at");
        });
    }
}
