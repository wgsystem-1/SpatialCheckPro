using System;

namespace SpatialCheckPro.Exceptions
{
    /// <summary>
    /// 파일 접근 관련 예외를 나타내는 클래스
    /// </summary>
    public class FileAccessException : Exception
    {
        /// <summary>
        /// 접근하려던 파일 경로
        /// </summary>
        public string FilePath { get; }

        /// <summary>
        /// 파일 접근 작업 유형
        /// </summary>
        public FileAccessOperation Operation { get; }

        /// <summary>
        /// 기본 생성자
        /// </summary>
        public FileAccessException() : base("파일 접근 중 오류가 발생했습니다.")
        {
            FilePath = string.Empty;
        }

        /// <summary>
        /// 메시지를 포함한 생성자
        /// </summary>
        /// <param name="message">예외 메시지</param>
        public FileAccessException(string message) : base(message)
        {
            FilePath = string.Empty;
        }

        /// <summary>
        /// 메시지와 내부 예외를 포함한 생성자
        /// </summary>
        /// <param name="message">예외 메시지</param>
        /// <param name="innerException">내부 예외</param>
        public FileAccessException(string message, Exception innerException) : base(message, innerException)
        {
            FilePath = string.Empty;
        }

        /// <summary>
        /// 파일 경로와 작업 유형을 포함한 생성자
        /// </summary>
        /// <param name="filePath">파일 경로</param>
        /// <param name="operation">파일 접근 작업 유형</param>
        /// <param name="message">예외 메시지</param>
        public FileAccessException(string filePath, FileAccessOperation operation, string message) : base(message)
        {
            FilePath = filePath;
            Operation = operation;
        }

        /// <summary>
        /// 파일 경로, 작업 유형, 내부 예외를 포함한 생성자
        /// </summary>
        /// <param name="filePath">파일 경로</param>
        /// <param name="operation">파일 접근 작업 유형</param>
        /// <param name="message">예외 메시지</param>
        /// <param name="innerException">내부 예외</param>
        public FileAccessException(string filePath, FileAccessOperation operation, string message, Exception innerException) 
            : base(message, innerException)
        {
            FilePath = filePath;
            Operation = operation;
        }
    }

    /// <summary>
    /// 파일 접근 작업 유형
    /// </summary>
    public enum FileAccessOperation
    {
        /// <summary>파일 읽기</summary>
        Read,
        /// <summary>파일 쓰기</summary>
        Write,
        /// <summary>파일 생성</summary>
        Create,
        /// <summary>파일 삭제</summary>
        Delete,
        /// <summary>파일 이동</summary>
        Move,
        /// <summary>파일 복사</summary>
        Copy,
        /// <summary>파일 열기</summary>
        Open
    }
}
