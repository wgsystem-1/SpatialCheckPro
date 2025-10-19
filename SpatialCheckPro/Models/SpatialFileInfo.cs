using SpatialCheckPro.Models.Enums;
using System.Collections.Generic;

namespace SpatialCheckPro.Models
{
    /// <summary>
    /// 공간정보 파일 정보를 나타내는 모델 클래스
    /// </summary>
    public class SpatialFileInfo
    {
        /// <summary>
        /// 파일 경로
        /// </summary>
        public string FilePath { get; set; } = string.Empty;

        /// <summary>
        /// 파일명
        /// </summary>
        public string FileName { get; set; } = string.Empty;

        /// <summary>
        /// 파일 형식 (SHP, FileGDB, GeoPackage)
        /// </summary>
        public SpatialFileFormat Format { get; set; }

        /// <summary>
        /// 파일 크기 (바이트)
        /// </summary>
        public long FileSize { get; set; }

        /// <summary>
        /// 테이블 목록
        /// </summary>
        public List<TableInfo> Tables { get; set; } = new List<TableInfo>();

        /// <summary>
        /// 좌표계 정보
        /// </summary>
        public string CoordinateSystem { get; set; } = string.Empty;

        /// <summary>
        /// 파일 생성 일시
        /// </summary>
        public DateTime? CreatedAt { get; set; }

        /// <summary>
        /// 파일 수정 일시
        /// </summary>
        public DateTime? ModifiedAt { get; set; }
    }
}
