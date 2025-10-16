using System;

namespace SpatialCheckPro.Models.Config
{
    /// <summary>
    /// UK(Unique Key) 검수 설정 모델
    /// </summary>
    public class UniqueKeyConfig
    {
        /// <summary>
        /// 테이블명
        /// </summary>
        public string TableName { get; set; } = string.Empty;

        /// <summary>
        /// 필드명
        /// </summary>
        public string FieldName { get; set; } = string.Empty;

        /// <summary>
        /// NULL 값 무시 여부
        /// </summary>
        public bool IgnoreNullValues { get; set; } = true;

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
        /// 설정 유효성을 검증합니다
        /// </summary>
        public bool IsValid()
        {
            return !string.IsNullOrWhiteSpace(TableName) && 
                   !string.IsNullOrWhiteSpace(FieldName);
        }

        /// <summary>
        /// 고유 식별자를 반환합니다
        /// </summary>
        public string GetUniqueKey()
        {
            return $"{TableName}.{FieldName}";
        }

        public override string ToString()
        {
            return $"UK: {TableName}.{FieldName} (활성: {IsEnabled})";
        }

        public override bool Equals(object? obj)
        {
            if (obj is UniqueKeyConfig other)
            {
                return string.Equals(TableName, other.TableName, StringComparison.OrdinalIgnoreCase) &&
                       string.Equals(FieldName, other.FieldName, StringComparison.OrdinalIgnoreCase);
            }
            return false;
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(
                TableName?.ToLowerInvariant(), 
                FieldName?.ToLowerInvariant());
        }
    }
}
