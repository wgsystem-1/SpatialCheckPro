namespace SpatialCheckPro.Models.Enums
{
    /// <summary>
    /// 병렬 처리 작업 유형
    /// </summary>
    public enum ProcessingType
    {
        /// <summary>
        /// I/O 중심 작업 (예: 파일 읽기) - 낮은 병렬도 사용
        /// </summary>
        IOBound,

        /// <summary>
        /// CPU 중심 작업 (예: 계산, 분석) - 높은 병렬도 사용
        /// </summary>
        CPUBound
    }
}
