using System;
using System.IO;
using System.Threading.Tasks;

namespace SpatialCheckPro.Models
{
    /// <summary>
    /// 지오메트리 검수 기준 설정
    /// </summary>
    public class GeometryCriteria
    {
        /// <summary>
        /// 최소 선 길이 (미터)
        /// </summary>
        public double MinLineLength { get; set; } = 0.01;

        /// <summary>
        /// 최소 면적 (제곱미터)
        /// </summary>
        public double MinPolygonArea { get; set; } = 1.0;

        /// <summary>
        /// 겹침 허용 면적 (제곱미터)
        /// </summary>
        public double OverlapTolerance { get; set; } = 0.001;

        /// <summary>
        /// 자체 꼬임 허용 각도 (도)
        /// </summary>
        public double SelfIntersectionAngle { get; set; } = 1.0;

        /// <summary>
        /// 폴리곤 내 폴리곤 최소 거리 (미터)
        /// </summary>
        public double PolygonInPolygonDistance { get; set; } = 0.1;

        /// <summary>
        /// 슬리버폴리곤 면적 기준 (제곱미터)
        /// </summary>
        public double SliverArea { get; set; } = 2.0;

        /// <summary>
        /// 슬리버폴리곤 형태지수 기준
        /// </summary>
        public double SliverShapeIndex { get; set; } = 0.1;

        /// <summary>
        /// 슬리버폴리곤 신장률 기준
        /// </summary>
        public double SliverElongation { get; set; } = 10.0;

        /// <summary>
        /// 스파이크비율
        /// </summary>
        public double SpikeRatio { get; set; }

        /// <summary>
        /// 스파이크허용각도
        /// </summary>
        public double SpikeAngleThreshold { get; set; }

        /// <summary>
        /// 스파이크를 모두 저장할지 여부 (true = 모든 정점 저장, false = 가장 날카로운 1개만)
        /// </summary>
        public bool SaveAllSpikes { get; set; } = false;

        /// <summary>
        /// 네트워크탐색거리
        /// </summary>
        public double NetworkSearchDistance { get; set; }

    /// <summary>
    /// 링 폐합 허용 오차 (미터)
    /// </summary>
    public double RingClosureTolerance { get; set; } = 1e-8;

    /// <summary>
    /// 스파이크 각도 임계값 (도)
    /// </summary>
    public double SpikeAngleThresholdDegrees { get; set; } = 10.0;

    /// <summary>
    /// 중복 검사 허용 오차 (미터)
    /// </summary>
    public double DuplicateCheckTolerance { get; set; } = 0.001;

    // ===== 관계 검수용 허용 오차 =====

    /// <summary>
    /// 선-면 포함 관계 검수 기본 허용 오차 (미터)
    /// LineWithinPolygon 케이스용
    /// </summary>
    public double LineWithinPolygonTolerance { get; set; } = 0.001;

    /// <summary>
    /// 면-면 포함 관계 검수 기본 허용 오차 (미터)
    /// PolygonNotWithinPolygon 케이스용
    /// </summary>
    public double PolygonNotWithinPolygonTolerance { get; set; } = 0.001;

    /// <summary>
    /// 선 연결성 검수 기본 탐색 거리 (미터)
    /// LineConnectivity 케이스용
    /// </summary>
    public double LineConnectivityTolerance { get; set; } = 1.0;

    /// <summary>
    /// 기본 설정으로 초기화
    /// </summary>
    public static GeometryCriteria CreateDefault()
    {
        return new GeometryCriteria
        {
            MinLineLength = 0.01,
            MinPolygonArea = 1.0,
            OverlapTolerance = 0.001,
            SelfIntersectionAngle = 1.0,
            PolygonInPolygonDistance = 0.1,
            SliverArea = 2.0,
            SliverShapeIndex = 0.1,
            SliverElongation = 10.0,
            RingClosureTolerance = 1e-8,
            SpikeAngleThresholdDegrees = 10.0,
            DuplicateCheckTolerance = 0.001,
            NetworkSearchDistance = 0.1,
            SpikeRatio = 0.1,
            SpikeAngleThreshold = 0.1,
            SaveAllSpikes = false
        };
    }

    /// <summary>
    /// CSV 파일에서 기준값을 로드합니다
    /// </summary>
    public static async Task<GeometryCriteria> LoadFromCsvAsync(string csvFilePath)
    {
        var criteria = CreateDefault();
        
        try
        {
            if (!File.Exists(csvFilePath))
            {
                return criteria;
            }

            var lines = await File.ReadAllLinesAsync(csvFilePath, System.Text.Encoding.UTF8);
            
            // 헤더 라인 스킵 (항목명,값,단위,설명)
            for (int i = 1; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                var parts = line.Split(',');
                if (parts.Length >= 2)
                {
                    var key = parts[0].Trim();
                    var valueStr = parts[1].Trim();
                    
                    if (double.TryParse(valueStr, System.Globalization.NumberStyles.Float, 
                        System.Globalization.CultureInfo.InvariantCulture, out double value))
                    {
                        switch (key)
                        {
                            case "최소선길이":
                                criteria.MinLineLength = value;
                                break;
                            case "최소폴리곤면적":
                                criteria.MinPolygonArea = value;
                                break;
                            case "겹침허용면적":
                                criteria.OverlapTolerance = value;
                                break;
                            case "자체꼬임허용각도":
                                criteria.SelfIntersectionAngle = value;
                                break;
                            case "폴리곤내폴리곤최소거리":
                                criteria.PolygonInPolygonDistance = value;
                                break;
                            case "슬리버면적":
                                criteria.SliverArea = value;
                                break;
                            case "슬리버형태지수":
                                criteria.SliverShapeIndex = value;
                                break;
                            case "슬리버신장률":
                                criteria.SliverElongation = value;
                                break;
                            case "링폐합오차":
                                criteria.RingClosureTolerance = value;
                                break;
                            case "스파이크각도임계값":
                                criteria.SpikeAngleThresholdDegrees = value;
                                break;
                            case "스파이크모두저장":
                                criteria.SaveAllSpikes = value > 0.0;
                                break;
                            case "네트워크탐색거리":
                                criteria.NetworkSearchDistance = value;
                                break;
                            case "중복검사허용오차":
                                criteria.DuplicateCheckTolerance = value;
                                break;
                            case "선면포함관계허용오차":
                                criteria.LineWithinPolygonTolerance = value;
                                break;
                            case "면면포함관계허용오차":
                                criteria.PolygonNotWithinPolygonTolerance = value;
                                break;
                            case "선연결성탐색거리":
                                criteria.LineConnectivityTolerance = value;
                                break;
                        }
                    }
                }
            }
        }
        catch (Exception)
        {
            // 오류 발생 시 기본값 반환
            return CreateDefault();
        }

        return criteria;
    }
    }
}
