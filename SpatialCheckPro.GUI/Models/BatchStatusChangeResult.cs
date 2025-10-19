#nullable enable
using System;
using System.Collections.Generic;

namespace SpatialCheckPro.GUI.Models
{
    /// <summary>
    /// 일괄 상태 변경 결과
    /// </summary>
    public class BatchStatusChangeResult
    {
        /// <summary>
        /// 성공 여부
        /// </summary>
        public bool IsSuccess { get; set; }

        /// <summary>
        /// 처리된 항목 수
        /// </summary>
        public int ProcessedCount { get; set; }

        /// <summary>
        /// 성공한 항목 수
        /// </summary>
        public int SuccessCount { get; set; }

        /// <summary>
        /// 실패한 항목 수
        /// </summary>
        public int FailedCount { get; set; }

        /// <summary>
        /// 오류 메시지 목록
        /// </summary>
        public List<string> ErrorMessages { get; set; } = new List<string>();

        /// <summary>
        /// 처리 시간
        /// </summary>
        public TimeSpan ProcessingTime { get; set; }

        /// <summary>
        /// 상세 결과 메시지
        /// </summary>
        public string? DetailMessage { get; set; }

        /// <summary>
        /// 생성자
        /// </summary>
        public BatchStatusChangeResult()
        {
            ErrorMessages = new List<string>();
        }

        /// <summary>
        /// 성공 결과 생성
        /// </summary>
        /// <param name="processedCount">처리된 항목 수</param>
        /// <param name="processingTime">처리 시간</param>
        /// <returns>성공 결과</returns>
        public static BatchStatusChangeResult Success(int processedCount, TimeSpan processingTime)
        {
            return new BatchStatusChangeResult
            {
                IsSuccess = true,
                ProcessedCount = processedCount,
                SuccessCount = processedCount,
                FailedCount = 0,
                ProcessingTime = processingTime
            };
        }

        /// <summary>
        /// 실패 결과 생성
        /// </summary>
        /// <param name="errorMessage">오류 메시지</param>
        /// <returns>실패 결과</returns>
        public static BatchStatusChangeResult Failure(string errorMessage)
        {
            return new BatchStatusChangeResult
            {
                IsSuccess = false,
                ProcessedCount = 0,
                SuccessCount = 0,
                FailedCount = 0,
                ErrorMessages = new List<string> { errorMessage }
            };
        }
    }
}