using SpatialCheckPro.Models;
using SpatialCheckPro.Models.Enums;
using System.ComponentModel.DataAnnotations;

namespace SpatialCheckPro.Data
{
    /// <summary>
    /// 검수 오류 엔티티 클래스
    /// </summary>
    public class ValidationErrorEntity
    {
        /// <summary>
        /// 오류 ID (Primary Key)
        /// </summary>
        [Key]
        public string ErrorId { get; set; } = string.Empty;

        /// <summary>
        /// 검수 항목 결과 ID (Foreign Key)
        /// </summary>
        public int CheckResultId { get; set; }

        /// <summary>
        /// 테이블명
        /// </summary>
        public string TableName { get; set; } = string.Empty;

        /// <summary>
        /// 피처 ID
        /// </summary>
        public string? FeatureId { get; set; }

        /// <summary>
        /// 오류 메시지
        /// </summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>
        /// 심각도
        /// </summary>
        public ErrorSeverity Severity { get; set; }

        /// <summary>
        /// 오류 유형
        /// </summary>
        public ErrorType ErrorType { get; set; }

        /// <summary>
        /// 오류 발생 시간
        /// </summary>
        public DateTime OccurredAt { get; set; }

        /// <summary>
        /// 오류 해결 여부
        /// </summary>
        public bool IsResolved { get; set; }

        /// <summary>
        /// 오류 해결 시간
        /// </summary>
        public DateTime? ResolvedAt { get; set; }

        /// <summary>
        /// 오류 해결 방법
        /// </summary>
        public string? ResolutionMethod { get; set; }

        /// <summary>
        /// 지리적 위치 X 좌표
        /// </summary>
        public double? LocationX { get; set; }

        /// <summary>
        /// 지리적 위치 Y 좌표
        /// </summary>
        public double? LocationY { get; set; }

        /// <summary>
        /// 지리적 위치 Z 좌표
        /// </summary>
        public double? LocationZ { get; set; }

        /// <summary>
        /// 좌표계 정보
        /// </summary>
        public string? LocationCoordinateSystem { get; set; }

        /// <summary>
        /// 메타데이터 JSON 문자열
        /// </summary>
        public Dictionary<string, object> MetadataJson { get; set; } = new Dictionary<string, object>();

        /// <summary>
        /// 검수 항목 결과 (Navigation Property)
        /// </summary>
        public CheckResultEntity CheckResult { get; set; } = null!;

        /// <summary>
        /// 도메인 모델로 변환
        /// </summary>
        /// <returns>ValidationError 도메인 모델</returns>
        public ValidationError ToDomainModel()
        {
            var domainModel = new ValidationError
            {
                ErrorId = ErrorId,
                TableName = TableName,
                FeatureId = FeatureId,
                Message = Message,
                Severity = Severity,
                ErrorType = ErrorType,
                OccurredAt = OccurredAt,
                IsResolved = IsResolved,
                ResolvedAt = ResolvedAt,
                ResolutionMethod = ResolutionMethod,
                Metadata = MetadataJson
            };

            // 지리적 위치 정보 설정
            if (LocationX.HasValue && LocationY.HasValue)
            {
                domainModel.Location = new GeographicLocation
                {
                    X = LocationX.Value,
                    Y = LocationY.Value,
                    Z = LocationZ ?? 0.0,
                    CoordinateSystem = LocationCoordinateSystem ?? string.Empty
                };
            }

            return domainModel;
        }

        /// <summary>
        /// 도메인 모델에서 엔티티로 변환
        /// </summary>
        /// <param name="domainModel">ValidationError 도메인 모델</param>
        /// <returns>ValidationErrorEntity</returns>
        public static ValidationErrorEntity FromDomainModel(ValidationError domainModel)
        {
            var entity = new ValidationErrorEntity
            {
                ErrorId = domainModel.ErrorId,
                TableName = domainModel.TableName,
                FeatureId = domainModel.FeatureId,
                Message = domainModel.Message,
                Severity = domainModel.Severity,
                ErrorType = domainModel.ErrorType,
                OccurredAt = domainModel.OccurredAt,
                IsResolved = domainModel.IsResolved,
                ResolvedAt = domainModel.ResolvedAt,
                ResolutionMethod = domainModel.ResolutionMethod,
                MetadataJson = domainModel.Metadata
            };

            // 지리적 위치 정보 설정
            if (domainModel.Location != null)
            {
                entity.LocationX = domainModel.Location.X;
                entity.LocationY = domainModel.Location.Y;
                entity.LocationZ = domainModel.Location.Z;
                entity.LocationCoordinateSystem = domainModel.Location.CoordinateSystem;
            }

            return entity;
        }
    }
}
