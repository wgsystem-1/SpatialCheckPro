using System;

namespace SpatialCheckPro.Exceptions
{
    /// <summary>
    /// 권한 관련 예외를 나타내는 클래스
    /// </summary>
    public class PermissionException : Exception
    {
        /// <summary>
        /// 권한이 필요한 리소스
        /// </summary>
        public string Resource { get; }

        /// <summary>
        /// 필요한 권한 유형
        /// </summary>
        public PermissionType RequiredPermission { get; }

        /// <summary>
        /// 기본 생성자
        /// </summary>
        public PermissionException() : base("권한이 부족합니다.")
        {
            Resource = string.Empty;
        }

        /// <summary>
        /// 메시지를 포함한 생성자
        /// </summary>
        /// <param name="message">예외 메시지</param>
        public PermissionException(string message) : base(message)
        {
            Resource = string.Empty;
        }

        /// <summary>
        /// 메시지와 내부 예외를 포함한 생성자
        /// </summary>
        /// <param name="message">예외 메시지</param>
        /// <param name="innerException">내부 예외</param>
        public PermissionException(string message, Exception innerException) : base(message, innerException)
        {
            Resource = string.Empty;
        }

        /// <summary>
        /// 리소스와 권한 유형을 포함한 생성자
        /// </summary>
        /// <param name="resource">리소스명</param>
        /// <param name="requiredPermission">필요한 권한 유형</param>
        /// <param name="message">예외 메시지</param>
        public PermissionException(string resource, PermissionType requiredPermission, string message) : base(message)
        {
            Resource = resource;
            RequiredPermission = requiredPermission;
        }
    }

    /// <summary>
    /// 권한 유형
    /// </summary>
    public enum PermissionType
    {
        /// <summary>읽기 권한</summary>
        Read,
        /// <summary>쓰기 권한</summary>
        Write,
        /// <summary>실행 권한</summary>
        Execute,
        /// <summary>삭제 권한</summary>
        Delete,
        /// <summary>관리자 권한</summary>
        Administrator
    }
}
