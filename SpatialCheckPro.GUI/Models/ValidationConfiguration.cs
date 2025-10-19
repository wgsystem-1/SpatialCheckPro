using System.Collections.Generic;
using SpatialCheckPro.Models.Config;

namespace SpatialCheckPro.GUI.Models
{
    /// <summary>
    /// 검수 설정 통합 관리 클래스
    /// </summary>
    public class ValidationConfiguration
    {
        /// <summary>
        /// 기본 설정 파일 경로
        /// </summary>
        public ConfigFilePaths DefaultPaths { get; set; } = new();

        /// <summary>
        /// 사용자 정의 설정 파일 경로
        /// </summary>
        public ConfigFilePaths CustomPaths { get; set; } = new();

        /// <summary>
        /// 검수 단계 활성화 설정
        /// </summary>
        public StageToggles Stages { get; set; } = new();

        /// <summary>
        /// 선택된 검수 항목 (행 단위 선택)
        /// </summary>
        public SelectedCheckItems SelectedItems { get; set; } = new();

        /// <summary>
        /// 유효한 테이블 설정 경로 가져오기 (사용자 정의 우선)
        /// </summary>
        public string? GetEffectiveTableConfig() 
            => CustomPaths.TableConfig ?? DefaultPaths.TableConfig;

        /// <summary>
        /// 유효한 스키마 설정 경로 가져오기 (사용자 정의 우선)
        /// </summary>
        public string? GetEffectiveSchemaConfig() 
            => CustomPaths.SchemaConfig ?? DefaultPaths.SchemaConfig;

        /// <summary>
        /// 유효한 지오메트리 설정 경로 가져오기 (사용자 정의 우선)
        /// </summary>
        public string? GetEffectiveGeometryConfig() 
            => CustomPaths.GeometryConfig ?? DefaultPaths.GeometryConfig;

        /// <summary>
        /// 유효한 관계 설정 경로 가져오기 (사용자 정의 우선)
        /// </summary>
        public string? GetEffectiveRelationConfig() 
            => CustomPaths.RelationConfig ?? DefaultPaths.RelationConfig;

        /// <summary>
        /// 유효한 속성 설정 경로 가져오기 (사용자 정의 우선)
        /// </summary>
        public string? GetEffectiveAttributeConfig() 
            => CustomPaths.AttributeConfig ?? DefaultPaths.AttributeConfig;

        /// <summary>
        /// 유효한 지오메트리 기준 경로 가져오기 (사용자 정의 우선)
        /// </summary>
        public string? GetEffectiveGeometryCriteria() 
            => CustomPaths.GeometryCriteria ?? DefaultPaths.GeometryCriteria;
    }

    /// <summary>
    /// 설정 파일 경로 집합
    /// </summary>
    public class ConfigFilePaths
    {
        /// <summary>
        /// 테이블 검수 설정 파일 경로
        /// </summary>
        public string? TableConfig { get; set; }

        /// <summary>
        /// 스키마 검수 설정 파일 경로
        /// </summary>
        public string? SchemaConfig { get; set; }

        /// <summary>
        /// 지오메트리 검수 설정 파일 경로
        /// </summary>
        public string? GeometryConfig { get; set; }

        /// <summary>
        /// 관계 검수 설정 파일 경로
        /// </summary>
        public string? RelationConfig { get; set; }

        /// <summary>
        /// 속성 검수 설정 파일 경로
        /// </summary>
        public string? AttributeConfig { get; set; }

        /// <summary>
        /// 지오메트리 기준 파일 경로
        /// </summary>
        public string? GeometryCriteria { get; set; }
    }

    /// <summary>
    /// 검수 단계별 활성화 플래그
    /// </summary>
    public class StageToggles
    {
        /// <summary>
        /// 1단계: 테이블 검수 활성화
        /// </summary>
        public bool EnableStage1 { get; set; } = true;

        /// <summary>
        /// 2단계: 스키마 검수 활성화
        /// </summary>
        public bool EnableStage2 { get; set; } = true;

        /// <summary>
        /// 3단계: 지오메트리 검수 활성화
        /// </summary>
        public bool EnableStage3 { get; set; } = true;

        /// <summary>
        /// 4단계: 관계 검수 활성화
        /// </summary>
        public bool EnableStage4 { get; set; } = true;

        /// <summary>
        /// 5단계: 속성 검수 활성화
        /// </summary>
        public bool EnableStage5 { get; set; } = true;
    }

    /// <summary>
    /// 선택된 검수 항목 (행 단위 선택)
    /// </summary>
    public class SelectedCheckItems
    {
        /// <summary>
        /// 1단계 선택 항목
        /// </summary>
        public List<TableCheckConfig>? Stage1Items { get; set; }

        /// <summary>
        /// 2단계 선택 항목
        /// </summary>
        public List<SchemaCheckConfig>? Stage2Items { get; set; }

        /// <summary>
        /// 3단계 선택 항목
        /// </summary>
        public List<GeometryCheckConfig>? Stage3Items { get; set; }

        /// <summary>
        /// 4단계 선택 항목
        /// </summary>
        public List<RelationCheckConfig>? Stage4Items { get; set; }

        /// <summary>
        /// 5단계 선택 항목
        /// </summary>
        public List<AttributeCheckConfig>? Stage5Items { get; set; }
    }
}
