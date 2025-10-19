using SpatialCheckPro.Models;
using OSGeo.OGR;
using System;

namespace SpatialCheckPro.GUI.Models
{
    /// <summary>
    /// 지도 상호작용을 위한 오류 피처 모델 클래스
    /// QcError 기반으로 지도에서 표시되고 조작되는 오류 피처를 나타냅니다.
    /// </summary>
    public class ErrorFeature
    {
        /// <summary>
        /// 오류 피처의 고유 식별자
        /// </summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// 연결된 QC 오류 정보
        /// </summary>
        public QcError QcError { get; set; } = new QcError();

        /// <summary>
        /// GDAL 지오메트리 객체
        /// </summary>
        public Geometry? Geometry { get; set; }

        /// <summary>
        /// 오류 피처의 심볼 정보
        /// </summary>
        public ErrorSymbol Symbol { get; set; } = new ErrorSymbol();

        /// <summary>
        /// 선택 상태 여부
        /// </summary>
        public bool IsSelected { get; set; }

        /// <summary>
        /// 하이라이트 상태 여부
        /// </summary>
        public bool IsHighlighted { get; set; }

        /// <summary>
        /// 마지막 업데이트 시간
        /// </summary>
        public DateTime LastUpdated { get; set; } = DateTime.UtcNow;

        // QcError 기반 속성들 (호환성을 위해 추가)
        /// <summary>오류 코드 (QcError.ErrCode 기반)</summary>
        public string ErrorCode => QcError?.ErrCode ?? string.Empty;

        /// <summary>오류 유형 (QcError.ErrType 기반)</summary>
        public string ErrorType => QcError?.ErrType ?? string.Empty;

        /// <summary>심각도 (QcError.Severity 기반)</summary>
        public string Severity => QcError?.Severity ?? string.Empty;

        /// <summary>상태 (QcError.Status 기반)</summary>
        public string Status => QcError?.Status ?? string.Empty;

        /// <summary>X 좌표 (QcError.X 기반)</summary>
        public double X => QcError?.X ?? 0.0;

        /// <summary>Y 좌표 (QcError.Y 기반)</summary>
        public double Y => QcError?.Y ?? 0.0;

        /// <summary>메시지 (QcError.Message 기반)</summary>
        public string Message => QcError?.Message ?? string.Empty;

        /// <summary>소스 클래스 (QcError.SourceClass 기반)</summary>
        public string SourceClass => QcError?.SourceClass ?? string.Empty;

        /// <summary>생성 시간 (QcError.CreatedUTC 기반)</summary>
        public DateTime CreatedAt => QcError?.CreatedUTC ?? DateTime.UtcNow;

        /// <summary>
        /// 지정된 좌표까지의 거리를 계산합니다
        /// </summary>
        /// <param name="x">대상 X 좌표</param>
        /// <param name="y">대상 Y 좌표</param>
        /// <returns>유클리드 거리 (미터)</returns>
        public double DistanceTo(double x, double y)
        {
            var dx = x - QcError.X;
            var dy = y - QcError.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        /// <summary>
        /// 다른 ErrorFeature까지의 거리를 계산합니다
        /// </summary>
        /// <param name="other">대상 ErrorFeature</param>
        /// <returns>유클리드 거리 (미터)</returns>
        public double DistanceTo(ErrorFeature other)
        {
            return DistanceTo(other.X, other.Y);
        }

        /// <summary>
        /// 지정된 좌표가 허용 거리 내에 있는지 확인합니다
        /// </summary>
        /// <param name="x">확인할 X 좌표</param>
        /// <param name="y">확인할 Y 좌표</param>
        /// <param name="tolerance">허용 거리 (미터)</param>
        /// <returns>허용 거리 내에 있으면 true</returns>
        public bool ContainsPoint(double x, double y, double tolerance)
        {
            return DistanceTo(x, y) <= tolerance;
        }

        /// <summary>
        /// 오류 피처의 바운딩 박스를 반환합니다
        /// </summary>
        /// <returns>바운딩 박스 (Envelope)</returns>
        public Envelope GetBounds()
        {
            if (Geometry != null)
            {
                var envelope = new Envelope();
                Geometry.GetEnvelope(envelope);
                return envelope;
            }
            else
            {
                // 지오메트리가 없으면 점 좌표 기준으로 작은 바운딩 박스 생성
                var envelope = new Envelope();
                envelope.MinX = QcError.X - 1.0;
                envelope.MaxX = QcError.X + 1.0;
                envelope.MinY = QcError.Y - 1.0;
                envelope.MaxY = QcError.Y + 1.0;
                return envelope;
            }
        }

        /// <summary>
        /// WKT 문자열에서 GDAL 지오메트리를 생성합니다
        /// </summary>
        public void CreateGeometryFromWKT()
        {
            if (!string.IsNullOrEmpty(QcError.GeometryWKT))
            {
                try
                {
                    Geometry = Geometry.CreateFromWkt(QcError.GeometryWKT);
                }
                catch (Exception)
                {
                    // WKT 파싱 실패 시 점 지오메트리로 대체
                    var pointWkt = $"POINT({QcError.X} {QcError.Y})";
                    Geometry = Geometry.CreateFromWkt(pointWkt);
                }
            }
            else
            {
                // WKT가 없으면 좌표 기반 점 지오메트리 생성
                var pointWkt = $"POINT({QcError.X} {QcError.Y})";
                Geometry = Geometry.CreateFromWkt(pointWkt);
            }
        }

        /// <summary>
        /// 오류 피처의 중심점 좌표를 반환합니다
        /// </summary>
        /// <returns>(X, Y) 좌표 튜플</returns>
        public (double X, double Y) GetCentroid()
        {
            if (Geometry != null)
            {
                var centroid = Geometry.Centroid();
                return (centroid.GetX(0), centroid.GetY(0));
            }
            else
            {
                return (QcError.X, QcError.Y);
            }
        }

        /// <summary>
        /// 오류 피처를 복제합니다
        /// </summary>
        /// <returns>복제된 ErrorFeature 객체</returns>
        public ErrorFeature Clone()
        {
            return new ErrorFeature
            {
                Id = Id,
                QcError = QcError, // QcError는 참조 복사 (필요시 깊은 복사 구현)
                Geometry = Geometry?.Clone(),
                Symbol = Symbol.Clone(),
                IsSelected = IsSelected,
                IsHighlighted = IsHighlighted,
                LastUpdated = LastUpdated
            };
        }

        /// <summary>
        /// 표시용 텍스트를 반환합니다
        /// </summary>
        /// <returns>표시용 텍스트</returns>
        public string GetDisplayText()
        {
            return $"{ErrorCode}: {Message}";
        }

        /// <summary>
        /// 오류 피처의 문자열 표현을 반환합니다
        /// </summary>
        /// <returns>오류 정보 문자열</returns>
        public override string ToString()
        {
            return $"ErrorFeature[{Id}]: {QcError.ErrCode} - {QcError.Severity} at ({QcError.X:F2}, {QcError.Y:F2})";
        }
    }
}