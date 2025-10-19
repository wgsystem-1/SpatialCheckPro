using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Media;
using SpatialCheckPro.Models;
using SpatialCheckPro.GUI.Models;
using ErrorFeature = SpatialCheckPro.GUI.Models.ErrorFeature;

namespace SpatialCheckPro.GUI.Services
{
    /// <summary>
    /// 오류 심볼 및 스타일 관리 서비스 인터페이스
    /// </summary>
    public interface IErrorSymbolService
    {
        /// <summary>
        /// ErrorFeature에 대한 심볼 생성
        /// </summary>
        /// <param name="errorFeature">ErrorFeature 객체</param>
        /// <param name="zoomLevel">현재 줌 레벨</param>
        /// <returns>생성된 ErrorSymbol</returns>
        ErrorSymbol CreateSymbol(ErrorFeature errorFeature, double zoomLevel);

        /// <summary>
        /// 심각도별 색상 조회
        /// </summary>
        /// <param name="severity">심각도</param>
        /// <returns>해당 심각도의 색상</returns>
        Color GetSeverityColor(string severity);

        /// <summary>
        /// 오류 타입별 심볼 모양 조회
        /// </summary>
        /// <param name="errorType">오류 타입</param>
        /// <returns>해당 오류 타입의 심볼 모양</returns>
        SymbolShape GetErrorTypeSymbol(string errorType);

        /// <summary>
        /// 상태별 스타일 조회
        /// </summary>
        /// <param name="status">오류 상태</param>
        /// <returns>해당 상태의 스타일</returns>
        ErrorStyle GetStatusStyle(string status);

        /// <summary>
        /// 줌 레벨에 따른 심볼 크기 계산
        /// </summary>
        /// <param name="baseSize">기본 크기</param>
        /// <param name="zoomLevel">줌 레벨</param>
        /// <returns>조정된 심볼 크기</returns>
        double CalculateSymbolSize(double baseSize, double zoomLevel);

        /// <summary>
        /// 선택된 ErrorFeature의 하이라이트 심볼 생성
        /// </summary>
        /// <param name="errorFeature">ErrorFeature 객체</param>
        /// <param name="zoomLevel">현재 줌 레벨</param>
        /// <returns>하이라이트 심볼</returns>
        ErrorSymbol CreateHighlightSymbol(ErrorFeature errorFeature, double zoomLevel);

        /// <summary>
        /// 클러스터 심볼 생성
        /// </summary>
        /// <param name="cluster">ErrorCluster 객체</param>
        /// <param name="zoomLevel">현재 줌 레벨</param>
        /// <returns>클러스터 심볼</returns>
        ErrorSymbol CreateClusterSymbol(ErrorCluster cluster, double zoomLevel);

        /// <summary>
        /// 심볼 스타일 설정 업데이트
        /// </summary>
        /// <param name="styleSettings">새로운 스타일 설정</param>
        Task UpdateStyleSettingsAsync(SymbolStyleSettings styleSettings);

        /// <summary>
        /// 현재 스타일 설정 조회
        /// </summary>
        /// <returns>현재 스타일 설정</returns>
        SymbolStyleSettings GetCurrentStyleSettings();

        /// <summary>
        /// 기본 스타일 설정으로 초기화
        /// </summary>
        void ResetToDefaultStyles();

        /// <summary>
        /// 사용자 정의 색상 팔레트 설정
        /// </summary>
        /// <param name="colorPalette">색상 팔레트</param>
        void SetCustomColorPalette(Dictionary<string, Color> colorPalette);

        /// <summary>
        /// 심볼 캐시 초기화
        /// </summary>
        void ClearSymbolCache();
    }

    /// <summary>
    /// 심볼 모양 열거형
    /// </summary>
    public enum SymbolShape
    {
        /// <summary>
        /// 원형
        /// </summary>
        Circle,

        /// <summary>
        /// 사각형
        /// </summary>
        Square,

        /// <summary>
        /// 삼각형
        /// </summary>
        Triangle,

        /// <summary>
        /// 다이아몬드
        /// </summary>
        Diamond,

        /// <summary>
        /// 별모양
        /// </summary>
        Star,

        /// <summary>
        /// X 표시
        /// </summary>
        Cross,

        /// <summary>
        /// 플러스 표시
        /// </summary>
        Plus,

        /// <summary>
        /// 육각형
        /// </summary>
        Hexagon
    }

    /// <summary>
    /// 오류 스타일 정보
    /// </summary>
    public class ErrorStyle
    {
        /// <summary>
        /// 채우기 색상
        /// </summary>
        public Color FillColor { get; set; }

        /// <summary>
        /// 테두리 색상
        /// </summary>
        public Color StrokeColor { get; set; }

        /// <summary>
        /// 테두리 두께
        /// </summary>
        public double StrokeWidth { get; set; }

        /// <summary>
        /// 투명도 (0.0 ~ 1.0)
        /// </summary>
        public double Opacity { get; set; }

        /// <summary>
        /// 점선 패턴 (null이면 실선)
        /// </summary>
        public double[]? DashPattern { get; set; }

        /// <summary>
        /// 그림자 효과 여부
        /// </summary>
        public bool HasShadow { get; set; }

        /// <summary>
        /// 그림자 색상
        /// </summary>
        public Color ShadowColor { get; set; }

        /// <summary>
        /// 그림자 오프셋
        /// </summary>
        public (double X, double Y) ShadowOffset { get; set; }
    }

    /// <summary>
    /// 심볼 스타일 설정
    /// </summary>
    public class SymbolStyleSettings
    {
        /// <summary>
        /// 심각도별 색상 매핑
        /// </summary>
        public Dictionary<string, Color> SeverityColors { get; set; } = new Dictionary<string, Color>();

        /// <summary>
        /// 오류 타입별 심볼 모양 매핑
        /// </summary>
        public Dictionary<string, SymbolShape> ErrorTypeSymbols { get; set; } = new Dictionary<string, SymbolShape>();

        /// <summary>
        /// 상태별 스타일 매핑
        /// </summary>
        public Dictionary<string, ErrorStyle> StatusStyles { get; set; } = new Dictionary<string, ErrorStyle>();

        /// <summary>
        /// 기본 심볼 크기
        /// </summary>
        public double DefaultSymbolSize { get; set; } = 8.0;

        /// <summary>
        /// 최소 심볼 크기
        /// </summary>
        public double MinSymbolSize { get; set; } = 4.0;

        /// <summary>
        /// 최대 심볼 크기
        /// </summary>
        public double MaxSymbolSize { get; set; } = 32.0;

        /// <summary>
        /// 줌 레벨별 크기 조정 계수
        /// </summary>
        public double ZoomScaleFactor { get; set; } = 1.2;

        /// <summary>
        /// 하이라이트 색상
        /// </summary>
        public Color HighlightColor { get; set; } = Colors.Yellow;

        /// <summary>
        /// 하이라이트 테두리 두께
        /// </summary>
        public double HighlightStrokeWidth { get; set; } = 3.0;

        /// <summary>
        /// 선택 색상
        /// </summary>
        public Color SelectionColor { get; set; } = Colors.Cyan;

        /// <summary>
        /// 클러스터 색상
        /// </summary>
        public Color ClusterColor { get; set; } = Colors.Purple;

        /// <summary>
        /// 클러스터 텍스트 색상
        /// </summary>
        public Color ClusterTextColor { get; set; } = Colors.White;

        /// <summary>
        /// 애니메이션 효과 사용 여부
        /// </summary>
        public bool UseAnimation { get; set; } = true;

        /// <summary>
        /// 애니메이션 지속 시간 (밀리초)
        /// </summary>
        public int AnimationDurationMs { get; set; } = 300;
    }

    /// <summary>
    /// 심볼 캐시 항목
    /// </summary>
    public class SymbolCacheItem
    {
        /// <summary>
        /// 캐시 키
        /// </summary>
        public string Key { get; set; } = string.Empty;

        /// <summary>
        /// 캐시된 심볼
        /// </summary>
        public ErrorSymbol Symbol { get; set; } = new ErrorSymbol();

        /// <summary>
        /// 생성 시간
        /// </summary>
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// 마지막 사용 시간
        /// </summary>
        public DateTime LastUsedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// 사용 횟수
        /// </summary>
        public int UseCount { get; set; } = 1;
    }
}