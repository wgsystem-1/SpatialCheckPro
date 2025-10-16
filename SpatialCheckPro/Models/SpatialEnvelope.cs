namespace SpatialCheckPro.Models
{
    /// <summary>
    /// 공간 범위(Envelope)를 나타내는 클래스
    /// </summary>
    public class SpatialEnvelope
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
        public SpatialEnvelope()
        {
        }

        /// <summary>
        /// 좌표를 지정하는 생성자
        /// </summary>
        /// <param name="minX">최소 X 좌표</param>
        /// <param name="minY">최소 Y 좌표</param>
        /// <param name="maxX">최대 X 좌표</param>
        /// <param name="maxY">최대 Y 좌표</param>
        public SpatialEnvelope(double minX, double minY, double maxX, double maxY)
        {
            MinX = minX;
            MinY = minY;
            MaxX = maxX;
            MaxY = maxY;
        }

        /// <summary>
        /// 범위의 너비
        /// </summary>
        public double Width => MaxX - MinX;

        /// <summary>
        /// 범위의 높이
        /// </summary>
        public double Height => MaxY - MinY;

        /// <summary>
        /// 범위의 중심 X 좌표
        /// </summary>
        public double CenterX => (MinX + MaxX) / 2.0;

        /// <summary>
        /// 범위의 중심 Y 좌표
        /// </summary>
        public double CenterY => (MinY + MaxY) / 2.0;

        /// <summary>
        /// 다른 범위와 교차하는지 확인
        /// </summary>
        /// <param name="other">비교할 범위</param>
        /// <returns>교차 여부</returns>
        public bool Intersects(SpatialEnvelope other)
        {
            return !(other.MinX > MaxX || other.MaxX < MinX || other.MinY > MaxY || other.MaxY < MinY);
        }

        /// <summary>
        /// 다른 범위를 포함하는지 확인
        /// </summary>
        /// <param name="other">비교할 범위</param>
        /// <returns>포함 여부</returns>
        public bool Contains(SpatialEnvelope other)
        {
            return MinX <= other.MinX && MinY <= other.MinY && MaxX >= other.MaxX && MaxY >= other.MaxY;
        }
    }
}
