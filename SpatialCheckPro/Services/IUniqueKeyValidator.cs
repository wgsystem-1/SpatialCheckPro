using System.Threading.Tasks;
using System.Collections.Generic;
using SpatialCheckPro.Models;

namespace SpatialCheckPro.Services
{
    /// <summary>
    /// 고유키 검증 인터페이스
    /// </summary>
    public interface IUniqueKeyValidator
    {
        /// <summary>
        /// 진행률 업데이트 이벤트
        /// </summary>
        event System.EventHandler<UniqueKeyValidationProgressEventArgs>? ProgressUpdated;

        /// <summary>
        /// 고유키 중복 검사를 수행합니다
        /// </summary>
        Task<UniqueKeyValidationResult> ValidateUniqueKeyAsync(string gdbPath, string tableName, string fieldName);
    }

    /// <summary>
    /// 고유키 검증 진행률 이벤트 인자
    /// </summary>
    public class UniqueKeyValidationProgressEventArgs : System.EventArgs
    {
        public int Progress { get; set; }
        public string StatusMessage { get; set; } = "";
    }
}
