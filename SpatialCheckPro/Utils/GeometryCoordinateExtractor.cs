using OSGeo.OGR;
using NetTopologySuite.Geometries;
using NetTopologySuite.Operation.Valid;

namespace SpatialCheckPro.Utils
{
    /// <summary>
    /// 지오메트리 오류 위치 추출을 위한 공통 유틸리티 클래스
    /// </summary>
    public static class GeometryCoordinateExtractor
    {
        /// <summary>
        /// GDAL Geometry의 Envelope 중심점 추출
        /// </summary>
        public static (double X, double Y) GetEnvelopeCenter(OSGeo.OGR.Geometry geometry)
        {
            if (geometry == null || geometry.IsEmpty())
                return (0, 0);

            var envelope = new OSGeo.OGR.Envelope();
            geometry.GetEnvelope(envelope);
            double centerX = (envelope.MinX + envelope.MaxX) / 2.0;
            double centerY = (envelope.MinY + envelope.MaxY) / 2.0;
            return (centerX, centerY);
        }

        /// <summary>
        /// LineString의 중점 추출
        /// </summary>
        public static (double X, double Y) GetLineStringMidpoint(OSGeo.OGR.Geometry lineString)
        {
            if (lineString == null || lineString.IsEmpty())
                return (0, 0);

            if (lineString.GetGeometryType() != wkbGeometryType.wkbLineString)
                return GetEnvelopeCenter(lineString);

            int pointCount = lineString.GetPointCount();
            if (pointCount == 0) return (0, 0);

            int midIndex = pointCount / 2;
            return (lineString.GetX(midIndex), lineString.GetY(midIndex));
        }

        /// <summary>
        /// Polygon 외부 링의 중점 추출
        /// </summary>
        public static (double X, double Y) GetPolygonRingMidpoint(OSGeo.OGR.Geometry polygon)
        {
            if (polygon == null || polygon.IsEmpty())
                return (0, 0);

            if (polygon.GetGeometryCount() > 0)
            {
                var exteriorRing = polygon.GetGeometryRef(0);
                if (exteriorRing != null && exteriorRing.GetPointCount() > 0)
                {
                    int pointCount = exteriorRing.GetPointCount();
                    int midIndex = pointCount / 2;
                    return (exteriorRing.GetX(midIndex), exteriorRing.GetY(midIndex));
                }
            }

            return GetEnvelopeCenter(polygon);
        }

        /// <summary>
        /// 첫 번째 정점 추출
        /// </summary>
        public static (double X, double Y) GetFirstVertex(OSGeo.OGR.Geometry geometry)
        {
            if (geometry == null || geometry.IsEmpty())
                return (0, 0);

            if (geometry.GetPointCount() > 0)
            {
                return (geometry.GetX(0), geometry.GetY(0));
            }

            return GetEnvelopeCenter(geometry);
        }

        /// <summary>
        /// NTS ValidationError에서 좌표 추출, 없으면 IsSimpleOp의 비단순 위치, 최종 폴백은 Envelope 중심
        /// </summary>
        public static (double X, double Y) GetValidationErrorLocation(NetTopologySuite.Geometries.Geometry ntsGeometry, TopologyValidationError? validationError)
        {
            // 1) ValidationError 가 좌표를 제공하는 경우
            if (validationError?.Coordinate != null)
            {
                return (validationError.Coordinate.X, validationError.Coordinate.Y);
            }

            if (ntsGeometry == null || ntsGeometry.IsEmpty)
                return (0, 0);

            // 2) 단순성 검사(Self-intersection 등)에서 비단순 위치 추출
            try
            {
                var simpleOp = new NetTopologySuite.Operation.Valid.IsSimpleOp(ntsGeometry);
                if (!simpleOp.IsSimple())
                {
                    var nonSimple = simpleOp.NonSimpleLocation;
                    if (nonSimple != null)
                    {
                        return (nonSimple.X, nonSimple.Y);
                    }
                }
            }
            catch
            {
                // NTS 예외 무시하고 폴백
            }

            // 3) 최종 폴백: Envelope 중심
            var env = ntsGeometry.EnvelopeInternal;
            return ((env.MinX + env.MaxX) / 2.0, (env.MinY + env.MaxY) / 2.0);
        }

        /// <summary>
        /// 두 점 사이의 간격 선분 WKT 생성 (언더슛/오버슛용)
        /// </summary>
        public static string CreateGapLineWkt(NetTopologySuite.Geometries.Point startPoint, NetTopologySuite.Geometries.Point endPoint)
        {
            var lineString = new NetTopologySuite.Geometries.LineString(new[] { startPoint.Coordinate, endPoint.Coordinate });
            return lineString.ToText();
        }

        /// <summary>
        /// Polygon의 내부 중심점 추출 (PointOnSurface 사용, 실패 시 Centroid, 최종 폴백 Envelope)
        /// - PointOnSurface: 폴리곤 내부에 반드시 위치하는 점 (오목 폴리곤에도 안전)
        /// - Centroid: 무게중심 (오목 폴리곤의 경우 외부에 위치할 수 있음)
        /// </summary>
        public static (double X, double Y) GetPolygonInteriorPoint(OSGeo.OGR.Geometry geometry)
        {
            if (geometry == null || geometry.IsEmpty())
                return (0, 0);

            try
            {
                // 1순위: PointOnSurface (폴리곤 내부 보장)
                using var pointOnSurface = geometry.PointOnSurface();
                if (pointOnSurface != null && !pointOnSurface.IsEmpty())
                {
                    var point = new double[3];
                    pointOnSurface.GetPoint(0, point);
                    return (point[0], point[1]);
                }
            }
            catch
            {
                // PointOnSurface 실패 시 Centroid 시도
            }

            try
            {
                // 2순위: Centroid (무게중심)
                using var centroid = geometry.Centroid();
                if (centroid != null && !centroid.IsEmpty())
                {
                    var point = new double[3];
                    centroid.GetPoint(0, point);
                    return (point[0], point[1]);
                }
            }
            catch
            {
                // Centroid 실패 시 Envelope 중심 사용
            }

            // 3순위: Envelope 중심 (폴백)
            return GetEnvelopeCenter(geometry);
        }

        /// <summary>
        /// NTS ValidationError 타입을 한글 오류명으로 변환
        /// </summary>
        public static string GetKoreanErrorType(int errorType)
        {
            return errorType switch
            {
                0 => "자체 꼬임",
                1 => "링이 닫히지 않음",
                2 => "홀이 쉘 외부에 위치",
                3 => "중첩된 홀",
                4 => "쉘과 홀 연결 해제",
                5 => "링 자체 교차",
                6 => "중첩된 링",
                7 => "중복된 링",
                8 => "너무 적은 점",
                9 => "유효하지 않은 좌표",
                10 => "링 자체 교차",
                _ => "지오메트리 유효성 오류"
            };
        }
    }
}


