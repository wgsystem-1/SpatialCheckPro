using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SpatialCheckPro.GUI.ViewModels
{
    public class ValidationSettingsViewModel : INotifyPropertyChanged
    {
        private bool _enableHighPerformanceMode;
        private bool _forceStreamingMode;
        private int _customBatchSize = 1000;
        private int _maxMemoryUsageMB = 512;
        private bool _enablePrefetching;
        private bool _enableParallelStreaming;

        public bool EnableHighPerformanceMode
        {
            get => _enableHighPerformanceMode;
            set
            {
                _enableHighPerformanceMode = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// 스트리밍 모드 강제 사용 (파일 크기와 무관하게)
        /// </summary>
        public bool ForceStreamingMode
        {
            get => _forceStreamingMode;
            set
            {
                _forceStreamingMode = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// 사용자 지정 배치 크기
        /// </summary>
        public int CustomBatchSize
        {
            get => _customBatchSize;
            set
            {
                if (value > 0 && value <= 10000)
                {
                    _customBatchSize = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// 최대 메모리 사용량 (MB)
        /// </summary>
        public int MaxMemoryUsageMB
        {
            get => _maxMemoryUsageMB;
            set
            {
                if (value >= 128 && value <= 4096)
                {
                    _maxMemoryUsageMB = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// 프리페칭 활성화
        /// </summary>
        public bool EnablePrefetching
        {
            get => _enablePrefetching;
            set
            {
                _enablePrefetching = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// 병렬 스트리밍 활성화
        /// </summary>
        public bool EnableParallelStreaming
        {
            get => _enableParallelStreaming;
            set
            {
                _enableParallelStreaming = value;
                OnPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
