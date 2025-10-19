using System;
using System.Collections.Generic;

namespace SpatialCheckPro.Models
{
    /// <summary>
    /// 편집 작업 결과를 나타내는 모델 클래스
    /// </summary>
    public class EditResult
    {
        /// <summary>편집 성공 여부</summary>
        public bool IsSuccess { get; set; }

        /// <summary>오류 메시지 (실패 시)</summary>
        public string? ErrorMessage { get; set; }

        /// <summary>경고 메시지 목록</summary>
        public List<string> Warnings { get; set; } = new List<string>();

        /// <summary>편집된 피처 ID</summary>
        public string? FeatureId { get; set; }

        /// <summary>편집 타입</summary>
        public EditChangeType EditType { get; set; }

        /// <summary>편집 시간</summary>
        public DateTime EditTime { get; set; } = DateTime.UtcNow;

        /// <summary>유효성 검사 결과</summary>
        public ValidationResult? ValidationResult { get; set; }

        /// <summary>추가 메타데이터</summary>
        public Dictionary<string, object> Metadata { get; set; } = new Dictionary<string, object>();

        /// <summary>성공적인 편집 결과 생성</summary>
        public static EditResult Success(string featureId, EditChangeType editType)
        {
            return new EditResult
            {
                IsSuccess = true,
                FeatureId = featureId,
                EditType = editType
            };
        }

        /// <summary>실패한 편집 결과 생성</summary>
        public static EditResult Failure(string errorMessage, EditChangeType editType)
        {
            return new EditResult
            {
                IsSuccess = false,
                ErrorMessage = errorMessage,
                EditType = editType
            };
        }
    }

    /// <summary>
    /// 저장 결과를 나타내는 모델 클래스
    /// </summary>
    public class SaveResult
    {
        /// <summary>저장 성공 여부</summary>
        public bool IsSuccess { get; set; }

        /// <summary>오류 메시지 (실패 시)</summary>
        public string? ErrorMessage { get; set; }

        /// <summary>저장된 변경사항 개수</summary>
        public int SavedChangesCount { get; set; }

        /// <summary>저장 시간</summary>
        public DateTime SaveTime { get; set; } = DateTime.UtcNow;

        /// <summary>증분 재검수 결과</summary>
        public ValidationResult? IncrementalValidationResult { get; set; }

        /// <summary>재검수 소요 시간 (밀리초)</summary>
        public long RevalidationTimeMs { get; set; }

        /// <summary>성공적인 저장 결과 생성</summary>
        public static SaveResult Success(int changesCount)
        {
            return new SaveResult
            {
                IsSuccess = true,
                SavedChangesCount = changesCount
            };
        }

        /// <summary>실패한 저장 결과 생성</summary>
        public static SaveResult Failure(string errorMessage)
        {
            return new SaveResult
            {
                IsSuccess = false,
                ErrorMessage = errorMessage
            };
        }
    }
}
