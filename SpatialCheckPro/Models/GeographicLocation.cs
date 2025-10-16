namespace SpatialCheckPro.Models
{
    /// <summary>
    /// 지리적 위치 정보
    /// </summary>
    public class GeographicLocation
    {
        /// <summary>
        /// X 좌표 (경도)
        /// </summary>
        public double X { get; set; }

        /// <summary>
        /// Y 좌표 (위도)
        /// </summary>
        public double Y { get; set; }

        /// <summary>
        /// Z 좌표 (고도)
        /// </summary>
        public double Z { get; set; }

        /// <summary>
        /// 좌표계 정보
        /// </summary>
        public string CoordinateSystem { get; set; } = string.Empty;

        /// <summary>
        /// 기본 생성자
        /// </summary>
        public GeographicLocation()
        {
        }

        /// <summary>
        /// 좌표를 지정하는 생성자
        /// </summary>
        public GeographicLocation(double x, double y, double z = 0, string coordinateSystem = "")
        {
            X = x;
            Y = y;
            Z = z;
            CoordinateSystem = coordinateSystem;
        }

        /// <summary>
        /// 문자열 표현
        /// </summary>
        public override string ToString()
        {
            return $"({X}, {Y}, {Z}) [{CoordinateSystem}]";
        }
    }
}
