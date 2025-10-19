using SpatialCheckPro.Models;
using SpatialCheckPro.Models.Enums;
using System.ComponentModel.DataAnnotations;

namespace SpatialCheckPro.Data
{
    /// <summary>
    /// 공간정보 파일 정보 엔티티 클래스
    /// </summary>
    public class SpatialFileInfoEntity
    {
        /// <summary>
        /// 엔티티 ID (Primary Key)
        /// </summary>
        [Key]
        public int Id { get; set; }

        /// <summary>
        /// 파일 경로
        /// </summary>
        public string FilePath { get; set; } = string.Empty;

        /// <summary>
        /// 파일명
        /// </summary>
        public string FileName { get; set; } = string.Empty;

        /// <summary>
        /// 파일 형식
        /// </summary>
        public SpatialFileFormat Format { get; set; }

        /// <summary>
        /// 파일 크기 (바이트)
        /// </summary>
        public long FileSize { get; set; }

        /// <summary>
        /// 좌표계 정보
        /// </summary>
        public string CoordinateSystem { get; set; } = string.Empty;

        /// <summary>
        /// 파일 생성 일시
        /// </summary>
        public DateTime CreatedAt { get; set; }

        /// <summary>
        /// 파일 수정 일시
        /// </summary>
        public DateTime ModifiedAt { get; set; }

        /// <summary>
        /// 테이블 정보 JSON 문자열
        /// </summary>
        public List<TableInfo> TablesJson { get; set; } = new List<TableInfo>();

        /// <summary>
        /// 도메인 모델로 변환
        /// </summary>
        /// <returns>SpatialFileInfo 도메인 모델</returns>
        public SpatialFileInfo ToDomainModel()
        {
            return new SpatialFileInfo
            {
                FilePath = FilePath,
                FileName = FileName,
                Format = Format,
                FileSize = FileSize,
                CoordinateSystem = CoordinateSystem,
                CreatedAt = CreatedAt,
                ModifiedAt = ModifiedAt,
                Tables = TablesJson
            };
        }

        /// <summary>
        /// 도메인 모델에서 엔티티로 변환
        /// </summary>
        /// <param name="domainModel">SpatialFileInfo 도메인 모델</param>
        /// <returns>SpatialFileInfoEntity</returns>
        public static SpatialFileInfoEntity FromDomainModel(SpatialFileInfo domainModel)
        {
            return new SpatialFileInfoEntity
            {
                FilePath = domainModel.FilePath,
                FileName = domainModel.FileName,
                Format = domainModel.Format,
                FileSize = domainModel.FileSize,
                CoordinateSystem = domainModel.CoordinateSystem,
                CreatedAt = domainModel.CreatedAt ?? DateTime.Now,
                ModifiedAt = domainModel.ModifiedAt ?? DateTime.Now,
                TablesJson = domainModel.Tables
            };
        }
    }
}
