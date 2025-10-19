using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using SpatialCheckPro.GUI.Models;

namespace SpatialCheckPro.GUI.Services
{
    /// <summary>
    /// 오류 클러스터링 서비스
    /// </summary>
    public class ErrorClusteringService
    {
        private readonly ILogger<ErrorClusteringService> _logger;

        public ErrorClusteringService(ILogger<ErrorClusteringService> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// 거리 기반 오류 클러스터링을 수행합니다
        /// </summary>
        /// <param name="errors">클러스터링할 오류 목록</param>
        /// <param name="clusterDistance">클러스터링 거리 (미터)</param>
        /// <returns>클러스터 목록</returns>
        public List<ErrorCluster> ClusterErrors(List<ErrorFeature> errors, double clusterDistance = 100.0)
        {
            try
            {
                _logger.LogInformation("오류 클러스터링 시작: {Count}개 오류, 거리: {Distance}m", 
                    errors.Count, clusterDistance);
                
                var clusters = new List<ErrorCluster>();
                var processed = new HashSet<string>();

                foreach (var error in errors)
                {
                    if (processed.Contains(error.Id))
                        continue;

                    var cluster = new ErrorCluster
                    {
                        Id = Guid.NewGuid().ToString(),
                        CenterX = error.QcError.X,
                        CenterY = error.QcError.Y,
                        Errors = new List<ErrorFeature> { error }
                    };

                    // 거리 내의 다른 오류들을 클러스터에 추가
                    foreach (var otherError in errors)
                    {
                        if (processed.Contains(otherError.Id) || otherError.Id == error.Id)
                            continue;

                        var distance = CalculateDistance(error.QcError.X, error.QcError.Y, 
                                                       otherError.QcError.X, otherError.QcError.Y);
                        
                        if (distance <= clusterDistance)
                        {
                            cluster.Errors.Add(otherError);
                            processed.Add(otherError.Id);
                        }
                    }

                    // 클러스터 중심점 재계산
                    RecalculateClusterCenter(cluster);
                    
                    processed.Add(error.Id);
                    clusters.Add(cluster);
                }

                _logger.LogInformation("오류 클러스터링 완료: {ClusterCount}개 클러스터 생성", clusters.Count);
                return clusters;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "오류 클러스터링 실패");
                return new List<ErrorCluster>();
            }
        }

        /// <summary>
        /// 클러스터 중심점을 재계산합니다
        /// </summary>
        private void RecalculateClusterCenter(ErrorCluster cluster)
        {
            if (cluster.Errors.Count > 0)
            {
                cluster.CenterX = cluster.Errors.Average(e => e.QcError.X);
                cluster.CenterY = cluster.Errors.Average(e => e.QcError.Y);
            }
        }

        /// <summary>
        /// 두 점 사이의 유클리드 거리를 계산합니다
        /// </summary>
        private double CalculateDistance(double x1, double y1, double x2, double y2)
        {
            var dx = x2 - x1;
            var dy = y2 - y1;
            return Math.Sqrt(dx * dx + dy * dy);
        }
    }

    /// <summary>
    /// 오류 클러스터 클래스
    /// </summary>
    public class ErrorCluster
    {
        /// <summary>
        /// 클러스터 고유 ID
        /// </summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// 클러스터 중심점 X 좌표
        /// </summary>
        public double CenterX { get; set; }

        /// <summary>
        /// 클러스터 중심점 Y 좌표
        /// </summary>
        public double CenterY { get; set; }

        /// <summary>
        /// 클러스터에 포함된 오류들
        /// </summary>
        public List<ErrorFeature> Errors { get; set; } = new List<ErrorFeature>();

        /// <summary>
        /// 클러스터 크기 (포함된 오류 개수)
        /// </summary>
        public int Size => Errors.Count;

        /// <summary>
        /// 클러스터의 주요 오류 타입
        /// </summary>
        public string PrimaryErrorType => Errors.GroupBy(e => e.QcError.ErrType)
                                                .OrderByDescending(g => g.Count())
                                                .FirstOrDefault()?.Key ?? "UNKNOWN";

        /// <summary>
        /// 클러스터의 최고 심각도
        /// </summary>
        public string HighestSeverity
        {
            get
            {
                var severityOrder = new Dictionary<string, int>
                {
                    ["CRIT"] = 4,
                    ["MAJOR"] = 3,
                    ["MINOR"] = 2,
                    ["INFO"] = 1
                };

                return Errors.Select(e => e.QcError.Severity)
                           .OrderByDescending(s => severityOrder.GetValueOrDefault(s, 0))
                           .FirstOrDefault() ?? "INFO";
            }
        }
    }
}