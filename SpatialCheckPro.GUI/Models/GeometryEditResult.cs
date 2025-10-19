#nullable enable
using System;
using NetTopologySuite.Geometries;

namespace SpatialCheckPro.GUI.Models
{
    /// <summary>
    /// 지오메트리 편집 결과
    /// </summary>
    public class GeometryEditResult
    {
        /// <summary>
        /// 편집 성공 여부
        /// </summary>
        public bool IsSuccess { get; set; }

        /// <summary>
        /// 편집된 지오메트리
        /// </summary>
        public Geometry? EditedGeometry { get; set; }

        /// <summary>
        /// 오류 메시지
        /// </summary>
        public string? ErrorMessage { get; set; }

        /// <summary>
        /// 편집 시간
        /// </summary>
        public DateTime EditTime { get; set; } = DateTime.Now;

        /// <summary>
        /// 편집 작업 설명
        /// </summary>
        public string? Description { get; set; }

        /// <summary>
        /// 성공 결과 생성
        /// </summary>
        /// <param name="editedGeometry">편집된 지오메트리</param>
        /// <param name="description">작업 설명</param>
        /// <returns>성공 결과</returns>
        public static GeometryEditResult Success(Geometry editedGeometry, string? description = null)
        {
            return new GeometryEditResult
            {
                IsSuccess = true,
                EditedGeometry = editedGeometry,
                Description = description
            };
        }

        /// <summary>
        /// 실패 결과 생성
        /// </summary>
        /// <param name="errorMessage">오류 메시지</param>
        /// <returns>실패 결과</returns>
        public static GeometryEditResult Failure(string errorMessage)
        {
            return new GeometryEditResult
            {
                IsSuccess = false,
                ErrorMessage = errorMessage
            };
        }
    }
}