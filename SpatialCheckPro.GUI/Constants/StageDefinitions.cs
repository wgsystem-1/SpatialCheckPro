#nullable enable
using System.Collections.Generic;
using System.Linq;

namespace SpatialCheckPro.GUI.Constants
{
    /// <summary>
    /// 검수 단계 메타데이터를 정의하는 도우미 클래스
    /// </summary>
    internal static class StageDefinitions
    {
        private static readonly StageDefinition[] _definitions =
        {
            new StageDefinition(0, "stage0_file_integrity", "FileGDB 완전성 검수", false),
            new StageDefinition(1, "stage1_table_check", "테이블 검수", false),
            new StageDefinition(2, "stage2_schema_check", "스키마 검수", true),
            new StageDefinition(3, "stage3_geometry_check", "지오메트리 검수", true),
            new StageDefinition(4, "stage4_attribute_relation", "속성 관계 검수", true),
            new StageDefinition(5, "stage5_spatial_relation", "공간 관계 검수", false)
        };

        /// <summary>
        /// 모든 단계 정의 목록을 반환합니다
        /// </summary>
        internal static IReadOnlyList<StageDefinition> All => _definitions;

        /// <summary>
        /// 단계 번호로 단계 정의를 조회합니다
        /// </summary>
        /// <param name="stageNumber">단계 번호</param>
        internal static StageDefinition GetByNumber(int stageNumber)
        {
            return _definitions.FirstOrDefault(d => d.StageNumber == stageNumber)
                   ?? new StageDefinition(stageNumber, $"stage{stageNumber}_dynamic", $"{stageNumber}단계", false);
        }

        /// <summary>
        /// 단계 번호로 단계 식별자(ID)를 반환합니다
        /// </summary>
        /// <param name="stageNumber">단계 번호</param>
        internal static string GetStageId(int stageNumber) => GetByNumber(stageNumber).StageId;

        /// <summary>
        /// 단계 번호로 표시명을 반환합니다
        /// </summary>
        /// <param name="stageNumber">단계 번호</param>
        internal static string GetStageName(int stageNumber) => GetByNumber(stageNumber).StageName;
    }

    /// <summary>
    /// 단계 정의 정보를 담는 레코드
    /// </summary>
    /// <param name="StageNumber">단계 번호</param>
    /// <param name="StageId">단계 식별자</param>
    /// <param name="StageName">단계 표시명</param>
    /// <param name="IsParallelGroup">병렬 그룹 여부</param>
    public record StageDefinition(int StageNumber, string StageId, string StageName, bool IsParallelGroup);
}


