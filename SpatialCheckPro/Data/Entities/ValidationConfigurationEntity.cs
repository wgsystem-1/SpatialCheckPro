using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SpatialCheckPro.Data
{
    /// <summary>
    /// 검수 설정 엔티티
    /// </summary>
    [Table("ValidationConfigurations")]
    public class ValidationConfigurationEntity
    {
        /// <summary>
        /// 설정 ID (기본키)
        /// </summary>
        [Key]
        public int Id { get; set; }

        /// <summary>
        /// 설정 이름
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// 설정 값 (JSON)
        /// </summary>
        public string Value { get; set; } = string.Empty;

        /// <summary>
        /// 생성 시간
        /// </summary>
        public DateTime CreatedAt { get; set; }

        /// <summary>
        /// 수정 시간
        /// </summary>
        public DateTime UpdatedAt { get; set; }
    }
}
