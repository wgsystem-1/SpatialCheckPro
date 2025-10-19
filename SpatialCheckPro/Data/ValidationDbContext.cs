using Microsoft.EntityFrameworkCore;
using SpatialCheckPro.Data.Entities;
using SpatialCheckPro.Models;
using System.Text.Json;

namespace SpatialCheckPro.Data
{
    /// <summary>
    /// 검수 결과 저장을 위한 SQLite 데이터베이스 컨텍스트
    /// </summary>
    public class ValidationDbContext : DbContext
    {
        /// <summary>
        /// 검수 결과 테이블
        /// </summary>
        public DbSet<ValidationResultEntity> ValidationResults { get; set; }

        /// <summary>
        /// 단계별 검수 결과 테이블
        /// </summary>
        public DbSet<StageResultEntity> StageResults { get; set; }

        /// <summary>
        /// 검수 항목 결과 테이블
        /// </summary>
        public DbSet<CheckResultEntity> CheckResults { get; set; }

        /// <summary>
        /// 검수 오류 테이블
        /// </summary>
        public DbSet<ValidationErrorEntity> ValidationErrors { get; set; }

        /// <summary>
        /// 공간정보 파일 정보 테이블
        /// </summary>
        public DbSet<SpatialFileInfoEntity> SpatialFiles { get; set; }

        /// <summary>
        /// 검수 설정 테이블
        /// </summary>
        public DbSet<ValidationConfigurationEntity> ValidationConfigurations { get; set; }

        /// <summary>
        /// 단계 소요 시간 이력 테이블
        /// </summary>
        public DbSet<StageDurationHistoryEntity> StageDurationHistory { get; set; }

        /// <summary>
        /// 단계 소요 시간 이력 테이블
        /// </summary>

        /// <summary>
        /// 기본 생성자
        /// </summary>
        public ValidationDbContext()
        {
        }

        /// <summary>
        /// 옵션을 받는 생성자
        /// </summary>
        /// <param name="options">DbContext 옵션</param>
        public ValidationDbContext(DbContextOptions<ValidationDbContext> options) : base(options)
        {
        }

        /// <summary>
        /// 데이터베이스 연결 설정
        /// </summary>
        /// <param name="optionsBuilder">옵션 빌더</param>
        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            if (!optionsBuilder.IsConfigured)
            {
                // 기본 SQLite 데이터베이스 경로 설정
                var dbPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), 
                    "GeoSpatialValidationSystem", "validation.db");
                
                // 디렉토리가 없으면 생성
                var directory = System.IO.Path.GetDirectoryName(dbPath);
                if (!System.IO.Directory.Exists(directory))
                {
                    System.IO.Directory.CreateDirectory(directory!);
                }

                optionsBuilder.UseSqlite($"Data Source={dbPath}");
            }
        }

        /// <summary>
        /// 모델 구성
        /// </summary>
        /// <param name="modelBuilder">모델 빌더</param>
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // ValidationResultEntity 설정
            modelBuilder.Entity<ValidationResultEntity>(entity =>
            {
                entity.HasKey(e => e.ValidationId);
                entity.Property(e => e.ValidationId).IsRequired().HasMaxLength(50);
                entity.Property(e => e.Status).HasConversion<string>();
                entity.Property(e => e.StartedAt).IsRequired();
                entity.Property(e => e.CompletedAt);
                entity.Property(e => e.TotalErrors).HasDefaultValue(0);
                entity.Property(e => e.TotalWarnings).HasDefaultValue(0);

                // 관계 설정
                entity.HasOne(e => e.TargetFile)
                      .WithMany()
                      .HasForeignKey(e => e.TargetFileId)
                      .OnDelete(DeleteBehavior.Cascade);

                entity.HasMany(e => e.StageResults)
                      .WithOne(s => s.ValidationResult)
                      .HasForeignKey(s => s.ValidationId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            // StageResultEntity 설정
            modelBuilder.Entity<StageResultEntity>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Id).ValueGeneratedOnAdd();
                entity.Property(e => e.ValidationId).IsRequired().HasMaxLength(50);
                entity.Property(e => e.StageNumber).IsRequired();
                entity.Property(e => e.StageName).IsRequired().HasMaxLength(100);
                entity.Property(e => e.Status).HasConversion<string>();
                entity.Property(e => e.StartedAt).IsRequired();
                entity.Property(e => e.CompletedAt);
                entity.Property(e => e.ErrorMessage).HasMaxLength(1000);

                // 관계 설정
                entity.HasMany(e => e.CheckResults)
                      .WithOne(c => c.StageResult)
                      .HasForeignKey(c => c.StageResultId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            // CheckResultEntity 설정
            modelBuilder.Entity<CheckResultEntity>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Id).ValueGeneratedOnAdd();
                entity.Property(e => e.StageResultId).IsRequired();
                entity.Property(e => e.CheckId).IsRequired().HasMaxLength(50);
                entity.Property(e => e.CheckName).IsRequired().HasMaxLength(200);
                entity.Property(e => e.Status).HasConversion<string>();
                entity.Property(e => e.TotalCount).HasDefaultValue(0);

                // 관계 설정
                entity.HasMany(e => e.Errors)
                      .WithOne(err => err.CheckResult)
                      .HasForeignKey(err => err.CheckResultId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            // ValidationErrorEntity 설정
            modelBuilder.Entity<ValidationErrorEntity>(entity =>
            {
                entity.HasKey(e => e.ErrorId);
                entity.Property(e => e.ErrorId).IsRequired().HasMaxLength(50);
                entity.Property(e => e.CheckResultId).IsRequired();
                entity.Property(e => e.TableName).IsRequired().HasMaxLength(100);
                entity.Property(e => e.FeatureId).HasMaxLength(50);
                entity.Property(e => e.Message).IsRequired().HasMaxLength(1000);
                entity.Property(e => e.Severity).HasConversion<string>();
                entity.Property(e => e.ErrorType).HasConversion<string>();
                entity.Property(e => e.OccurredAt).IsRequired();
                entity.Property(e => e.IsResolved).HasDefaultValue(false);
                entity.Property(e => e.ResolvedAt);
                entity.Property(e => e.ResolutionMethod).HasMaxLength(500);

                // JSON 직렬화를 위한 변환 설정
                entity.Property(e => e.MetadataJson)
                      .HasColumnName("Metadata")
                      .HasConversion(
                          v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                          v => JsonSerializer.Deserialize<Dictionary<string, object>>(v, (JsonSerializerOptions?)null) ?? new Dictionary<string, object>()
                      );

                // 지리적 위치 정보 설정
                entity.Property(e => e.LocationX).HasColumnName("LocationX");
                entity.Property(e => e.LocationY).HasColumnName("LocationY");
                entity.Property(e => e.LocationZ).HasColumnName("LocationZ");
                entity.Property(e => e.LocationCoordinateSystem).HasMaxLength(100);
            });

            // SpatialFileInfoEntity 설정
            modelBuilder.Entity<SpatialFileInfoEntity>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Id).ValueGeneratedOnAdd();
                entity.Property(e => e.FilePath).IsRequired().HasMaxLength(500);
                entity.Property(e => e.FileName).IsRequired().HasMaxLength(255);
                entity.Property(e => e.Format).HasConversion<string>();
                entity.Property(e => e.FileSize).IsRequired();
                entity.Property(e => e.CoordinateSystem).HasMaxLength(200);
                entity.Property(e => e.CreatedAt).IsRequired();
                entity.Property(e => e.ModifiedAt).IsRequired();

                // JSON 직렬화를 위한 변환 설정
                entity.Property(e => e.TablesJson)
                      .HasColumnName("Tables")
                      .HasConversion(
                          v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                          v => JsonSerializer.Deserialize<List<TableInfo>>(v, (JsonSerializerOptions?)null) ?? new List<TableInfo>()
                      );
            });

            // 인덱스 설정
            modelBuilder.Entity<ValidationResultEntity>()
                .HasIndex(e => e.StartedAt)
                .HasDatabaseName("IX_ValidationResults_StartedAt");

            modelBuilder.Entity<ValidationErrorEntity>()
                .HasIndex(e => new { e.TableName, e.Severity })
                .HasDatabaseName("IX_ValidationErrors_TableName_Severity");

            modelBuilder.Entity<SpatialFileInfoEntity>()
                .HasIndex(e => e.FilePath)
                .IsUnique()
                .HasDatabaseName("IX_SpatialFiles_FilePath");

            modelBuilder.Entity<StageDurationHistoryEntity>()
                .HasIndex(e => new { e.StageId, e.CollectedAtUtc })
                .HasDatabaseName("IX_StageDurationHistory_StageId_CollectedAt");
        }
    }
}
