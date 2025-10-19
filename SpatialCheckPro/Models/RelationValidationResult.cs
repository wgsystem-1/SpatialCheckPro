using System;
using System.Collections.Generic;
using System.Linq;
using SpatialCheckPro.Models.Enums;

namespace SpatialCheckPro.Models
{
    /// <summary>
    /// 관계 검수 결과를 나타내는 모델 클래스
    /// </summary>
    public class RelationValidationResult
    {
        /// <summary>
        /// 검수 ID
        /// </summary>
        public string ValidationId { get; set; } = Guid.NewGuid().ToString();

        /// <summary>
        /// 검수 시작 시간
        /// </summary>
        public DateTime StartedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// 검수 완료 시간
        /// </summary>
        public DateTime CompletedAt { get; set; }

        /// <summary>
        /// 처리 시간
        /// </summary>
        public TimeSpan ProcessingTime => CompletedAt - StartedAt;

        /// <summary>
        /// 검수 유효성 여부
        /// </summary>
        public bool IsValid { get; set; }

        /// <summary>
        /// 검사된 총 규칙 수
        /// </summary>
        public int TotalRulesChecked { get; set; }

        /// <summary>
        /// 공간 오류 개수
        /// </summary>
        public int SpatialErrorCount { get; set; }

        /// <summary>
        /// 속성 오류 개수
        /// </summary>
        public int AttributeErrorCount { get; set; }

        /// <summary>
        /// 총 오류 개수
        /// </summary>
        public int TotalErrorCount => SpatialErrorCount + AttributeErrorCount;

        /// <summary>
        /// 공간 관계 오류 목록
        /// </summary>
        public List<SpatialRelationError> SpatialErrors { get; set; } = new List<SpatialRelationError>();

        /// <summary>
        /// 속성 관계 오류 목록
        /// </summary>
        public List<AttributeRelationError> AttributeErrors { get; set; } = new List<AttributeRelationError>();

        /// <summary>
        /// 전체 심각도
        /// </summary>
        public ErrorSeverity OverallSeverity { get; set; }

        /// <summary>
        /// 요약 정보
        /// </summary>
        public string Summary { get; set; } = string.Empty;

        /// <summary>
        /// 검수 결과 완료 처리
        /// </summary>
        public void Complete()
        {
            CompletedAt = DateTime.UtcNow;
            SpatialErrorCount = SpatialErrors.Count;
            AttributeErrorCount = AttributeErrors.Count;
            IsValid = SpatialErrorCount == 0 && AttributeErrorCount == 0;
            
            // 전체 심각도 계산
            OverallSeverity = CalculateOverallSeverity();
            
            // 요약 정보 생성
            Summary = GenerateSummary();
        }

        /// <summary>
        /// 전체 심각도를 계산합니다
        /// </summary>
        private ErrorSeverity CalculateOverallSeverity()
        {
            var maxSeverity = ErrorSeverity.Info;

            foreach (var error in SpatialErrors)
            {
                if (error.Severity > maxSeverity)
                    maxSeverity = error.Severity;
            }

            foreach (var error in AttributeErrors)
            {
                if (error.Severity > maxSeverity)
                    maxSeverity = error.Severity;
            }

            return maxSeverity;
        }

        /// <summary>
        /// 요약 정보를 생성합니다
        /// </summary>
        private string GenerateSummary()
        {
            var totalErrors = SpatialErrorCount + AttributeErrorCount;
            
            if (totalErrors == 0)
            {
                return $"관계 검수 완료: 모든 규칙 통과 (처리시간: {ProcessingTime.TotalSeconds:F1}초)";
            }
            else
            {
                return $"관계 검수 완료: {totalErrors}개 오류 발견 " +
                       $"(공간: {SpatialErrorCount}, 속성: {AttributeErrorCount}, " +
                       $"처리시간: {ProcessingTime.TotalSeconds:F1}초)";
            }
        }
    }
}
