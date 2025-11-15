using SpatialCheckPro.Models.Enums;
using CsvHelper.Configuration.Attributes;

namespace SpatialCheckPro.Models.Config
{
    /// <summary>
    /// 관계 검수 설정을 나타내는 클래스
    /// </summary>
    public class RelationCheckConfig
    {
        /// <summary>
        /// 규칙 ID
        /// </summary>
        [Name("RuleId")]
        public string RuleId { get; set; } = string.Empty;

        /// <summary>
        /// 사용 여부 (Y/N)
        /// </summary>
        [Name("Enabled")]
        public string Enabled { get; set; } = "Y";

        /// <summary>
        /// 케이스 유형 (PointInsidePolygon | LineWithinPolygon | PolygonNotWithinPolygon)
        /// </summary>
        [Name("CaseType")]
        public string CaseType { get; set; } = string.Empty;

        // 기존 CSV 기반 속성들 (하위 호환성 유지)
        /// <summary>
        /// 메인 테이블 ID
        /// </summary>
        [Name("MainTableId")]
        public string MainTableId { get; set; } = string.Empty;

        /// <summary>
        /// 메인 테이블명
        /// </summary>
        [Name("MainTableName")]
        public string MainTableName { get; set; } = string.Empty;

        /// <summary>
        /// 관련 테이블 ID
        /// </summary>
        [Name("RelatedTableId")]
        public string RelatedTableId { get; set; } = string.Empty;

        /// <summary>
        /// 관련 테이블명
        /// </summary>
        [Name("RelatedTableName")]
        public string RelatedTableName { get; set; } = string.Empty;

        /// <summary>
        /// 선택적 필드 필터 (예: PG_RDFC_SE IN (PRC002,PRC003))
        /// </summary>
        [Name("FieldFilter")]
        public string? FieldFilter { get; set; }

        /// <summary>
        /// 허용 오차 (CaseType에 따라 의미 다름)
        /// </summary>
        [Name("Tolerance")]
        public double? Tolerance { get; set; }

        /// <summary>
        /// 비고/설명
        /// </summary>
        [Name("Note")]
        public string? Note { get; set; }

        /// <summary>
        /// 선-폴리곤 검사 여부 (Y/N)
        /// </summary>
        public string CheckLineInPolygon { get; set; } = "N";

        /// <summary>
        /// 점-폴리곤 검사 여부 (Y/N)
        /// </summary>
        public string CheckPointInPolygon { get; set; } = "N";

        /// <summary>
        /// 폴리곤-폴리곤 검사 여부 (Y/N)
        /// </summary>
        public string CheckPolygonInPolygon { get; set; } = "N";

        // 새로운 고급 기능들
        /// <summary>
        /// 공간 관계 규칙 목록
        /// </summary>
        public List<SpatialRelationRule> SpatialRelationRules { get; set; } = new List<SpatialRelationRule>();

        /// <summary>
        /// 위상 규칙 목록
        /// </summary>
        public List<TopologyRule> TopologyRules { get; set; } = new List<TopologyRule>();

        /// <summary>
        /// 논리적 관계 규칙 목록
        /// </summary>
        public List<LogicalRelationRule> LogicalRelationRules { get; set; } = new List<LogicalRelationRule>();

        /// <summary>
        /// 조건부 규칙 목록
        /// </summary>
        public List<ConditionalRule> ConditionalRules { get; set; } = new List<ConditionalRule>();

        /// <summary>
        /// 교차 테이블 관계 규칙 목록
        /// </summary>
        public List<CrossTableRelationRule> CrossTableRelationRules { get; set; } = new List<CrossTableRelationRule>();

        /// <summary>
        /// 병렬 처리 여부
        /// </summary>
        public bool EnableParallelProcessing { get; set; } = true;

        /// <summary>
        /// 최대 병렬도
        /// </summary>
        public int MaxDegreeOfParallelism { get; set; } = Environment.ProcessorCount;

        /// <summary>
        /// 배치 크기
        /// </summary>
        public int BatchSize { get; set; } = 1000;

        /// <summary>
        /// 메모리 최적화 모드 사용 여부
        /// </summary>
        public bool UseMemoryOptimization { get; set; } = true;

        /// <summary>
        /// 공간 인덱스 타입
        /// </summary>
        public SpatialIndexType SpatialIndexType { get; set; } = SpatialIndexType.RTree;

        // 유연 파서에서 기본 인덱스 타입을 참조하도록 제공
        public static SpatialIndexType? DefaultIndexType { get; } = SpatialIndexType.RTree;

        /// <summary>
        /// QC_ERRORS 시스템에 결과 저장 여부
        /// </summary>
        public bool SaveToQcErrors { get; set; } = true;

        /// <summary>
        /// QC_ERRORS FileGDB 경로 (null이면 자동 생성)
        /// </summary>
        public string? QcErrorsGdbPath { get; set; }

        /// <summary>
        /// QC_ERRORS와의 호환성 확인 여부
        /// </summary>
        public bool ValidateQcErrorsCompatibility { get; set; } = true;

        /// <summary>
        /// 설정 유효성을 검증합니다
        /// </summary>
        /// <returns>검증 오류 목록</returns>
        public List<ValidationError> Validate()
        {
            var errors = new List<ValidationError>();

            if (!string.Equals(Enabled?.Trim(), "Y", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(Enabled?.Trim(), "N", StringComparison.OrdinalIgnoreCase))
            {
                errors.Add(new ValidationError { Message = "Enabled는 Y 또는 N 이어야 합니다." });
            }

            if (string.IsNullOrWhiteSpace(CaseType))
            {
                errors.Add(new ValidationError { Message = "CaseType은 필수입니다." });
            }

            if (string.IsNullOrWhiteSpace(MainTableId))
            {
                errors.Add(new ValidationError { Message = "메인 테이블 ID는 필수입니다." });
            }

            if (string.IsNullOrWhiteSpace(MainTableName))
            {
                errors.Add(new ValidationError { Message = "메인 테이블명은 필수입니다." });
            }

            if (string.IsNullOrWhiteSpace(RelatedTableId))
            {
                errors.Add(new ValidationError { Message = "관련 테이블 ID는 필수입니다." });
            }

            if (string.IsNullOrWhiteSpace(RelatedTableName))
            {
                errors.Add(new ValidationError { Message = "관련 테이블명은 필수입니다." });
            }

            // 기존 Y/N 값 검증 (하위 호환)
            var validYN = new[] { "Y", "N" };
            if (!validYN.Contains(CheckLineInPolygon?.ToUpper()))
            {
                errors.Add(new ValidationError { Message = "선-폴리곤 검사는 Y 또는 N이어야 합니다." });
            }

            if (!validYN.Contains(CheckPointInPolygon?.ToUpper()))
            {
                errors.Add(new ValidationError { Message = "점-폴리곤 검사는 Y 또는 N이어야 합니다." });
            }

            if (!validYN.Contains(CheckPolygonInPolygon?.ToUpper()))
            {
                errors.Add(new ValidationError { Message = "폴리곤-폴리곤 검사는 Y 또는 N이어야 합니다." });
            }

            // 케이스별 유효성
            if (string.Equals(CaseType, "LineWithinPolygon", StringComparison.OrdinalIgnoreCase))
            {
                if (Tolerance == null || Tolerance < 0)
                {
                    errors.Add(new ValidationError { Message = "LineWithinPolygon은 Tolerance(>=0)가 필요합니다." });
                }
            }

            return errors;
        }
    }
}
