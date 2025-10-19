using System.Windows.Media;

namespace SpatialCheckPro.GUI.Models
{
    /// <summary>
    /// 오류 피처의 심볼 정보를 나타내는 클래스
    /// </summary>
    public class ErrorSymbol
    {
        /// <summary>심볼 ID</summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>X 좌표</summary>
        public double X { get; set; }

        /// <summary>Y 좌표</summary>
        public double Y { get; set; }

        /// <summary>지오메트리 타입</summary>
        public string GeometryType { get; set; } = "Point";

        /// <summary>마커 스타일</summary>
        public string MarkerStyle { get; set; } = "Circle";

        /// <summary>
        /// 심볼 색상
        /// </summary>
        public Color Color { get; set; } = Colors.Red;

        /// <summary>채우기 색상</summary>
        public Color FillColor { get; set; } = Colors.Red;

        /// <summary>테두리 색상</summary>
        public Color StrokeColor { get; set; } = Colors.Black;

        /// <summary>테두리 두께</summary>
        public double StrokeWidth { get; set; } = 1.0;

        /// <summary>
        /// 심볼 크기
        /// </summary>
        public double Size { get; set; } = 8.0;

        /// <summary>
        /// 심볼 모양 (Circle, Square, Triangle 등)
        /// </summary>
        public string Shape { get; set; } = "Circle";

        /// <summary>
        /// 테두리 색상
        /// </summary>
        public Color BorderColor { get; set; } = Colors.Black;

        /// <summary>
        /// 테두리 두께
        /// </summary>
        public double BorderWidth { get; set; } = 1.0;

        /// <summary>
        /// 투명도 (0.0 ~ 1.0)
        /// </summary>
        public double Opacity { get; set; } = 1.0;

        /// <summary>표시 여부</summary>
        public bool IsVisible { get; set; } = true;

        /// <summary>Z 인덱스</summary>
        public int ZIndex { get; set; } = 0;

        /// <summary>애니메이션 여부</summary>
        public bool IsAnimated { get; set; } = false;

        /// <summary>텍스트</summary>
        public string Text { get; set; } = string.Empty;

        /// <summary>텍스트 색상</summary>
        public Color TextColor { get; set; } = Colors.Black;

        /// <summary>텍스트 크기</summary>
        public double TextSize { get; set; } = 12.0;

        /// <summary>
        /// 심볼이 선택되었는지 여부
        /// </summary>
        public bool IsSelected { get; set; } = false;

        /// <summary>
        /// 심볼이 강조 표시되었는지 여부
        /// </summary>
        public bool IsHighlighted { get; set; } = false;

        /// <summary>심볼 복제</summary>
        public ErrorSymbol Clone()
        {
            return new ErrorSymbol
            {
                Id = Id,
                X = X,
                Y = Y,
                GeometryType = GeometryType,
                MarkerStyle = MarkerStyle,
                Color = Color,
                FillColor = FillColor,
                StrokeColor = StrokeColor,
                StrokeWidth = StrokeWidth,
                Size = Size,
                Shape = Shape,
                BorderColor = BorderColor,
                BorderWidth = BorderWidth,
                Opacity = Opacity,
                IsVisible = IsVisible,
                ZIndex = ZIndex,
                IsAnimated = IsAnimated,
                Text = Text,
                TextColor = TextColor,
                TextSize = TextSize,
                IsSelected = IsSelected,
                IsHighlighted = IsHighlighted
            };
        }

        /// <summary>하이라이트 스타일 적용</summary>
        public void ApplyHighlightStyle()
        {
            IsHighlighted = true;
            StrokeWidth = BorderWidth * 2;
            StrokeColor = Colors.Yellow;
        }

        /// <summary>하이라이트 스타일 제거</summary>
        public void RemoveHighlightStyle()
        {
            IsHighlighted = false;
            StrokeWidth = BorderWidth;
            StrokeColor = BorderColor;
        }

        /// <summary>선택 스타일 적용</summary>
        public void ApplySelectionStyle()
        {
            IsSelected = true;
            StrokeWidth = BorderWidth * 1.5;
            StrokeColor = Colors.Blue;
        }

        /// <summary>선택 스타일 제거</summary>
        public void RemoveSelectionStyle()
        {
            IsSelected = false;
            StrokeWidth = BorderWidth;
            StrokeColor = BorderColor;
        }
    }
}