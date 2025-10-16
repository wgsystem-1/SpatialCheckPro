#nullable enable
using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using SpatialCheckPro.Models.Enums;

namespace SpatialCheckPro.GUI.ViewModels
{
    /// <summary>
    /// 검수 알림 정보를 표현하는 뷰모델
    /// </summary>
    public class AlertViewModel : INotifyPropertyChanged
    {
        private ErrorSeverity _severity;
        private string _message = string.Empty;
        private string? _detail;
        private DateTimeOffset _timestamp;
        private StageStatus _relatedStageStatus;

        /// <summary>
        /// 알림과 연관된 단계 식별자
        /// </summary>
        public string StageId { get; }

        /// <summary>
        /// 알림과 연관된 단계 번호
        /// </summary>
        public int StageNumber { get; }

        /// <summary>
        /// 알림과 연관된 단계명
        /// </summary>
        public string StageName { get; }

        /// <summary>
        /// 알림 심각도
        /// </summary>
        public ErrorSeverity Severity
        {
            get => _severity;
            private set
            {
                if (_severity == value)
                {
                    return;
                }
                _severity = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// 알림 메시지
        /// </summary>
        public string Message
        {
            get => _message;
            private set
            {
                if (_message == value)
                {
                    return;
                }
                _message = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// 추가 상세 설명
        /// </summary>
        public string? Detail
        {
            get => _detail;
            private set
            {
                if (_detail == value)
                {
                    return;
                }
                _detail = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// 알림 생성 또는 갱신 시각
        /// </summary>
        public DateTimeOffset Timestamp
        {
            get => _timestamp;
            private set
            {
                if (_timestamp == value)
                {
                    return;
                }
                _timestamp = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// 알림이 반영하는 단계 상태
        /// </summary>
        public StageStatus RelatedStageStatus
        {
            get => _relatedStageStatus;
            private set
            {
                if (_relatedStageStatus == value)
                {
                    return;
                }
                _relatedStageStatus = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// 알림 고유 식별자 (단계 기준)
        /// </summary>
        public string AlertKey => StageId;

        /// <summary>
        /// 생성자
        /// </summary>
        public AlertViewModel(string stageId, int stageNumber, string stageName)
        {
            StageId = stageId;
            StageNumber = stageNumber;
            StageName = stageName;
            _timestamp = DateTimeOffset.Now;
            _severity = ErrorSeverity.Info;
            _relatedStageStatus = StageStatus.NotStarted;
        }

        /// <summary>
        /// 알림 정보를 갱신합니다
        /// </summary>
        /// <param name="severity">새 심각도</param>
        /// <param name="message">요약 메시지</param>
        /// <param name="detail">세부 설명</param>
        /// <param name="stageStatus">관련 단계 상태</param>
        public void Update(ErrorSeverity severity, string message, string? detail, StageStatus stageStatus)
        {
            Severity = severity;
            Message = message;
            Detail = detail;
            RelatedStageStatus = stageStatus;
            Timestamp = DateTimeOffset.Now;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}


