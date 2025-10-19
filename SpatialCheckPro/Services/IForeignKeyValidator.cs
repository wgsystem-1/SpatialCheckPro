using System.Threading.Tasks;
using System.Collections.Generic;

namespace SpatialCheckPro.Services
{
    /// <summary>
    /// 외래키 검증 인터페이스
    /// </summary>
    public interface IForeignKeyValidator
    {
        /// <summary>
        /// 외래키 참조 무결성 검사를 수행합니다
        /// </summary>
        Task<ForeignKeyValidationResult> ValidateForeignKeyAsync(
            string gdbPath, 
            string sourceTable, 
            string sourceField,
            string referenceTable, 
            string referenceField);
    }

    /// <summary>
    /// 외래키 검증 결과
    /// </summary>
    public class ForeignKeyValidationResult
    {
        public bool IsValid { get; set; }
        public int OrphanCount { get; set; }
        public List<string> OrphanValues { get; set; } = new List<string>();
        public string Message { get; set; } = "";
    }
}
