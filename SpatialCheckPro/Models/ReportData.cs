using System;
using System.Collections.Generic;

namespace SpatialCheckPro.Models
{
    /// <summary>
    /// 보고서 생성에 필요한 데이터 모델
    /// </summary>
    public class ReportData
    {
        /// <summary>
        /// 보고서 생성 시간
        /// </summary>
        public DateTime GeneratedAt { get; set; } = DateTime.Now;

        /// <summary>
        /// 검수 대상 파일 정보
        /// </summary>
        public string? FilePath { get; set; }

        /// <summary>
        /// 파일명 (확장자 제외)
        /// </summary>
        public string? FileName { get; set; }

        /// <summary>
        /// 파일 크기 (바이트)
        /// </summary>
        public long FileSize { get; set; }

        /// <summary>
        /// 검수 시작 시간
        /// </summary>
        public DateTime? ValidationStartTime { get; set; }

        /// <summary>
        /// 검수 완료 시간
        /// </summary>
        public DateTime? ValidationEndTime { get; set; }

        /// <summary>
        /// 검수 소요 시간 (초)
        /// </summary>
        public double ValidationDurationSeconds => ValidationEndTime.HasValue && ValidationStartTime.HasValue
            ? (ValidationEndTime.Value - ValidationStartTime.Value).TotalSeconds
            : 0;

        /// <summary>
        /// 검수 요약 정보
        /// </summary>
        public ReportValidationSummary Summary { get; set; } = new();

        /// <summary>
        /// 테이블별 검수 결과
        /// </summary>
        public List<TableValidationSummary> TableResults { get; set; } = new();

        /// <summary>
        /// 지오메트리 오류 목록
        /// </summary>
        public List<GeometryErrorSummary> GeometryErrors { get; set; } = new();

        /// <summary>
        /// 속성 관계 오류 목록
        /// </summary>
        public List<AttributeErrorSummary> AttributeErrors { get; set; } = new();

        /// <summary>
        /// 스키마 오류 목록
        /// </summary>
        public List<SchemaErrorSummary> SchemaErrors { get; set; } = new();

        /// <summary>
        /// 검수 설정 정보
        /// </summary>
        public ReportValidationSettings Settings { get; set; } = new();
    }

    /// <summary>
    /// 보고서용 검수 요약 정보
    /// </summary>
    public class ReportValidationSummary
    {
        /// <summary>
        /// 전체 테이블 수
        /// </summary>
        public int TotalTables { get; set; }

        /// <summary>
        /// 검수 완료된 테이블 수
        /// </summary>
        public int ValidatedTables { get; set; }

        /// <summary>
        /// 전체 오류 수
        /// </summary>
        public int TotalErrors { get; set; }

        /// <summary>
        /// 지오메트리 오류 수
        /// </summary>
        public int GeometryErrors { get; set; }

        /// <summary>
        /// 속성 관계 오류 수
        /// </summary>
        public int AttributeErrors { get; set; }

        /// <summary>
        /// 스키마 오류 수
        /// </summary>
        public int SchemaErrors { get; set; }

        /// <summary>
        /// 경고 수
        /// </summary>
        public int Warnings { get; set; }

        /// <summary>
        /// 검수 성공률 (%)
        /// </summary>
        public double SuccessRate => TotalTables > 0 ? (double)(TotalTables - TotalErrors) / TotalTables * 100 : 0;
    }

    /// <summary>
    /// 테이블별 검수 결과 요약
    /// </summary>
    public class TableValidationSummary
    {
        /// <summary>
        /// 테이블 ID
        /// </summary>
        public string? TableId { get; set; }

        /// <summary>
        /// 테이블명
        /// </summary>
        public string? TableName { get; set; }

        /// <summary>
        /// 지오메트리 타입
        /// </summary>
        public string? GeometryType { get; set; }

        /// <summary>
        /// 피처 수
        /// </summary>
        public int FeatureCount { get; set; }

        /// <summary>
        /// 오류 수
        /// </summary>
        public int ErrorCount { get; set; }

        /// <summary>
        /// 경고 수
        /// </summary>
        public int WarningCount { get; set; }

        /// <summary>
        /// 검수 상태
        /// </summary>
        public string? ValidationStatus { get; set; }
    }

    /// <summary>
    /// 지오메트리 오류 요약
    /// </summary>
    public class GeometryErrorSummary
    {
        /// <summary>
        /// 테이블 ID
        /// </summary>
        public string? TableId { get; set; }

        /// <summary>
        /// 피처 ID
        /// </summary>
        public string? FeatureId { get; set; }

        /// <summary>
        /// 오류 타입
        /// </summary>
        public string? ErrorType { get; set; }

        /// <summary>
        /// 오류 설명
        /// </summary>
        public string? ErrorDescription { get; set; }

        /// <summary>
        /// 심각도
        /// </summary>
        public string? Severity { get; set; }

        /// <summary>
        /// X 좌표
        /// </summary>
        public double? X { get; set; }

        /// <summary>
        /// Y 좌표
        /// </summary>
        public double? Y { get; set; }
    }

    /// <summary>
    /// 속성 오류 요약
    /// </summary>
    public class AttributeErrorSummary
    {
        /// <summary>
        /// 테이블 ID
        /// </summary>
        public string? TableId { get; set; }

        /// <summary>
        /// 피처 ID
        /// </summary>
        public string? FeatureId { get; set; }

        /// <summary>
        /// 필드명
        /// </summary>
        public string? FieldName { get; set; }

        /// <summary>
        /// 현재값
        /// </summary>
        public string? CurrentValue { get; set; }

        /// <summary>
        /// 기대값
        /// </summary>
        public string? ExpectedValue { get; set; }

        /// <summary>
        /// 오류 설명
        /// </summary>
        public string? ErrorDescription { get; set; }

        /// <summary>
        /// 심각도
        /// </summary>
        public string? Severity { get; set; }
    }

    /// <summary>
    /// 스키마 오류 요약
    /// </summary>
    public class SchemaErrorSummary
    {
        /// <summary>
        /// 테이블 ID
        /// </summary>
        public string? TableId { get; set; }

        /// <summary>
        /// 필드명
        /// </summary>
        public string? FieldName { get; set; }

        /// <summary>
        /// 오류 타입
        /// </summary>
        public string? ErrorType { get; set; }

        /// <summary>
        /// 오류 설명
        /// </summary>
        public string? ErrorDescription { get; set; }

        /// <summary>
        /// 심각도
        /// </summary>
        public string? Severity { get; set; }
    }

    /// <summary>
    /// 보고서용 검수 설정 정보
    /// </summary>
    public class ReportValidationSettings
    {
        /// <summary>
        /// 테이블 검수 활성화 여부
        /// </summary>
        public bool TableValidationEnabled { get; set; }

        /// <summary>
        /// 스키마 검수 활성화 여부
        /// </summary>
        public bool SchemaValidationEnabled { get; set; }

        /// <summary>
        /// 지오메트리 검수 활성화 여부
        /// </summary>
        public bool GeometryValidationEnabled { get; set; }

        /// <summary>
        /// 속성 검수 활성화 여부
        /// </summary>
        public bool AttributeValidationEnabled { get; set; }

        /// <summary>
        /// 검수 설정 파일 경로들
        /// </summary>
        public Dictionary<string, string> ConfigFilePaths { get; set; } = new();
    }
}
