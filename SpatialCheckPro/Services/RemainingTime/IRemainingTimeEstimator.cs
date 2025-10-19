#nullable enable
using System.Collections.Generic;
using System.Threading.Tasks;
using SpatialCheckPro.Models;
using SpatialCheckPro.Services.RemainingTime.Models;

namespace SpatialCheckPro.Services.RemainingTime
{
    /// <summary>
    /// 남은 시간을 추정하는 서비스 인터페이스
    /// </summary>
    public interface IRemainingTimeEstimator
    {
        /// <summary>
        /// 단계별 예측 초기값을 등록합니다.
        /// </summary>
        /// <param name="stagePredictions">단계 번호와 예상 소요 초</param>
        /// <param name="context">현재 작업 컨텍스트 메타데이터</param>
        void SeedPredictions(IDictionary<int, double> stagePredictions, ValidationRunContext context);

        /// <summary>
        /// 현재 단계 진행 상황을 반영하고 결과를 반환합니다.
        /// </summary>
        /// <param name="sample">진행 샘플 정보</param>
        /// <returns>ETA 추정 결과</returns>
        StageEtaResult UpdateProgress(StageProgressSample sample);

        /// <summary>
        /// 특정 단계의 최신 ETA를 조회합니다.
        /// </summary>
        /// <param name="stageId">단계 식별자</param>
        /// <returns>ETA 추정 결과 또는 null</returns>
        StageEtaResult? GetStageEta(string stageId);

        /// <summary>
        /// 전체 남은 시간 추정값을 계산합니다.
        /// </summary>
        /// <returns>전체 ETA 결과</returns>
        OverallEtaResult GetOverallEta();

        /// <summary>
        /// 검수 종료 후 실제 소요 시간 기록을 저장합니다.
        /// </summary>
        /// <param name="history">종료된 단계 이력</param>
        Task RecordStageHistoryAsync(IEnumerable<StageDurationSample> history);
    }
}


