using System;

namespace SpatialCheckPro.Models.Config
{
    /// <summary>
    /// FK(Foreign Key) 검수 설정 모델
    /// </summary>
    public class ForeignKeyConfig
    {
        /// <summary>
        /// 소스 테이블명
        /// </summary>
        public string SourceTable { get; set; } = string.Empty;

        /// <summary>
        /// 소스 필드명
        /// </summary>
        public string SourceField { get; set; } = string.Empty;

        /// <summary>
        /// 참조 테이블명
        /// </summary>
        public string ReferenceTable { get; set; } = string.Empty;

        /// <summary>
        /// 참조 필드명
        /// </summary>
        public string ReferenceField { get; set; } = string.Empty;

        /// <summary>
        /// NULL 값 허용 여부
        /// </summary>
        public bool AllowNullValues { get; set; } = true;

        /// <summary>
        /// 대소문자 구분 여부
        /// </summary>
        public bool CaseSensitive { get; set; } = false;

        /// <summary>
        /// 검수 우선순위 (낮을수록 우선)
        /// </summary>
        public int Priority { get; set; } = 0;

        /// <summary>
        /// 검수 활성화 여부
        /// </summary>
        public bool IsEnabled { get; set; } = true;

        /// <summary>
        /// 설명
        /// </summary>
        public string? Description { get; set; }

        /// <summary>
        /// 관계 타입 (1:1, 1:N, N:1, N:N)
        /// </summary>
        public string RelationType { get; set; } = "N:1";

        /// <summary>
        /// 설정 유효성을 검증합니다
        /// </summary>
        public bool IsValid()
        {
            return !string.IsNullOrWhiteSpace(SourceTable) && 
                   !string.IsNullOrWhiteSpace(SourceField) &&
                   !string.IsNullOrWhiteSpace(ReferenceTable) && 
                   !string.IsNullOrWhiteSpace(ReferenceField);
        }

        /// <summary>
        /// 고유 식별자를 반환합니다
        /// </summary>
        public string GetUniqueKey()
        {
            return $"{SourceTable}.{SourceField}->{ReferenceTable}.{ReferenceField}";
        }

        /// <summary>
        /// 관계 설명을 반환합니다
        /// </summary>
        public string GetRelationDescription()
        {
            return $"{SourceTable}.{SourceField} → {ReferenceTable}.{ReferenceField}";
        }

        public override string ToString()
        {
            return $"FK: {GetRelationDescription()} (활성: {IsEnabled})";
        }

        public override bool Equals(object? obj)
        {
            if (obj is ForeignKeyConfig other)
            {
                return string.Equals(SourceTable, other.SourceTable, StringComparison.OrdinalIgnoreCase) &&
                       string.Equals(SourceField, other.SourceField, StringComparison.OrdinalIgnoreCase) &&
                       string.Equals(ReferenceTable, other.ReferenceTable, StringComparison.OrdinalIgnoreCase) &&
                       string.Equals(ReferenceField, other.ReferenceField, StringComparison.OrdinalIgnoreCase);
            }
            return false;
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(
                SourceTable?.ToLowerInvariant(), 
                SourceField?.ToLowerInvariant(),
                ReferenceTable?.ToLowerInvariant(), 
                ReferenceField?.ToLowerInvariant());
        }
    }
}
