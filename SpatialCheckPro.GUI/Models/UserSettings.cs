#nullable enable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace SpatialCheckPro.GUI.Models
{
    /// <summary>
    /// 사용자 설정 모델
    /// Requirements: 사용성 개선 - 클릭 허용 거리, 심볼 크기 및 색상 커스터마이징, 설정 저장 및 복원
    /// </summary>
    public class UserSettings : INotifyPropertyChanged
    {
        #region 지도 상호작용 설정

        private double _clickTolerance = 50.0;
        /// <summary>
        /// 클릭 허용 거리 (미터)
        /// </summary>
        public double ClickTolerance
        {
            get => _clickTolerance;
            set
            {
                if (Math.Abs(_clickTolerance - value) > 0.001)
                {
                    _clickTolerance = Math.Max(1.0, Math.Min(500.0, value)); // 1-500m 범위 제한
                    OnPropertyChanged(nameof(ClickTolerance));
                }
            }
        }

        private double _highlightDuration = 3.0;
        /// <summary>
        /// 하이라이트 지속 시간 (초)
        /// </summary>
        public double HighlightDuration
        {
            get => _highlightDuration;
            set
            {
                if (Math.Abs(_highlightDuration - value) > 0.001)
                {
                    _highlightDuration = Math.Max(0.5, Math.Min(10.0, value)); // 0.5-10초 범위 제한
                    OnPropertyChanged(nameof(HighlightDuration));
                }
            }
        }

        private double _animationDuration = 0.8;
        /// <summary>
        /// 애니메이션 지속 시간 (초)
        /// </summary>
        public double AnimationDuration
        {
            get => _animationDuration;
            set
            {
                if (Math.Abs(_animationDuration - value) > 0.001)
                {
                    _animationDuration = Math.Max(0.1, Math.Min(3.0, value)); // 0.1-3초 범위 제한
                    OnPropertyChanged(nameof(AnimationDuration));
                }
            }
        }

        #endregion

        #region 심볼 및 색상 설정

        private double _symbolSize = 12.0;
        /// <summary>
        /// 기본 심볼 크기 (픽셀)
        /// </summary>
        public double SymbolSize
        {
            get => _symbolSize;
            set
            {
                if (Math.Abs(_symbolSize - value) > 0.001)
                {
                    _symbolSize = Math.Max(4.0, Math.Min(50.0, value)); // 4-50px 범위 제한
                    OnPropertyChanged(nameof(SymbolSize));
                }
            }
        }

        private double _selectedSymbolSize = 18.0;
        /// <summary>
        /// 선택된 심볼 크기 (픽셀)
        /// </summary>
        public double SelectedSymbolSize
        {
            get => _selectedSymbolSize;
            set
            {
                if (Math.Abs(_selectedSymbolSize - value) > 0.001)
                {
                    _selectedSymbolSize = Math.Max(6.0, Math.Min(75.0, value)); // 6-75px 범위 제한
                    OnPropertyChanged(nameof(SelectedSymbolSize));
                }
            }
        }

        private double _symbolTransparency = 0.8;
        /// <summary>
        /// 심볼 투명도 (0.0-1.0)
        /// </summary>
        public double SymbolTransparency
        {
            get => _symbolTransparency;
            set
            {
                if (Math.Abs(_symbolTransparency - value) > 0.001)
                {
                    _symbolTransparency = Math.Max(0.1, Math.Min(1.0, value)); // 0.1-1.0 범위 제한
                    OnPropertyChanged(nameof(SymbolTransparency));
                }
            }
        }

        /// <summary>
        /// 심각도별 색상 설정
        /// </summary>
        public Dictionary<string, string> SeverityColors { get; set; } = new()
        {
            ["CRIT"] = "#F44336",    // 빨강
            ["MAJOR"] = "#FF9800",   // 주황
            ["MINOR"] = "#FFC107",   // 노랑
            ["INFO"] = "#2196F3"     // 파랑
        };

        /// <summary>
        /// 상태별 색상 설정
        /// </summary>
        public Dictionary<string, string> StatusColors { get; set; } = new()
        {
            ["OPEN"] = "#F44336",      // 빨강
            ["FIXED"] = "#4CAF50",     // 초록
            ["IGNORED"] = "#9E9E9E",   // 회색
            ["FALSE_POS"] = "#FF9800"  // 주황
        };

        #endregion

        #region 클러스터링 설정

        private bool _enableClustering = true;
        /// <summary>
        /// 클러스터링 활성화 여부
        /// </summary>
        public bool EnableClustering
        {
            get => _enableClustering;
            set
            {
                if (_enableClustering != value)
                {
                    _enableClustering = value;
                    OnPropertyChanged(nameof(EnableClustering));
                }
            }
        }

        private double _clusterTolerance = 100.0;
        /// <summary>
        /// 클러스터링 허용 거리 (미터)
        /// </summary>
        public double ClusterTolerance
        {
            get => _clusterTolerance;
            set
            {
                if (Math.Abs(_clusterTolerance - value) > 0.001)
                {
                    _clusterTolerance = Math.Max(10.0, Math.Min(1000.0, value)); // 10-1000m 범위 제한
                    OnPropertyChanged(nameof(ClusterTolerance));
                }
            }
        }

        private int _minClusterSize = 3;
        /// <summary>
        /// 최소 클러스터 크기
        /// </summary>
        public int MinClusterSize
        {
            get => _minClusterSize;
            set
            {
                if (_minClusterSize != value)
                {
                    _minClusterSize = Math.Max(2, Math.Min(20, value)); // 2-20개 범위 제한
                    OnPropertyChanged(nameof(MinClusterSize));
                }
            }
        }

        #endregion

        #region 성능 설정

        private int _maxRenderCount = 10000;
        /// <summary>
        /// 최대 렌더링 개수
        /// </summary>
        public int MaxRenderCount
        {
            get => _maxRenderCount;
            set
            {
                if (_maxRenderCount != value)
                {
                    _maxRenderCount = Math.Max(100, Math.Min(50000, value)); // 100-50000개 범위 제한
                    OnPropertyChanged(nameof(MaxRenderCount));
                }
            }
        }

        private bool _enableProgressiveRendering = true;
        /// <summary>
        /// 프로그레시브 렌더링 활성화 여부
        /// </summary>
        public bool EnableProgressiveRendering
        {
            get => _enableProgressiveRendering;
            set
            {
                if (_enableProgressiveRendering != value)
                {
                    _enableProgressiveRendering = value;
                    OnPropertyChanged(nameof(EnableProgressiveRendering));
                }
            }
        }

        #endregion

        #region UI 설정

        private bool _showTooltips = true;
        /// <summary>
        /// 툴팁 표시 여부
        /// </summary>
        public bool ShowTooltips
        {
            get => _showTooltips;
            set
            {
                if (_showTooltips != value)
                {
                    _showTooltips = value;
                    OnPropertyChanged(nameof(ShowTooltips));
                }
            }
        }

        private bool _enableKeyboardShortcuts = true;
        /// <summary>
        /// 키보드 단축키 활성화 여부
        /// </summary>
        public bool EnableKeyboardShortcuts
        {
            get => _enableKeyboardShortcuts;
            set
            {
                if (_enableKeyboardShortcuts != value)
                {
                    _enableKeyboardShortcuts = value;
                    OnPropertyChanged(nameof(EnableKeyboardShortcuts));
                }
            }
        }

        private string _language = "ko-KR";
        /// <summary>
        /// 언어 설정
        /// </summary>
        public string Language
        {
            get => _language;
            set
            {
                if (_language != value)
                {
                    _language = value ?? "ko-KR";
                    OnPropertyChanged(nameof(Language));
                }
            }
        }

        #endregion

        #region 설정 파일 관리

        /// <summary>
        /// 설정 파일 경로
        /// </summary>
        [JsonIgnore]
        public static string SettingsFilePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SpatialCheckPro",
            "UserSettings.json");

        /// <summary>
        /// 기본 설정으로 초기화
        /// </summary>
        public void SetDefaults()
        {
            ClickTolerance = 50.0;
            HighlightDuration = 3.0;
            AnimationDuration = 0.8;
            SymbolSize = 12.0;
            SelectedSymbolSize = 18.0;
            SymbolTransparency = 0.8;
            EnableClustering = true;
            ClusterTolerance = 100.0;
            MinClusterSize = 3;
            MaxRenderCount = 10000;
            EnableProgressiveRendering = true;
            ShowTooltips = true;
            EnableKeyboardShortcuts = true;
            Language = "ko-KR";

            // 기본 색상 설정
            SeverityColors.Clear();
            SeverityColors["CRIT"] = "#F44336";
            SeverityColors["MAJOR"] = "#FF9800";
            SeverityColors["MINOR"] = "#FFC107";
            SeverityColors["INFO"] = "#2196F3";

            StatusColors.Clear();
            StatusColors["OPEN"] = "#F44336";
            StatusColors["FIXED"] = "#4CAF50";
            StatusColors["IGNORED"] = "#9E9E9E";
            StatusColors["FALSE_POS"] = "#FF9800";
        }

        /// <summary>
        /// 설정을 파일에서 로드
        /// </summary>
        public static UserSettings Load(ILogger? logger = null)
        {
            try
            {
                if (File.Exists(SettingsFilePath))
                {
                    var json = File.ReadAllText(SettingsFilePath);
                    var settings = JsonSerializer.Deserialize<UserSettings>(json, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true,
                        WriteIndented = true
                    });

                    if (settings != null)
                    {
                        logger?.LogInformation("사용자 설정 로드 완료: {SettingsPath}", SettingsFilePath);
                        return settings;
                    }
                }
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "사용자 설정 로드 실패: {SettingsPath}", SettingsFilePath);
            }

            // 로드 실패 시 기본 설정 반환
            var defaultSettings = new UserSettings();
            defaultSettings.SetDefaults();
            logger?.LogInformation("기본 사용자 설정 사용");
            return defaultSettings;
        }

        /// <summary>
        /// 설정을 파일에 저장
        /// </summary>
        public bool Save(ILogger? logger = null)
        {
            try
            {
                // 디렉토리 생성
                var directory = Path.GetDirectoryName(SettingsFilePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                // JSON으로 직렬화하여 저장
                var json = JsonSerializer.Serialize(this, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    WriteIndented = true
                });

                File.WriteAllText(SettingsFilePath, json);
                logger?.LogInformation("사용자 설정 저장 완료: {SettingsPath}", SettingsFilePath);
                return true;
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "사용자 설정 저장 실패: {SettingsPath}", SettingsFilePath);
                return false;
            }
        }

        /// <summary>
        /// 설정 파일 삭제 (기본값으로 재설정)
        /// </summary>
        public static bool Reset(ILogger? logger = null)
        {
            try
            {
                if (File.Exists(SettingsFilePath))
                {
                    File.Delete(SettingsFilePath);
                    logger?.LogInformation("사용자 설정 파일 삭제 완료: {SettingsPath}", SettingsFilePath);
                }
                return true;
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "사용자 설정 파일 삭제 실패: {SettingsPath}", SettingsFilePath);
                return false;
            }
        }

        #endregion

        #region INotifyPropertyChanged 구현

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        #endregion

        #region 유틸리티 메서드

        /// <summary>
        /// 설정 유효성 검사
        /// </summary>
        public bool Validate()
        {
            try
            {
                // 범위 검사
                if (ClickTolerance < 1.0 || ClickTolerance > 500.0) return false;
                if (HighlightDuration < 0.5 || HighlightDuration > 10.0) return false;
                if (AnimationDuration < 0.1 || AnimationDuration > 3.0) return false;
                if (SymbolSize < 4.0 || SymbolSize > 50.0) return false;
                if (SelectedSymbolSize < 6.0 || SelectedSymbolSize > 75.0) return false;
                if (SymbolTransparency < 0.1 || SymbolTransparency > 1.0) return false;
                if (ClusterTolerance < 10.0 || ClusterTolerance > 1000.0) return false;
                if (MinClusterSize < 2 || MinClusterSize > 20) return false;
                if (MaxRenderCount < 100 || MaxRenderCount > 50000) return false;

                // 필수 색상 설정 확인
                var requiredSeverities = new[] { "CRIT", "MAJOR", "MINOR", "INFO" };
                var requiredStatuses = new[] { "OPEN", "FIXED", "IGNORED", "FALSE_POS" };

                foreach (var severity in requiredSeverities)
                {
                    if (!SeverityColors.ContainsKey(severity) || string.IsNullOrEmpty(SeverityColors[severity]))
                        return false;
                }

                foreach (var status in requiredStatuses)
                {
                    if (!StatusColors.ContainsKey(status) || string.IsNullOrEmpty(StatusColors[status]))
                        return false;
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 다른 설정과 비교
        /// </summary>
        public bool Equals(UserSettings? other)
        {
            if (other == null) return false;

            return Math.Abs(ClickTolerance - other.ClickTolerance) < 0.001 &&
                   Math.Abs(HighlightDuration - other.HighlightDuration) < 0.001 &&
                   Math.Abs(AnimationDuration - other.AnimationDuration) < 0.001 &&
                   Math.Abs(SymbolSize - other.SymbolSize) < 0.001 &&
                   Math.Abs(SelectedSymbolSize - other.SelectedSymbolSize) < 0.001 &&
                   Math.Abs(SymbolTransparency - other.SymbolTransparency) < 0.001 &&
                   EnableClustering == other.EnableClustering &&
                   Math.Abs(ClusterTolerance - other.ClusterTolerance) < 0.001 &&
                   MinClusterSize == other.MinClusterSize &&
                   MaxRenderCount == other.MaxRenderCount &&
                   EnableProgressiveRendering == other.EnableProgressiveRendering &&
                   ShowTooltips == other.ShowTooltips &&
                   EnableKeyboardShortcuts == other.EnableKeyboardShortcuts &&
                   Language == other.Language;
        }

        /// <summary>
        /// 설정 복사
        /// </summary>
        public UserSettings Clone()
        {
            var json = JsonSerializer.Serialize(this);
            return JsonSerializer.Deserialize<UserSettings>(json) ?? new UserSettings();
        }

        #endregion
    }
}