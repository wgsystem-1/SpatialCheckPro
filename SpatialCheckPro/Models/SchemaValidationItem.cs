using System;
using System.Collections.Generic;
using System.Linq;

namespace SpatialCheckPro.Models
{
    /// <summary>
    /// 스키마 검수 항목 결과
    /// </summary>
    public class SchemaValidationItem
    {
        /// <summary>
        /// 테이블 ID
        /// </summary>
        public string TableId { get; set; } = string.Empty;

        /// <summary>
        /// 컬럼명칭 (영문)
        /// </summary>
        public string ColumnName { get; set; } = string.Empty;

        /// <summary>
        /// 컬럼한글명
        /// </summary>
        public string ColumnKoreanName { get; set; } = string.Empty;

        /// <summary>
        /// 예상 데이터 타입
        /// </summary>
        public string ExpectedDataType { get; set; } = string.Empty;

        /// <summary>
        /// 실제 데이터 타입
        /// </summary>
        public string ActualDataType { get; set; } = string.Empty;

        /// <summary>
        /// 예상 길이
        /// </summary>
        public string ExpectedLength { get; set; } = string.Empty;

        /// <summary>
        /// 실제 길이
        /// </summary>
        public string ActualLength { get; set; } = string.Empty;

        /// <summary>
        /// 예상 Not Null 여부
        /// </summary>
        public string ExpectedNotNull { get; set; } = string.Empty;

        /// <summary>
        /// 실제 Not Null 여부
        /// </summary>
        public string ActualNotNull { get; set; } = string.Empty;

        /// <summary>
        /// 예상 Primary Key 여부
        /// </summary>
        public string ExpectedPrimaryKey { get; set; } = string.Empty;

        /// <summary>
        /// 실제 Primary Key 여부
        /// </summary>
        public string ActualPrimaryKey { get; set; } = string.Empty;

        /// <summary>
        /// 예상 Unique Key 여부
        /// </summary>
        public string ExpectedUniqueKey { get; set; } = string.Empty;

        /// <summary>
        /// 실제 Unique Key 여부
        /// </summary>
        public string ActualUniqueKey { get; set; } = string.Empty;

        /// <summary>
        /// 예상 Foreign Key 여부
        /// </summary>
        public string ExpectedForeignKey { get; set; } = string.Empty;

        /// <summary>
        /// 실제 Foreign Key 여부
        /// </summary>
        public string ActualForeignKey { get; set; } = string.Empty;

        /// <summary>
        /// 참조 테이블명
        /// </summary>
        public string ReferenceTable { get; set; } = string.Empty;

        /// <summary>
        /// 참조 컬럼명
        /// </summary>
        public string ReferenceColumn { get; set; } = string.Empty;

        /// <summary>
        /// 컬럼 존재 여부
        /// </summary>
        public bool ColumnExists { get; set; } = false;

        /// <summary>
        /// 데이터 타입 일치 여부
        /// </summary>
        public bool DataTypeMatches { get; set; } = false;

        /// <summary>
        /// 길이 일치 여부
        /// </summary>
        public bool LengthMatches { get; set; } = false;

        /// <summary>
        /// Not Null 일치 여부
        /// </summary>
        public bool NotNullMatches { get; set; } = false;

        /// <summary>
        /// Primary Key 일치 여부
        /// </summary>
        public bool PrimaryKeyMatches { get; set; } = false;

        /// <summary>
        /// Unique Key 일치 여부
        /// </summary>
        public bool UniqueKeyMatches { get; set; } = false;

        /// <summary>
        /// Foreign Key 일치 여부
        /// </summary>
        public bool ForeignKeyMatches { get; set; } = false;

        /// <summary>
        /// 정의되지 않은(스키마 설정에 없는) 컬럼 여부
        /// </summary>
        public bool IsUndefinedField { get; set; } = false;

        /// <summary>
        /// 전체 검수 통과 여부 (PK 검사만 제외)
        /// </summary>
        public bool IsValid => ColumnExists && DataTypeMatches && LengthMatches && NotNullMatches && 
                               UniqueKeyMatches && ForeignKeyMatches && !IsUndefinedField;

        /// <summary>
        /// 검수 상태
        /// </summary>
        public string Status { get; set; } = string.Empty;

        /// <summary>
        /// 오류내용 (최대 30자, IsValid가 false인 경우 오류 내용 작성, true인 경우 null)
        /// </summary>
        public string? ErrorContent { get; set; } = null;

        /// <summary>
        /// 오류 메시지 목록
        /// </summary>
        public List<string> Errors { get; set; } = new();

        /// <summary>
        /// 경고 메시지 목록
        /// </summary>
        public List<string> Warnings { get; set; } = new();

        /// <summary>
        /// 검수를 수행했는지 여부
        /// </summary>
        public bool IsProcessed { get; set; } = true;

        /// <summary>
        /// OBJECTID 특별 처리 여부
        /// </summary>
        public bool IsObjectIdField { get; set; } = false;

        /// <summary>
        /// FID 필드로 처리되었는지 여부
        /// </summary>
        public bool IsFidField { get; set; } = false;

        /// <summary>
        /// PK/UK 중복 검사 결과
        /// </summary>
        public int DuplicateValueCount { get; set; } = 0;

        /// <summary>
        /// 중복된 값들의 목록 (최대 10개까지)
        /// </summary>
        public List<string> DuplicateValues { get; set; } = new();

        /// <summary>
        /// Domain 검증 결과
        /// </summary>
        public bool DomainValidationPassed { get; set; } = true;

        /// <summary>
        /// Domain 위반 값들의 개수
        /// </summary>
        public int InvalidDomainValueCount { get; set; } = 0;

        /// <summary>
        /// Domain 위반 값들의 목록 (최대 10개까지)
        /// </summary>
        public List<string> InvalidDomainValues { get; set; } = new();

        /// <summary>
        /// FK 관계 검증 결과
        /// </summary>
        public bool ForeignKeyValidationPassed { get; set; } = true;

        /// <summary>
        /// 고아 레코드 개수 (FK 위반)
        /// </summary>
        public int OrphanRecordCount { get; set; } = 0;

        /// <summary>
        /// 고아 레코드 값들의 목록 (최대 10개까지)
        /// </summary>
        public List<string> OrphanRecordValues { get; set; } = new();

        /// <summary>
        /// 고아 레코드 값들의 목록 (FK 검사용 별칭)
        /// </summary>
        public List<string> OrphanValues { get; set; } = new();

        /// <summary>
        /// 검수 세부 정보 (OBJECTID, PK/UK 중복, Domain, FK 등)
        /// </summary>
        public string DetailedValidationInfo { get; set; } = string.Empty;

        /// <summary>
        /// NN일치 표시용 (Y/N)
        /// </summary>
        public string NotNullMatchesDisplay => NotNullMatches ? "Y" : "N";

        /// <summary>
        /// 검수결과 표시용 (통과/실패/경고)
        /// </summary>
        public string IsValidDisplay 
        { 
            get
            {
                if (!IsValid) return "실패";
                if (DuplicateValueCount > 0 || InvalidDomainValueCount > 0 || OrphanRecordCount > 0)
                    return "경고";
                return "통과";
            }
        }

        /// <summary>
        /// PK/UK 중복 검사 표시용
        /// </summary>
        public string DuplicateCheckDisplay => DuplicateValueCount > 0 ? $"중복 {DuplicateValueCount}개" : "정상";

        /// <summary>
        /// Domain 검증 표시용
        /// </summary>
        public string DomainValidationDisplay => InvalidDomainValueCount > 0 ? $"위반 {InvalidDomainValueCount}개" : "정상";

        /// <summary>
        /// FK 관계 검증 표시용
        /// </summary>
        public string ForeignKeyValidationDisplay => OrphanRecordCount > 0 ? $"고아 {OrphanRecordCount}개" : "정상";

        /// <summary>
        /// OBJECTID 처리 정보 표시용
        /// </summary>
        public string ObjectIdProcessingInfo
        {
            get
            {
                if (IsObjectIdField && IsFidField)
                    return "FID 필드로 처리됨";
                else if (IsObjectIdField)
                    return "OBJECTID 필드";
                return "";
            }
        }

        /// <summary>
        /// 중복값 미리보기 (최대 3개)
        /// </summary>
        public string DuplicateValuesPreview
        {
            get
            {
                if (DuplicateValues.Count == 0) return "";
                var preview = string.Join(", ", DuplicateValues.Take(3));
                return DuplicateValues.Count > 3 ? preview + "..." : preview;
            }
        }

        /// <summary>
        /// 위반값 미리보기 (Domain + FK 고아 레코드, 최대 3개)
        /// </summary>
        public string InvalidValuesPreview
        {
            get
            {
                var allInvalidValues = new List<string>();
                allInvalidValues.AddRange(InvalidDomainValues);
                allInvalidValues.AddRange(OrphanRecordValues);
                
                if (allInvalidValues.Count == 0) return "";
                var preview = string.Join(", ", allInvalidValues.Take(3));
                return allInvalidValues.Count > 3 ? preview + "..." : preview;
            }
        }

        /// <summary>
        /// UK 검수 결과를 통합합니다
        /// </summary>
        /// <param name="ukResult">UK 검수 결과</param>
        public void IntegrateUniqueKeyResult(UniqueKeyValidationResult ukResult)
        {
            if (ukResult == null) return;

            // UK 검수 결과 반영
            UniqueKeyMatches = ukResult.IsValid;
            
            if (!ukResult.IsValid)
            {
                DuplicateValueCount = ukResult.DuplicateValues;
                DuplicateValues = ukResult.Duplicates.Take(10).Select(d => d.Value).ToList();
                
                // 오류 메시지 추가
                Errors.Add($"UK 제약 위반: {ukResult.DuplicateValues}개의 중복값 발견");
                
                // 상세 정보 업데이트
                var duplicateInfo = string.Join(", ", ukResult.Duplicates.Take(3).Select(d => $"'{d.Value}'({d.Count}회)"));
                if (ukResult.Duplicates.Count > 3)
                {
                    duplicateInfo += $" 외 {ukResult.Duplicates.Count - 3}개";
                }
                DetailedValidationInfo += $"UK 중복값: {duplicateInfo}; ";
            }
            else
            {
                DuplicateValueCount = 0;
                DuplicateValues.Clear();
            }
        }

        /// <summary>
        /// FK 검수 결과를 통합합니다
        /// </summary>
        /// <param name="fkResult">FK 검수 결과</param>
        public void IntegrateForeignKeyResult(ForeignKeyValidationResult fkResult)
        {
            if (fkResult == null) return;

            // FK 검수 결과 반영
            ForeignKeyMatches = fkResult.IsValid;
            ForeignKeyValidationPassed = fkResult.IsValid;
            
            if (!fkResult.IsValid)
            {
                OrphanRecordCount = fkResult.OrphanRecords;
                OrphanRecordValues = fkResult.Orphans.Take(10).Select(o => o.Value).ToList();
                OrphanValues = OrphanRecordValues; // 동기화
                
                // 오류 메시지 추가
                Errors.Add($"FK 제약 위반: {fkResult.OrphanRecords}개의 고아 레코드 발견");
                
                // 상세 정보 업데이트
                var orphanInfo = string.Join(", ", fkResult.Orphans.Take(3).Select(o => $"'{o.Value}'"));
                if (fkResult.Orphans.Count > 3)
                {
                    orphanInfo += $" 외 {fkResult.Orphans.Count - 3}개";
                }
                DetailedValidationInfo += $"FK 고아레코드: {orphanInfo}; ";
            }
            else
            {
                OrphanRecordCount = 0;
                OrphanRecordValues.Clear();
                OrphanValues.Clear(); // 동기화
            }
        }

        /// <summary>
        /// 표준화된 오류 메시지를 생성합니다
        /// </summary>
        /// <returns>사용자 친화적인 오류 메시지</returns>
        public string GetStandardizedErrorMessage()
        {
            if (IsValid && DuplicateValueCount == 0 && OrphanRecordCount == 0)
            {
                return "검수 통과";
            }

            var messages = new List<string>();

            // 스키마 오류
            if (!ColumnExists)
            {
                messages.Add("필드가 존재하지 않습니다");
            }
            else
            {
                if (!DataTypeMatches)
                {
                    messages.Add($"데이터 타입이 일치하지 않습니다 (예상: {ExpectedDataType}, 실제: {ActualDataType})");
                }
                
                if (!LengthMatches)
                {
                    messages.Add($"필드 길이가 일치하지 않습니다 (예상: {ExpectedLength}, 실제: {ActualLength})");
                }
                
                if (!NotNullMatches)
                {
                    var expectedMsg = ExpectedNotNull == "Y" ? "NOT NULL 필수" : "NULL 허용";
                    var actualMsg = ActualNotNull == "Y" ? "NOT NULL" : "NULL 허용";
                    messages.Add($"NULL 제약 조건이 일치하지 않습니다 (설정: {expectedMsg}, 실제: {actualMsg})");
                }
            }

            // UK 오류
            if (DuplicateValueCount > 0)
            {
                messages.Add($"UNIQUE KEY 제약 위반: {DuplicateValueCount}개의 중복값이 발견되었습니다");
                if (DuplicateValues.Any())
                {
                    var preview = string.Join(", ", DuplicateValues.Take(3).Select(v => $"'{v}'"));
                    if (DuplicateValues.Count > 3) preview += "...";
                    messages.Add($"중복값 예시: {preview}");
                }
            }

            // FK 오류
            if (OrphanRecordCount > 0)
            {
                messages.Add($"FOREIGN KEY 제약 위반: {OrphanRecordCount}개의 고아 레코드가 발견되었습니다");
                if (OrphanRecordValues.Any())
                {
                    var preview = string.Join(", ", OrphanRecordValues.Take(3).Select(v => $"'{v}'"));
                    if (OrphanRecordValues.Count > 3) preview += "...";
                    messages.Add($"고아 레코드 예시: {preview}");
                }
            }

            return string.Join(". ", messages);
        }

        /// <summary>
        /// 검수 이력 비교를 위한 요약 정보를 생성합니다
        /// </summary>
        /// <returns>검수 요약 정보</returns>
        public SchemaValidationSummary GetValidationSummary()
        {
            return new SchemaValidationSummary
            {
                TableId = TableId,
                ColumnName = ColumnName,
                IsValid = IsValid,
                HasDuplicates = DuplicateValueCount > 0,
                HasOrphanRecords = OrphanRecordCount > 0,
                ErrorCount = Errors.Count,
                WarningCount = Warnings.Count,
                ValidationTimestamp = DateTime.UtcNow
            };
        }

        /// <summary>
        /// 오류내용 자동 생성 (IsValid가 false인 경우 오류 내용 작성, true인 경우 null)
        /// </summary>
        public void UpdateErrorContent()
        {
            if (IsValid && DuplicateValueCount == 0 && OrphanRecordCount == 0)
            {
                ErrorContent = null;
                return;
            }

            var errorMessages = new List<string>();

            // 정의 상태 및 존재 여부 확인
            if (IsUndefinedField)
            {
                errorMessages.Add("정의에 없는 컬럼");
            }
            else if (!ColumnExists)
            {
                errorMessages.Add("컬럼 누락");
            }
            else
            {
                // 데이터 타입 불일치
                if (!DataTypeMatches)
                {
                    errorMessages.Add($"타입불일치({ActualDataType})");
                }

                // 길이 불일치
                if (!LengthMatches)
                {
                    errorMessages.Add($"길이불일치({ActualLength})");
                }

                // Not Null 불일치
                if (!NotNullMatches)
                {
                    errorMessages.Add($"NN불일치({ActualNotNull})");
                }

                // Unique Key 불일치
                if (!UniqueKeyMatches)
                {
                    errorMessages.Add($"UK불일치({ActualUniqueKey})");
                }

                // Foreign Key 불일치
                if (!ForeignKeyMatches)
                {
                    errorMessages.Add($"FK불일치({ActualForeignKey})");
                }
            }

            // 중복값 존재
            if (DuplicateValueCount > 0)
            {
                errorMessages.Add($"중복{DuplicateValueCount}개");
            }

            // Domain 위반
            if (InvalidDomainValueCount > 0)
            {
                errorMessages.Add($"도메인위반{InvalidDomainValueCount}개");
            }

            // FK 고아 레코드
            if (OrphanRecordCount > 0)
            {
                errorMessages.Add($"고아레코드{OrphanRecordCount}개");
            }

            // 최대 30자로 제한하여 오류내용 생성
            var errorContent = string.Join(",", errorMessages);
            if (errorContent.Length > 30)
            {
                ErrorContent = errorContent.Substring(0, 27) + "...";
            }
            else
            {
                ErrorContent = errorContent;
            }

            // 빈 문자열인 경우 기본 메시지
            if (string.IsNullOrEmpty(ErrorContent))
            {
                ErrorContent = "검수실패";
            }
        }
    }

    /// <summary>
    /// 스키마 검수 요약 정보
    /// </summary>
    public class SchemaValidationSummary
    {
        /// <summary>
        /// 테이블 ID
        /// </summary>
        public string TableId { get; set; } = string.Empty;

        /// <summary>
        /// 컬럼명
        /// </summary>
        public string ColumnName { get; set; } = string.Empty;

        /// <summary>
        /// 검수 통과 여부
        /// </summary>
        public bool IsValid { get; set; }

        /// <summary>
        /// 중복값 존재 여부
        /// </summary>
        public bool HasDuplicates { get; set; }

        /// <summary>
        /// 고아 레코드 존재 여부
        /// </summary>
        public bool HasOrphanRecords { get; set; }

        /// <summary>
        /// 오류 개수
        /// </summary>
        public int ErrorCount { get; set; }

        /// <summary>
        /// 경고 개수
        /// </summary>
        public int WarningCount { get; set; }

        /// <summary>
        /// 검수 시간
        /// </summary>
        public DateTime ValidationTimestamp { get; set; }
    }
}
