using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using SpatialCheckPro.GUI.Models;

namespace SpatialCheckPro.GUI.Services
{
    /// <summary>
    /// 오류 그룹화 서비스
    /// </summary>
    public class ErrorGroupService
    {
        private readonly ILogger<ErrorGroupService> _logger;

        public ErrorGroupService(ILogger<ErrorGroupService> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// 오류 타입별로 그룹화합니다
        /// </summary>
        public Dictionary<string, List<ErrorFeature>> GroupByErrorType(List<ErrorFeature> errors)
        {
            try
            {
                var groups = errors.GroupBy(e => e.QcError.ErrType)
                                  .ToDictionary(g => g.Key, g => g.ToList());
                
                _logger.LogDebug("오류 타입별 그룹화 완료: {GroupCount}개 그룹", groups.Count);
                return groups;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "오류 타입별 그룹화 실패");
                return new Dictionary<string, List<ErrorFeature>>();
            }
        }

        /// <summary>
        /// 심각도별로 그룹화합니다
        /// </summary>
        public Dictionary<string, List<ErrorFeature>> GroupBySeverity(List<ErrorFeature> errors)
        {
            try
            {
                var groups = errors.GroupBy(e => e.QcError.Severity)
                                  .ToDictionary(g => g.Key, g => g.ToList());
                
                _logger.LogDebug("심각도별 그룹화 완료: {GroupCount}개 그룹", groups.Count);
                return groups;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "심각도별 그룹화 실패");
                return new Dictionary<string, List<ErrorFeature>>();
            }
        }

        /// <summary>
        /// 상태별로 그룹화합니다
        /// </summary>
        public Dictionary<string, List<ErrorFeature>> GroupByStatus(List<ErrorFeature> errors)
        {
            try
            {
                var groups = errors.GroupBy(e => e.QcError.Status)
                                  .ToDictionary(g => g.Key, g => g.ToList());
                
                _logger.LogDebug("상태별 그룹화 완료: {GroupCount}개 그룹", groups.Count);
                return groups;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "상태별 그룹화 실패");
                return new Dictionary<string, List<ErrorFeature>>();
            }
        }
    }
}