namespace SpatialCheckPro.Models
{
    /// <summary>
    /// 지오메트리 검수 항목 결과
    /// </summary>
    public class GeometryValidationItem
    {
        /// <summary>
        /// 테이블 ID
        /// </summary>
        public string TableId { get; set; } = string.Empty;

        /// <summary>
        /// 테이블 명칭
        /// </summary>
        public string TableName { get; set; } = string.Empty;

        /// <summary>
        /// 지오메트리 타입
        /// </summary>
        public string GeometryType { get; set; } = string.Empty;

        /// <summary>
        /// 총 객체 수
        /// </summary>
        public int TotalFeatureCount { get; set; }

        /// <summary>
        /// 검수된 객체 수
        /// </summary>
        public int ProcessedFeatureCount { get; set; }

        /// <summary>
        /// 제외된 객체 수
        /// </summary>
        public int SkippedFeatureCount { get; set; }

        /// <summary>
        /// 중복 객체 수
        /// </summary>
        public int DuplicateCount { get; set; }

        /// <summary>
        /// 겹침 객체 수
        /// </summary>
        public int OverlapCount { get; set; }

        /// <summary>
        /// 자체 꼬임 객체 수
        /// </summary>
        public int SelfIntersectionCount { get; set; }

        /// <summary>
        /// 슬리버 객체 수
        /// </summary>
        public int SliverCount { get; set; }

        /// <summary>
        /// 짧은 객체 수
        /// </summary>
        public int ShortObjectCount { get; set; }

        /// <summary>
        /// 작은 면적 객체 수
        /// </summary>
        public int SmallAreaCount { get; set; }

        /// <summary>
        /// 폴리곤 내 폴리곤 객체 수
        /// </summary>
        public int PolygonInPolygonCount { get; set; }

        /// <summary>
        /// 최소 정점 개수 오류
        /// </summary>
        public int MinPointCount { get; set; }

        /// <summary>
        /// 스파이크(돌기) 오류 개수
        /// </summary>
        public int SpikeCount { get; set; }

        /// <summary>
        /// 자기 중첩 오류 개수
        /// </summary>
        public int SelfOverlapCount { get; set; }

        /// <summary>
        /// 언더슛 오류 개수
        /// </summary>
        public int UndershootCount { get; set; }

        /// <summary>
        /// 오버슛 오류 개수
        /// </summary>
        public int OvershootCount { get; set; }

        /// <summary>
        /// 기본 검수 오류 수 (NULL 기하, 빈 기하, 무효한 기하)
        /// </summary>
        public int BasicValidationErrorCount { get; set; }

        /// <summary>
        /// 총 오류 수
        /// </summary>
        public int TotalErrorCount => DuplicateCount + OverlapCount + SelfIntersectionCount + 
                                     SliverCount + ShortObjectCount + SmallAreaCount + PolygonInPolygonCount + BasicValidationErrorCount +
                                     MinPointCount + SpikeCount + SelfOverlapCount + UndershootCount + OvershootCount;

        /// <summary>
        /// 검수 통과 여부
        /// </summary>
        public bool IsValid => TotalErrorCount == 0;

        /// <summary>
        /// 검수 상태
        /// </summary>
        public string ValidationStatus
        {
            get
            {
                if (ProcessedFeatureCount == 0 && SkippedFeatureCount > 0) return "스킵";
                if (IsValid) return "통과";
                return "오류";
            }
        }

        /// <summary>
        /// 상세 오류 메시지 목록
        /// </summary>
        public List<string> ErrorMessages { get; set; } = new List<string>();

        /// <summary>
        /// 경고 메시지 목록
        /// </summary>
        public List<string> WarningMessages { get; set; } = new List<string>();

        /// <summary>
        /// 검수 시작 시간
        /// </summary>
        public DateTime ValidationStartTime { get; set; }

        /// <summary>
        /// 검수 완료 시간
        /// </summary>
        public DateTime ValidationEndTime { get; set; }

        /// <summary>
        /// 검수 소요 시간
        /// </summary>
        public TimeSpan ValidationDuration => ValidationEndTime - ValidationStartTime;

        /// <summary>
        /// 검수 유형 (PDF 보고서용)
        /// </summary>
        public string CheckType { get; set; } = string.Empty;

        /// <summary>
        /// 오류 개수 (PDF 보고서용)
        /// </summary>
        public int ErrorCount => TotalErrorCount;

        /// <summary>
        /// 경고 개수 (PDF 보고서용)
        /// </summary>
        public int WarningCount => WarningMessages.Count;

        /// <summary>
        /// 오류 여부
        /// </summary>
        public bool HasError => TotalErrorCount > 0;

        /// <summary>
        /// 경고 여부
        /// </summary>
        public bool HasWarning => WarningMessages.Count > 0;

        /// <summary>
        /// 오류 메시지
        /// </summary>
        public string ErrorMessage => string.Join(", ", ErrorMessages);

        /// <summary>
        /// 경고 메시지
        /// </summary>
        public string WarningMessage => string.Join(", ", WarningMessages);

        /// <summary>
        /// 기준값 (UI 표시용)
        /// </summary>
        public string Threshold { get; set; } = string.Empty;

        /// <summary>
        /// 검수 상태 (UI 표시용)
        /// </summary>
        public string Status => IsValid ? "통과" : (HasError ? "실패" : "경고");

        /// <summary>
        /// 오류 존재 여부 (UI 표시용)
        /// </summary>
        public bool HasErrors => TotalErrorCount > 0;

        /// <summary>
        /// 총 객체 수 (UI 표시용)
        /// </summary>
        public int TotalObjectCount => TotalFeatureCount;

        /// <summary>
        /// 오류 상세 정보 목록
        /// </summary>
        public List<GeometryErrorDetail>? ErrorDetails { get; set; }

        /// <summary>
        /// 오류 종류별 개수 표시 (UI용)
        /// </summary>
        public string ErrorTypesSummary
        {
            get
            {
                var errorTypes = new List<string>();
                
                if (DuplicateCount > 0) errorTypes.Add($"중복:{DuplicateCount}개");
                if (OverlapCount > 0) errorTypes.Add($"겹침:{OverlapCount}개");
                if (SelfIntersectionCount > 0) errorTypes.Add($"자체꼬임:{SelfIntersectionCount}개");
                if (SliverCount > 0) errorTypes.Add($"슬리버:{SliverCount}개");
                if (ShortObjectCount > 0) errorTypes.Add($"짧은객체:{ShortObjectCount}개");
                if (SmallAreaCount > 0) errorTypes.Add($"작은면적:{SmallAreaCount}개");
                if (PolygonInPolygonCount > 0) errorTypes.Add($"홀 폴리곤 오류:{PolygonInPolygonCount}개");
                if (MinPointCount > 0) errorTypes.Add($"최소정점:{MinPointCount}개");
                if (SpikeCount > 0) errorTypes.Add($"스파이크:{SpikeCount}개");
                if (SelfOverlapCount > 0) errorTypes.Add($"자기중첩:{SelfOverlapCount}개");
                if (UndershootCount > 0) errorTypes.Add($"언더슛:{UndershootCount}개");
                if (OvershootCount > 0) errorTypes.Add($"오버슛:{OvershootCount}개");
                if (BasicValidationErrorCount > 0) errorTypes.Add($"기본 유효성:{BasicValidationErrorCount}개");
                
                return errorTypes.Any() ? string.Join(", ", errorTypes) : "오류없음";
            }
        }

        /// <summary>
        /// 주요 오류 유형 (가장 많은 오류)
        /// </summary>
        public string PrimaryErrorType
        {
            get
            {
                var maxCount = Math.Max(DuplicateCount, Math.Max(OverlapCount, Math.Max(SelfIntersectionCount,
                    Math.Max(SliverCount, Math.Max(ShortObjectCount, Math.Max(SmallAreaCount, PolygonInPolygonCount))))));
                
                if (maxCount == 0) return "오류없음";
                
                if (DuplicateCount == maxCount) return "중복 객체";
                if (OverlapCount == maxCount) return "겹침 객체";
                if (SelfIntersectionCount == maxCount) return "자체 꼬임";
                if (SliverCount == maxCount) return "슬리버 폴리곤";
                if (ShortObjectCount == maxCount) return "짧은 객체";
                if (SmallAreaCount == maxCount) return "작은 면적";
                if (PolygonInPolygonCount == maxCount) return "홀 폴리곤 오류";
                
                return "기타 오류";
            }
        }
    }

    /// <summary>
    /// 지오메트리 오류 상세 정보
    /// </summary>
    public class GeometryErrorDetail
    {
        /// <summary>
        /// 객체 ID
        /// </summary>
        public string ObjectId { get; set; } = string.Empty;

        /// <summary>
        /// 오류 유형
        /// </summary>
        public string ErrorType { get; set; } = string.Empty;

        /// <summary>
        /// 오류 값 (구체적인 오류 종류 포함)
        /// </summary>
        public string ErrorValue { get; set; } = string.Empty;

        /// <summary>
        /// 기준값
        /// </summary>
        public string ThresholdValue { get; set; } = string.Empty;

        /// <summary>
        /// 위치 정보 (X, Y 좌표)
        /// </summary>
        public string Location { get; set; } = string.Empty;

        /// <summary>
        /// 상세 메시지
        /// </summary>
        public string DetailMessage { get; set; } = string.Empty;

        /// <summary>
        /// X 좌표
        /// </summary>
        public double X { get; set; }

        /// <summary>
        /// Y 좌표
        /// </summary>
        public double Y { get; set; }

        /// <summary>
        /// 지오메트리 WKT
        /// </summary>
        public string? GeometryWkt { get; set; }

        /// <summary>
        /// 오류 종류가 포함된 상세 오류 값 (UI 표시용)
        /// </summary>
        public string DetailedErrorValue
        {
            get
            {
                if (string.IsNullOrEmpty(ErrorValue))
                    return ErrorType;
                
                return $"{ErrorType}: {ErrorValue}";
            }
        }

        /// <summary>
        /// 오류 심각도 (UI 표시용)
        /// </summary>
        public string Severity
        {
            get
            {
                // 오류 유형에 따른 심각도 결정
                return ErrorType.ToLower() switch
                {
                    var type when type.Contains("중복") => "오류",
                    var type when type.Contains("겹침") => "오류", 
                    var type when type.Contains("자체") => "오류",
                    var type when type.Contains("슬리버") => "경고",
                    var type when type.Contains("짧은") => "경고",
                    var type when type.Contains("작은") => "경고",
                    var type when type.Contains("폴리곤") => "경고",
                    _ => "정보"
                };
            }
        }

        /// <summary>
        /// 오류 설명 (사용자 친화적)
        /// </summary>
        public string UserFriendlyDescription
        {
            get
            {
                return ErrorType.ToLower() switch
                {
                    var type when type.Contains("중복") => "동일한 지오메트리가 중복으로 존재합니다",
                    var type when type.Contains("겹침") => "다른 객체와 겹치는 영역이 있습니다",
                    var type when type.Contains("자체") => "지오메트리가 자기 자신과 교차합니다",
                    var type when type.Contains("슬리버") => "매우 좁은 폴리곤입니다",
                    var type when type.Contains("짧은") => "기준보다 짧은 선형 객체입니다",
                    var type when type.Contains("작은") => "기준보다 작은 면적의 객체입니다",
                    var type when type.Contains("폴리곤") => "다른 폴리곤 내부에 포함된 객체입니다",
                    _ => DetailMessage
                };
            }
        }
    }
}
