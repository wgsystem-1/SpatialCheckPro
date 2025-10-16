namespace SpatialCheckPro.GUI.Models
{
    /// <summary>
    /// 경계 상자를 나타내는 클래스
    /// </summary>
    public class BoundingBox
    {
        /// <summary>
        /// 최소 X 좌표
        /// </summary>
        public double MinX { get; set; }

        /// <summary>
        /// 최소 Y 좌표
        /// </summary>
        public double MinY { get; set; }

        /// <summary>
        /// 최대 X 좌표
        /// </summary>
        public double MaxX { get; set; }

        /// <summary>
        /// 최대 Y 좌표
        /// </summary>
        public double MaxY { get; set; }

        /// <summary>
        /// 기본 생성자
        /// </summary>
        public BoundingBox()
        {
        }

        /// <summary>
        /// 4개 좌표로 경계 상자 생성
        /// </summary>
        /// <param name="minX">최소 X 좌표</param>
        /// <param name="minY">최소 Y 좌표</param>
        /// <param name="maxX">최대 X 좌표</param>
        /// <param name="maxY">최대 Y 좌표</param>
        public BoundingBox(double minX, double minY, double maxX, double maxY)
        {
            MinX = minX;
            MinY = minY;
            MaxX = maxX;
            MaxY = maxY;
        }

        /// <summary>
        /// 경계 상자의 너비
        /// </summary>
        public double Width => MaxX - MinX;

        /// <summary>
        /// 경계 상자의 높이
        /// </summary>
        public double Height => MaxY - MinY;

        /// <summary>
        /// 경계 상자의 중심 X 좌표
        /// </summary>
        public double CenterX => (MinX + MaxX) / 2.0;

        /// <summary>
        /// 경계 상자의 중심 Y 좌표
        /// </summary>
        public double CenterY => (MinY + MaxY) / 2.0;

        /// <summary>
        /// 경계 상자가 유효한지 확인
        /// </summary>
        public bool IsValid => MinX <= MaxX && MinY <= MaxY;

        /// <summary>
        /// 다른 경계 상자와 교차하는지 확인
        /// </summary>
        /// <param name="other">비교할 경계 상자</param>
        /// <returns>교차 여부</returns>
        public bool Intersects(BoundingBox other)
        {
            return MinX <= other.MaxX && MaxX >= other.MinX &&
                   MinY <= other.MaxY && MaxY >= other.MinY;
        }

        /// <summary>
        /// 점이 경계 상자 내부에 있는지 확인
        /// </summary>
        /// <param name="x">X 좌표</param>
        /// <param name="y">Y 좌표</param>
        /// <returns>포함 여부</returns>
        public bool Contains(double x, double y)
        {
            return x >= MinX && x <= MaxX && y >= MinY && y <= MaxY;
        }
    }
}