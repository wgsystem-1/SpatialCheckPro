using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SpatialCheckPro.Models.Config;

namespace SpatialCheckPro.GUI.Services
{
    /// <summary>
    /// 설정 모델들을 생성하고 관리하는 팩토리 클래스
    /// </summary>
    public interface IConfigurationFactory
    {
        /// <summary>
        /// 성능 설정을 생성합니다
        /// </summary>
        /// <returns>성능 설정 인스턴스</returns>
        PerformanceSettings CreatePerformanceSettings();
        
        /// <summary>
        /// 기본 설정으로 성능 설정을 생성합니다
        /// </summary>
        /// <returns>기본 성능 설정 인스턴스</returns>
        PerformanceSettings CreateDefaultPerformanceSettings();
    }

    /// <summary>
    /// 설정 모델 팩토리 구현체
    /// </summary>
    public class ConfigurationFactory : IConfigurationFactory
    {
        private readonly ILogger<ConfigurationFactory> _logger;

        public ConfigurationFactory(ILogger<ConfigurationFactory> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// 성능 설정을 생성합니다
        /// </summary>
        public PerformanceSettings CreatePerformanceSettings()
        {
            try
            {
                var settings = new PerformanceSettings();
                _logger.LogDebug("성능 설정 인스턴스 생성 완료");
                return settings;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "성능 설정 생성 중 오류 발생");
                throw;
            }
        }

        /// <summary>
        /// 기본 설정으로 성능 설정을 생성합니다
        /// </summary>
        public PerformanceSettings CreateDefaultPerformanceSettings()
        {
            try
            {
                var settings = new PerformanceSettings
                {
                    // 기본값들은 이미 클래스에서 설정되어 있음
                    // 필요시 여기서 추가 설정 가능
                };
                
                _logger.LogDebug("기본 성능 설정 인스턴스 생성 완료");
                return settings;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "기본 성능 설정 생성 중 오류 발생");
                throw;
            }
        }
    }

    /// <summary>
    /// 설정 모델 팩토리 확장 메서드
    /// </summary>
    public static class ConfigurationFactoryExtensions
    {
        /// <summary>
        /// 설정 팩토리 서비스를 등록합니다
        /// </summary>
        /// <param name="services">서비스 컬렉션</param>
        /// <returns>서비스 컬렉션</returns>
        public static IServiceCollection AddConfigurationFactory(this IServiceCollection services)
        {
            services.AddSingleton<IConfigurationFactory, ConfigurationFactory>();
            return services;
        }
    }
}
