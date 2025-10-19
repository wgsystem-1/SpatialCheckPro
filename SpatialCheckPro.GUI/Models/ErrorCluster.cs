using System;
using System.Collections.Generic;
using System.Linq;

namespace SpatialCheckPro.GUI.Models
{
    /// <summary>
    /// 오류 피처들의 클러스터를 나타내는 모델 클래스
    /// </summary>
    public class ErrorCluster
    {
        /// <summary>
        /// 클러스터의 고유 식별자
        /// </summary>
        public string Id { get; set; } = Guid.NewGuid().ToString();

        /// <summary>
        /// 클러스터 중심점 X 좌표
        /// </summary>
        public double CenterX { get; set; }

        /// <summary>
        /// 클러스터 중심점 Y 좌표
        /// </summary>
        public double CenterY { get; set; }

        /// <summary>
        /// 클러스터에 포함된 오류 피처들
        /// </summary>
        public List<ErrorFeature> Errors { get; set; } = new List<ErrorFeature>();

        /// <summary>
        /// 클러스터에 포함된 오류 개수
        /// </summary>
        public int Count => Errors.Count;

        /// <summary>
        /// 클러스터에 포함된 오류 개수 (별칭)
        /// </summary>
        public int ErrorCount => Errors.Count;

        /// <summary>
        /// 클러스터에 포함된 오류 피처들 (별칭)
        /// </summary>
        public List<ErrorFeature> ErrorFeatures => Errors;

        /// <summary>
        /// 클러스터의 바운딩 박스
        /// </summary>
        public BoundingBox Bounds
        {
            get
            {
                if (!Errors.Any())
                    return new BoundingBox(CenterX - 10, CenterY - 10, CenterX + 10, CenterY + 10);

                var minX = Errors.Min(e => e.X);
                var minY = Errors.Min(e => e.Y);
                var maxX = Errors.Max(e => e.X);
                var maxY = Errors.Max(e => e.Y);

                return new BoundingBox(minX, minY, maxX, maxY);
            }
        }

        /// <summary>
        /// 클러스터의 지배적인 심각도 (가장 높은 심각도)
        /// </summary>
        public string DominantSeverity
        {
            get
            {
                if (!Errors.Any()) return "INFO";

                var severityPriority = new Dictionary<string, int>
                {
                    { "CRIT", 4 },
                    { "MAJOR", 3 },
                    { "MINOR", 2 },
                    { "INFO", 1 }
                };

                return Errors
                    .Select(e => e.Severity)
                    .OrderByDescending(s => severityPriority.GetValueOrDefault(s, 0))
                    .FirstOrDefault() ?? "INFO";
            }
        }

        /// <summary>
        /// 클러스터의 바운딩 박스 반지름 (미터)
        /// </summary>
        public double Radius { get; set; } = 50.0;

        /// <summary>
        /// 클러스터가 확장된 상태인지 여부
        /// </summary>
        public bool IsExpanded { get; set; } = false;

        /// <summary>
        /// 클러스터 생성 시간
        /// </summary>
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        /// <summary>
        /// 오류 피처들로부터 클러스터 생성
        /// </summary>
        /// <param name="errors">클러스터링할 오류 피처들</param>
        /// <param name="tolerance">클러스터링 허용 거리 (미터)</param>
        /// <returns>생성된 클러스터 목록</returns>
        public static List<ErrorCluster> CreateClusters(List<ErrorFeature> errors, double tolerance = 100.0)
        {
            var clusters = new List<ErrorCluster>();
            var processedErrors = new HashSet<string>();

            foreach (var error in errors)
            {
                if (processedErrors.Contains(error.Id))
                    continue;

                var cluster = new ErrorCluster();
                var clusterErrors = new List<ErrorFeature> { error };
                processedErrors.Add(error.Id);

                // 허용 거리 내의 다른 오류들 찾기
                foreach (var otherError in errors)
                {
                    if (processedErrors.Contains(otherError.Id))
                        continue;

                    var distance = error.DistanceTo(otherError);
                    if (distance <= tolerance)
                    {
                        clusterErrors.Add(otherError);
                        processedErrors.Add(otherError.Id);
                    }
                }

                // 클러스터 중심점 계산
                cluster.CenterX = clusterErrors.Average(e => e.X);
                cluster.CenterY = clusterErrors.Average(e => e.Y);
                cluster.Errors = clusterErrors;
                cluster.Radius = tolerance;

                clusters.Add(cluster);
            }

            return clusters;
        }

        /// <summary>
        /// 클러스터의 바운딩 박스 반환
        /// </summary>
        /// <returns>바운딩 박스 (MinX, MinY, MaxX, MaxY)</returns>
        public (double MinX, double MinY, double MaxX, double MaxY) GetBounds()
        {
            if (!Errors.Any())
                return (CenterX - Radius, CenterY - Radius, CenterX + Radius, CenterY + Radius);

            var minX = Errors.Min(e => e.X);
            var minY = Errors.Min(e => e.Y);
            var maxX = Errors.Max(e => e.X);
            var maxY = Errors.Max(e => e.Y);

            return (minX, minY, maxX, maxY);
        }

        /// <summary>
        /// 클러스터의 표시 텍스트 반환
        /// </summary>
        /// <returns>표시 텍스트</returns>
        public string GetDisplayText()
        {
            if (Count == 1)
                return Errors.First().GetDisplayText();

            var severityCount = Errors.GroupBy(e => e.Severity)
                .ToDictionary(g => g.Key, g => g.Count());

            var severityText = string.Join(", ", severityCount.Select(kv => $"{kv.Key}:{kv.Value}"));
            return $"클러스터 ({Count}개): {severityText}";
        }

        /// <summary>
        /// 지정된 점이 클러스터 영역 내에 있는지 확인
        /// </summary>
        /// <param name="x">X 좌표</param>
        /// <param name="y">Y 좌표</param>
        /// <returns>포함 여부</returns>
        public bool ContainsPoint(double x, double y)
        {
            var distance = Math.Sqrt(Math.Pow(CenterX - x, 2) + Math.Pow(CenterY - y, 2));
            return distance <= Radius;
        }
    }
}