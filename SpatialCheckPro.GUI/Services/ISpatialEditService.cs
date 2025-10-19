using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NetTopologySuite.Geometries;
using SpatialCheckPro.Models;

namespace SpatialCheckPro.GUI.Services
{
    /// <summary>
    /// 공간정보 피처 편집을 담당하는 서비스 인터페이스
    /// </summary>
    public interface ISpatialEditService
    {
        /// <summary>
        /// 피처 편집 모드 시작
        /// </summary>
        /// <param name="spatialFile">편집할 공간정보 파일</param>
        /// <param name="featureClassName">편집할 피처 클래스명</param>
        /// <param name="featureId">편집할 피처 ID</param>
        /// <returns>편집 세션 정보</returns>
        Task<EditSession> StartEditSessionAsync(SpatialFileInfo spatialFile, string featureClassName, string featureId);

        /// <summary>
        /// 피처 속성 값 수정
        /// </summary>
        /// <param name="editSession">편집 세션</param>
        /// <param name="attributeName">속성명</param>
        /// <param name="newValue">새로운 값</param>
        /// <returns>수정 결과</returns>
        Task<EditResult> UpdateAttributeAsync(EditSession editSession, string attributeName, object newValue);

        /// <summary>
        /// 지오메트리 수정
        /// </summary>
        /// <param name="editSession">편집 세션</param>
        /// <param name="newGeometry">새로운 지오메트리</param>
        /// <returns>수정 결과</returns>
        Task<EditResult> UpdateGeometryAsync(EditSession editSession, Geometry newGeometry);

        /// <summary>
        /// 편집 내용 저장 및 세션 종료
        /// </summary>
        /// <param name="editSession">편집 세션</param>
        /// <returns>저장 결과</returns>
        Task<SaveResult> SaveAndCloseEditSessionAsync(EditSession editSession);

        /// <summary>
        /// 편집 내용 취소 및 세션 종료
        /// </summary>
        /// <param name="editSession">편집 세션</param>
        /// <returns>취소 결과</returns>
        Task<bool> CancelEditSessionAsync(EditSession editSession);

        /// <summary>
        /// 편집 후 자동 재검수 실행
        /// </summary>
        /// <param name="spatialFile">편집된 공간정보 파일</param>
        /// <param name="editedFeatureIds">편집된 피처 ID 목록</param>
        /// <returns>재검수 결과</returns>
        Task<ValidationResult> RevalidateAfterEditAsync(SpatialFileInfo spatialFile, IEnumerable<string> editedFeatureIds);

        /// <summary>
        /// Undo 작업 수행
        /// </summary>
        /// <param name="editSession">편집 세션</param>
        /// <returns>Undo 결과</returns>
        Task<EditResult> UndoAsync(EditSession editSession);

        /// <summary>
        /// Redo 작업 수행
        /// </summary>
        /// <param name="editSession">편집 세션</param>
        /// <returns>Redo 결과</returns>
        Task<EditResult> RedoAsync(EditSession editSession);

        /// <summary>
        /// 편집 세션의 변경 이력 조회
        /// </summary>
        /// <param name="editSession">편집 세션</param>
        /// <returns>변경 이력 목록</returns>
        List<EditChange> GetEditHistory(EditSession editSession);
    }
}