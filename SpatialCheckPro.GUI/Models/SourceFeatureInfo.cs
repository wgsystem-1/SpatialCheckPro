#nullable enable
using System;
using System.Collections.Generic;

namespace SpatialCheckPro.GUI.Models
{
    /// <summary>
    /// 원본 피처 정보 모델
    /// Requirements: 3.4, 3.5 - 원본 FeatureClass와 ObjectID 정보
    /// </summary>
    public class SourceFeatureInfo
    {
        /// <summary>
        /// 원본 FeatureClass 이름
        /// </summary>
        public string SourceClass { get; set; } = string.Empty;

        /// <summary>
        /// 원본 ObjectID
        /// </summary>
        public long SourceOID { get; set; }

        /// <summary>
        /// 원본 GlobalID
        /// </summary>
        public string? SourceGlobalID { get; set; }

        /// <summary>
        /// 피처의 지오메트리 타입
        /// </summary>
        public string GeometryType { get; set; } = string.Empty;

        /// <summary>
        /// 피처의 지오메트리 (WKT 형태)
        /// </summary>
        public string? GeometryWKT { get; set; }

        /// <summary>
        /// 피처의 중심점 X 좌표
        /// </summary>
        public double CenterX { get; set; }

        /// <summary>
        /// 피처의 중심점 Y 좌표
        /// </summary>
        public double CenterY { get; set; }

        /// <summary>
        /// 피처의 바운딩 박스 (MinX, MinY, MaxX, MaxY)
        /// </summary>
        public double[] BoundingBox { get; set; } = new double[4];

        /// <summary>
        /// 피처의 속성 정보
        /// </summary>
        public Dictionary<string, object?> Attributes { get; set; } = new Dictionary<string, object?>();

        /// <summary>
        /// 피처가 존재하는지 여부
        /// </summary>
        public bool Exists { get; set; }

        /// <summary>
        /// 피처의 생성 시간
        /// </summary>
        public DateTime? CreatedDate { get; set; }

        /// <summary>
        /// 피처의 수정 시간
        /// </summary>
        public DateTime? ModifiedDate { get; set; }

        /// <summary>
        /// 피처의 생성자
        /// </summary>
        public string? CreatedBy { get; set; }

        /// <summary>
        /// 피처의 수정자
        /// </summary>
        public string? ModifiedBy { get; set; }

        /// <summary>
        /// 연결된 오류 개수
        /// </summary>
        public int RelatedErrorCount { get; set; }

        /// <summary>
        /// 피처의 면적 (면형 지오메트리인 경우)
        /// </summary>
        public double? Area { get; set; }

        /// <summary>
        /// 피처의 길이 (선형 지오메트리인 경우)
        /// </summary>
        public double? Length { get; set; }

        /// <summary>
        /// 피처의 둘레 (면형 지오메트리인 경우)
        /// </summary>
        public double? Perimeter { get; set; }

        /// <summary>
        /// 추가 메타데이터
        /// </summary>
        public Dictionary<string, object?> Metadata { get; set; } = new Dictionary<string, object?>();

        /// <summary>
        /// 생성자
        /// </summary>
        public SourceFeatureInfo()
        {
        }

        /// <summary>
        /// 생성자
        /// </summary>
        /// <param name="sourceClass">원본 FeatureClass 이름</param>
        /// <param name="sourceOID">원본 ObjectID</param>
        public SourceFeatureInfo(string sourceClass, long sourceOID)
        {
            SourceClass = sourceClass;
            SourceOID = sourceOID;
        }

        /// <summary>
        /// 피처의 위치 정보 문자열
        /// </summary>
        public string LocationString => $"{CenterX:F2}, {CenterY:F2}";

        /// <summary>
        /// 피처의 크기 정보 문자열
        /// </summary>
        public string SizeString
        {
            get
            {
                return GeometryType.ToLower() switch
                {
                    "point" => "점",
                    "linestring" or "multilinestring" => Length.HasValue ? $"길이: {Length:F2}m" : "선형",
                    "polygon" or "multipolygon" => Area.HasValue ? $"면적: {Area:F2}㎡" : "면형",
                    _ => "알 수 없음"
                };
            }
        }

        /// <summary>
        /// 피처의 표시명
        /// </summary>
        public string DisplayName => $"{SourceClass}#{SourceOID}";

        /// <summary>
        /// 특정 속성 값 가져오기
        /// </summary>
        /// <param name="attributeName">속성명</param>
        /// <returns>속성 값</returns>
        public object? GetAttribute(string attributeName)
        {
            return Attributes.TryGetValue(attributeName, out var value) ? value : null;
        }

        /// <summary>
        /// 특정 속성 값 가져오기 (타입 지정)
        /// </summary>
        /// <typeparam name="T">반환 타입</typeparam>
        /// <param name="attributeName">속성명</param>
        /// <param name="defaultValue">기본값</param>
        /// <returns>속성 값</returns>
        public T GetAttribute<T>(string attributeName, T defaultValue = default!)
        {
            try
            {
                var value = GetAttribute(attributeName);
                if (value == null)
                    return defaultValue;

                if (value is T directValue)
                    return directValue;

                return (T)Convert.ChangeType(value, typeof(T));
            }
            catch
            {
                return defaultValue;
            }
        }

        /// <summary>
        /// 속성 값 설정
        /// </summary>
        /// <param name="attributeName">속성명</param>
        /// <param name="value">속성 값</param>
        public void SetAttribute(string attributeName, object? value)
        {
            Attributes[attributeName] = value;
        }

        /// <summary>
        /// 메타데이터 값 가져오기
        /// </summary>
        /// <param name="key">키</param>
        /// <returns>메타데이터 값</returns>
        public object? GetMetadata(string key)
        {
            return Metadata.TryGetValue(key, out var value) ? value : null;
        }

        /// <summary>
        /// 메타데이터 값 설정
        /// </summary>
        /// <param name="key">키</param>
        /// <param name="value">값</param>
        public void SetMetadata(string key, object? value)
        {
            Metadata[key] = value;
        }

        /// <summary>
        /// 바운딩 박스가 유효한지 확인
        /// </summary>
        /// <returns>유효 여부</returns>
        public bool HasValidBounds()
        {
            return BoundingBox.Length == 4 && 
                   BoundingBox[0] < BoundingBox[2] && // MinX < MaxX
                   BoundingBox[1] < BoundingBox[3];   // MinY < MaxY
        }

        /// <summary>
        /// 바운딩 박스 설정
        /// </summary>
        /// <param name="minX">최소 X</param>
        /// <param name="minY">최소 Y</param>
        /// <param name="maxX">최대 X</param>
        /// <param name="maxY">최대 Y</param>
        public void SetBounds(double minX, double minY, double maxX, double maxY)
        {
            BoundingBox = new[] { minX, minY, maxX, maxY };
            
            // 중심점 계산
            CenterX = (minX + maxX) / 2.0;
            CenterY = (minY + maxY) / 2.0;
        }

        /// <summary>
        /// 바운딩 박스의 폭
        /// </summary>
        public double Width => HasValidBounds() ? BoundingBox[2] - BoundingBox[0] : 0;

        /// <summary>
        /// 바운딩 박스의 높이
        /// </summary>
        public double Height => HasValidBounds() ? BoundingBox[3] - BoundingBox[1] : 0;

        /// <summary>
        /// 바운딩 박스의 대각선 길이
        /// </summary>
        public double DiagonalLength => HasValidBounds() ? Math.Sqrt(Width * Width + Height * Height) : 0;

        /// <summary>
        /// 피처 정보 요약
        /// </summary>
        /// <returns>요약 문자열</returns>
        public string GetSummary()
        {
            var summary = $"피처: {DisplayName}\n";
            summary += $"타입: {GeometryType}\n";
            summary += $"위치: {LocationString}\n";
            summary += $"크기: {SizeString}\n";
            
            if (RelatedErrorCount > 0)
            {
                summary += $"연결된 오류: {RelatedErrorCount}개\n";
            }

            if (CreatedDate.HasValue)
            {
                summary += $"생성일: {CreatedDate:yyyy-MM-dd HH:mm:ss}\n";
            }

            if (ModifiedDate.HasValue)
            {
                summary += $"수정일: {ModifiedDate:yyyy-MM-dd HH:mm:ss}\n";
            }

            return summary.TrimEnd('\n');
        }

        /// <summary>
        /// 문자열 표현
        /// </summary>
        /// <returns>피처 정보 문자열</returns>
        public override string ToString()
        {
            return $"SourceFeatureInfo[{DisplayName}]: {GeometryType} at ({CenterX:F2}, {CenterY:F2})";
        }

        /// <summary>
        /// 두 SourceFeatureInfo가 같은 피처를 나타내는지 확인
        /// </summary>
        /// <param name="obj">비교할 객체</param>
        /// <returns>같은 피처 여부</returns>
        public override bool Equals(object? obj)
        {
            if (obj is not SourceFeatureInfo other)
                return false;

            return SourceClass.Equals(other.SourceClass, StringComparison.OrdinalIgnoreCase) &&
                   SourceOID == other.SourceOID;
        }

        /// <summary>
        /// 해시 코드 생성
        /// </summary>
        /// <returns>해시 코드</returns>
        public override int GetHashCode()
        {
            return HashCode.Combine(SourceClass.ToLowerInvariant(), SourceOID);
        }
    }
}