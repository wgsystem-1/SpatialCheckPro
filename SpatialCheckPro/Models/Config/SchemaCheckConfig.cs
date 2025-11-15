using System.ComponentModel.DataAnnotations;
using CsvHelper.Configuration.Attributes;

namespace SpatialCheckPro.Models.Config
{
    /// <summary>
    /// 2단계 스키마 검수 설정 모델 클래스
    /// </summary>
    public class SchemaCheckConfig
    {
        /// <summary>
        /// 테이블 ID
        /// </summary>
        [Name("TableId")]
        [Required(ErrorMessage = "테이블ID는 필수 입력값입니다.")]
        public string TableId { get; set; } = string.Empty;

        /// <summary>
        /// 컬럼명칭 (영문)
        /// </summary>
        [Name("FieldName")]
        [Required(ErrorMessage = "컬럼명칭은 필수 입력값입니다.")]
        public string ColumnName { get; set; } = string.Empty;

        /// <summary>
        /// 컬럼한글명
        /// </summary>
        [Name("FieldAlias")]
        public string ColumnKoreanName { get; set; } = string.Empty;

        /// <summary>
        /// 데이터 타입 (INTEGER, TEXT, NUMERIC, CHAR)
        /// </summary>
        [Name("DataType")]
        [Required(ErrorMessage = "타입은 필수 입력값입니다.")]
        public string DataType { get; set; } = string.Empty;

        /// <summary>
        /// 컬럼 길이 (문자열 또는 "길이,소수점" 형태)
        /// </summary>
        [Name("Length")]
        public string Length { get; set; } = string.Empty;

        /// <summary>
        /// Primary Key 여부 (PK 또는 공백) - 현재 CSV에 PK 컬럼이 없으므로 사용하지 않음
        /// </summary>
        [Ignore]
        public string PrimaryKey { get; set; } = string.Empty;

        /// <summary>
        /// Unique Key 여부 (UK 또는 공백)
        /// </summary>
        [Name("UK")]
        public string UniqueKey { get; set; } = string.Empty;

        /// <summary>
        /// Foreign Key 여부 (FK 또는 공백)
        /// </summary>
        [Name("FK")]
        public string ForeignKey { get; set; } = string.Empty;

        /// <summary>
        /// Not Null 여부 (Y/N 또는 공백)
        /// </summary>
        [Name("NN")]
        public string IsNotNull { get; set; } = string.Empty;

        /// <summary>
        /// 참조 테이블명
        /// </summary>
        [Name("RefTable")]
        public string ReferenceTable { get; set; } = string.Empty;

        /// <summary>
        /// 참조 컬럼명
        /// </summary>
        [Name("RefColumn")]
        public string ReferenceColumn { get; set; } = string.Empty;



        /// <summary>
        /// Not Null 여부 확인
        /// </summary>
        public bool IsNotNullColumn => IsNotNull.Equals("Y", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Primary Key 여부 확인
        /// </summary>
        public bool IsPrimaryKeyColumn => PrimaryKey.Equals("PK", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Unique Key 여부 확인
        /// </summary>
        public bool IsUniqueKeyColumn => UniqueKey.Equals("UK", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Foreign Key 여부 확인
        /// </summary>
        public bool IsForeignKeyColumn => ForeignKey.Equals("FK", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// 텍스트 타입인지 확인
        /// </summary>
        public bool IsTextType => DataType.Equals("TEXT", StringComparison.OrdinalIgnoreCase) ||
                                  DataType.Equals("CHAR", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// 숫자 타입인지 확인
        /// </summary>
        public bool IsNumericType => DataType.Equals("INTEGER", StringComparison.OrdinalIgnoreCase) ||
                                     DataType.Equals("NUMERIC", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// 정수 타입인지 확인
        /// </summary>
        public bool IsIntegerType => DataType.Equals("INTEGER", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// 실수 타입인지 확인
        /// </summary>
        public bool IsDecimalType => DataType.Equals("NUMERIC", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// 문자 타입인지 확인
        /// </summary>
        public bool IsCharType => DataType.Equals("CHAR", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// 날짜 타입인지 확인
        /// </summary>
        public bool IsDateType => DataType.Equals("DATE", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// 데이터 타입이 유효한지 검증
        /// </summary>
        /// <returns>유효성 검증 결과</returns>
        public bool IsValidDataType()
        {
            var validTypes = new[] { "INTEGER", "TEXT", "NUMERIC", "CHAR", "DATE" };
            return validTypes.Contains(DataType, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 길이 정보를 파싱하여 정수 길이 반환
        /// </summary>
        /// <returns>길이 값 (파싱 실패 시 0)</returns>
        public int GetIntegerLength()
        {
            if (string.IsNullOrWhiteSpace(Length)) return 0;
            
            // "길이,소수점" 형태인 경우 첫 번째 값만 사용
            var parts = Length.Split(',');
            if (int.TryParse(parts[0].Trim(), out int length))
            {
                return length;
            }
            
            return 0;
        }

        /// <summary>
        /// 소수점 자릿수 반환 (NUMERIC 타입인 경우)
        /// </summary>
        /// <returns>소수점 자릿수 (없으면 0)</returns>
        public int GetDecimalPlaces()
        {
            if (string.IsNullOrWhiteSpace(Length)) return 0;
            
            var parts = Length.Split(',');
            if (parts.Length > 1 && int.TryParse(parts[1].Trim(), out int decimalPlaces))
            {
                return decimalPlaces;
            }
            
            return 0;
        }



        /// <summary>
        /// 설정 유효성 검증
        /// </summary>
        /// <returns>검증 결과 목록</returns>
        public List<System.ComponentModel.DataAnnotations.ValidationResult> Validate()
        {
            var results = new List<System.ComponentModel.DataAnnotations.ValidationResult>();
            var context = new ValidationContext(this);

            Validator.TryValidateObject(this, context, results, true);

            // 추가 비즈니스 로직 검증
            if (!IsValidDataType())
            {
                results.Add(new System.ComponentModel.DataAnnotations.ValidationResult(
                    $"지원하지 않는 데이터 타입입니다: {DataType}",
                    new[] { nameof(DataType) }));
            }

            // Foreign Key 검증
            if (IsForeignKeyColumn)
            {
                if (string.IsNullOrWhiteSpace(ReferenceTable))
                {
                    results.Add(new System.ComponentModel.DataAnnotations.ValidationResult(
                        "Foreign Key인 경우 참조 테이블명이 필요합니다.",
                        new[] { nameof(ReferenceTable) }));
                }
                
                if (string.IsNullOrWhiteSpace(ReferenceColumn))
                {
                    results.Add(new System.ComponentModel.DataAnnotations.ValidationResult(
                        "Foreign Key인 경우 참조 컬럼명이 필요합니다.",
                        new[] { nameof(ReferenceColumn) }));
                }
            }

            // Primary Key와 Unique Key 동시 설정 검증
            if (IsPrimaryKeyColumn && IsUniqueKeyColumn)
            {
                results.Add(new System.ComponentModel.DataAnnotations.ValidationResult(
                    "Primary Key와 Unique Key를 동시에 설정할 수 없습니다.",
                    new[] { nameof(PrimaryKey), nameof(UniqueKey) }));
            }

            // 텍스트 타입 길이 검증
            if (IsTextType || IsCharType)
            {
                var length = GetIntegerLength();
                if (length <= 0 && !string.IsNullOrWhiteSpace(Length))
                {
                    results.Add(new System.ComponentModel.DataAnnotations.ValidationResult(
                        $"텍스트 타입의 길이 형식이 올바르지 않습니다: {Length}",
                        new[] { nameof(Length) }));
                }
            }

            // NUMERIC 타입 길이 검증
            if (IsDecimalType && !string.IsNullOrWhiteSpace(Length))
            {
                var parts = Length.Split(',');
                if (parts.Length == 2)
                {
                    if (!int.TryParse(parts[0].Trim(), out _) || !int.TryParse(parts[1].Trim(), out _))
                    {
                        results.Add(new System.ComponentModel.DataAnnotations.ValidationResult(
                            $"NUMERIC 타입의 길이 형식이 올바르지 않습니다: {Length} (예: '10,2')",
                            new[] { nameof(Length) }));
                    }
                }
            }

            // 테이블ID 형식 검증
            if (string.IsNullOrWhiteSpace(TableId))
            {
                results.Add(new System.ComponentModel.DataAnnotations.ValidationResult(
                    "테이블ID는 필수 입력값입니다.",
                    new[] { nameof(TableId) }));
            }

            return results;
        }

        /// <summary>
        /// 문자열 표현
        /// </summary>
        /// <returns>컬럼 정보 문자열</returns>
        public override string ToString()
        {
            return $"{TableId}.{ColumnName}: {ColumnKoreanName} ({DataType})";
        }
    }
}
