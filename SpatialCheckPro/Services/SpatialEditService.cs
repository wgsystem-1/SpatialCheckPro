using SpatialCheckPro.Models;
using SpatialCheckPro.Models.Enums;
using Microsoft.Extensions.Logging;
using NetTopologySuite.Geometries;
using OSGeo.OGR;
using System.Collections.Concurrent;
using System.IO;
using NTSGeometry = NetTopologySuite.Geometries.Geometry;
using IGeometry = NetTopologySuite.Geometries.Geometry;

namespace SpatialCheckPro.Services
{
    /// <summary>
    /// 공간정보 편집 서비스 구현
    /// </summary>
    public class SpatialEditService : ISpatialEditService
    {
        private readonly ILogger<SpatialEditService> _logger;
        private readonly IValidationService _validationService;
        private readonly ConcurrentDictionary<string, EditSession> _activeSessions;
        private readonly object _lockObject = new object();
        
        // 백업 생성 여부 설정 (환경 변수 또는 설정 파일에서 읽을 수 있음)
        private readonly bool _enableBackup = Environment.GetEnvironmentVariable("DISABLE_SPATIAL_BACKUP") != "1";

        public SpatialEditService(
            ILogger<SpatialEditService> logger,
            IValidationService validationService)
        {
            _logger = logger;
            _validationService = validationService;
            _activeSessions = new ConcurrentDictionary<string, EditSession>();

            // GDAL 초기화
            Ogr.RegisterAll();
        }

        /// <summary>
        /// 피처 편집 모드 시작
        /// </summary>
        public async Task<EditSession> StartEditSessionAsync(SpatialFileInfo spatialFile, string featureId)
        {
            try
            {
                _logger.LogInformation("편집 세션 시작: 파일={FileName}, 피처={FeatureId}", 
                    spatialFile.FileName, featureId);

                // 편집 가능 여부 확인
                var canEdit = await CanEditFeatureAsync(spatialFile, featureId);
                if (!canEdit)
                {
                    throw new InvalidOperationException($"피처 {featureId}는 편집할 수 없습니다.");
                }

                // 피처 정보 조회
                var featureInfo = await GetFeatureInfoAsync(spatialFile, featureId);
                if (featureInfo == null)
                {
                    throw new InvalidOperationException($"피처 {featureId}를 찾을 수 없습니다.");
                }

                // 편집 세션 생성
                var editSession = new EditSession
                {
                    SpatialFile = spatialFile,
                    FeatureId = featureId,
                    OriginalAttributes = new Dictionary<string, object>(featureInfo.Attributes),
                    OriginalGeometry = featureInfo.Geometry?.Copy(),
                    CurrentAttributes = new Dictionary<string, object>(featureInfo.Attributes),
                    CurrentGeometry = featureInfo.Geometry?.Copy(),
                    Status = EditSessionStatus.Active
                };

                // 활성 세션에 추가
                _activeSessions.TryAdd(editSession.SessionId, editSession);

                _logger.LogInformation("편집 세션 생성됨: {SessionId}", editSession.SessionId);
                return editSession;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "편집 세션 시작 실패: 파일={FileName}, 피처={FeatureId}", 
                    spatialFile.FileName, featureId);
                throw;
            }
        }

        /// <summary>
        /// 피처 속성 값 수정
        /// </summary>
        public async Task<EditResult> UpdateAttributeAsync(EditSession editSession, string attributeName, object newValue)
        {
            try
            {
                _logger.LogDebug("속성 수정: 세션={SessionId}, 속성={AttributeName}, 값={NewValue}", 
                    editSession.SessionId, attributeName, newValue);

                var result = new EditResult();

                // 세션 유효성 확인
                if (editSession.Status != EditSessionStatus.Active)
                {
                    result.IsSuccess = false;
                    result.ErrorMessage = "편집 세션이 활성 상태가 아닙니다.";
                    return result;
                }

                // 속성 존재 여부 확인
                if (!editSession.CurrentAttributes.ContainsKey(attributeName))
                {
                    result.IsSuccess = false;
                    result.ErrorMessage = $"속성 '{attributeName}'이 존재하지 않습니다.";
                    return result;
                }

                // 이전 값 저장
                var oldValue = editSession.CurrentAttributes[attributeName];

                // 값 유효성 검사
                var validationResult = await ValidateAttributeValueAsync(attributeName, newValue, editSession);
                if (validationResult.Any())
                {
                    result.ValidationErrors.AddRange(validationResult);
                    result.Warnings.Add($"속성 값 유효성 검사에서 {validationResult.Count}개의 문제가 발견되었습니다.");
                }

                // 값 업데이트
                editSession.CurrentAttributes[attributeName] = newValue;
                editSession.LastModified = DateTime.Now;

                // 편집 이력 추가
                editSession.EditHistory.Add(new EditOperation
                {
                    Type = EditOperationType.AttributeUpdate,
                    AttributeName = attributeName,
                    OldValue = oldValue,
                    NewValue = newValue,
                    Description = $"속성 '{attributeName}' 값을 '{oldValue}'에서 '{newValue}'로 변경"
                });

                result.IsSuccess = true;
                result.EditedValue = newValue;

                _logger.LogDebug("속성 수정 완료: 세션={SessionId}, 속성={AttributeName}", 
                    editSession.SessionId, attributeName);

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "속성 수정 실패: 세션={SessionId}, 속성={AttributeName}", 
                    editSession.SessionId, attributeName);
                
                return new EditResult
                {
                    IsSuccess = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        /// <summary>
        /// 지오메트리 수정
        /// </summary>
        public async Task<EditResult> UpdateGeometryAsync(EditSession editSession, IGeometry newGeometry)
        {
            try
            {
                _logger.LogDebug("지오메트리 수정: 세션={SessionId}", editSession.SessionId);

                var result = new EditResult();

                // 세션 유효성 확인
                if (editSession.Status != EditSessionStatus.Active)
                {
                    result.IsSuccess = false;
                    result.ErrorMessage = "편집 세션이 활성 상태가 아닙니다.";
                    return result;
                }

                // 지오메트리 유효성 검사
                var validationResult = await ValidateGeometryAsync(newGeometry, editSession);
                if (validationResult.Any())
                {
                    result.ValidationErrors.AddRange(validationResult);
                    result.Warnings.Add($"지오메트리 유효성 검사에서 {validationResult.Count}개의 문제가 발견되었습니다.");
                }

                // 이전 지오메트리 저장
                var oldGeometry = editSession.CurrentGeometry;

                // 지오메트리 업데이트
                editSession.CurrentGeometry = newGeometry?.Copy();
                editSession.LastModified = DateTime.Now;

                // 편집 이력 추가
                editSession.EditHistory.Add(new EditOperation
                {
                    Type = EditOperationType.GeometryUpdate,
                    OldValue = oldGeometry?.AsText(),
                    NewValue = newGeometry?.AsText(),
                    Description = "지오메트리 수정"
                });

                result.IsSuccess = true;
                result.EditedValue = newGeometry;

                _logger.LogDebug("지오메트리 수정 완료: 세션={SessionId}", editSession.SessionId);

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "지오메트리 수정 실패: 세션={SessionId}", editSession.SessionId);
                
                return new EditResult
                {
                    IsSuccess = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        /// <summary>
        /// 편집 내용 저장 및 세션 종료
        /// </summary>
        public async Task<SaveResult> SaveAndCloseEditSessionAsync(EditSession editSession)
        {
            try
            {
                _logger.LogInformation("편집 내용 저장: 세션={SessionId}", editSession.SessionId);

                var result = new SaveResult();

                // 세션 유효성 확인
                if (editSession.Status != EditSessionStatus.Active)
                {
                    result.IsSuccess = false;
                    result.ErrorMessage = "편집 세션이 활성 상태가 아닙니다.";
                    return result;
                }

                // 백업 파일 생성 (설정에 따라)
                if (_enableBackup)
                {
                    var backupPath = await CreateBackupFileAsync(editSession.SpatialFile);
                    result.BackupFilePath = backupPath;
                }
                else
                {
                    result.BackupFilePath = string.Empty; // 백업 비활성화
                    _logger.LogInformation("백업 파일 생성이 비활성화되어 있습니다.");
                }

                // 파일에 변경사항 저장
                var saveSuccess = await SaveChangesToFileAsync(editSession);
                if (!saveSuccess)
                {
                    result.IsSuccess = false;
                    result.ErrorMessage = "파일 저장에 실패했습니다.";
                    return result;
                }

                // 편집 세션 상태 업데이트
                editSession.Status = EditSessionStatus.Saved;

                // 활성 세션에서 제거
                _activeSessions.TryRemove(editSession.SessionId, out _);

                // 재검수 실행
                try
                {
                    result.RevalidationResult = await RevalidateAfterEditAsync(
                        editSession.SpatialFile, 
                        new[] { editSession.FeatureId });
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "편집 후 재검수 실패: 세션={SessionId}", editSession.SessionId);
                    result.RevalidationResult = null;
                }

                result.IsSuccess = true;
                result.SavedFeatureCount = 1;

                _logger.LogInformation("편집 내용 저장 완료: 세션={SessionId}", editSession.SessionId);
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "편집 내용 저장 실패: 세션={SessionId}", editSession.SessionId);
                
                return new SaveResult
                {
                    IsSuccess = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        /// <summary>
        /// 편집 내용 취소 및 세션 종료
        /// </summary>
        public async Task<bool> CancelEditSessionAsync(EditSession editSession)
        {
            try
            {
                _logger.LogInformation("편집 세션 취소: 세션={SessionId}", editSession.SessionId);

                // 편집 세션 상태 업데이트
                editSession.Status = EditSessionStatus.Cancelled;

                // 활성 세션에서 제거
                _activeSessions.TryRemove(editSession.SessionId, out _);

                _logger.LogInformation("편집 세션 취소 완료: 세션={SessionId}", editSession.SessionId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "편집 세션 취소 실패: 세션={SessionId}", editSession.SessionId);
                return false;
            }
        }

        /// <summary>
        /// 편집 후 자동 재검수 실행
        /// </summary>
        public async Task<ValidationResult> RevalidateAfterEditAsync(SpatialFileInfo spatialFile, IEnumerable<string> editedFeatureIds)
        {
            try
            {
                _logger.LogInformation("편집 후 재검수 실행: 파일={FileName}, 피처 수={FeatureCount}", 
                    spatialFile.FileName, editedFeatureIds.Count());

                // 전체 검수 실행 (실제 구현에서는 편집된 피처만 검수할 수 있도록 최적화 필요)
                var validationResult = await _validationService.ExecuteValidationAsync(spatialFile, null);

                _logger.LogInformation("편집 후 재검수 완료: 파일={FileName}", spatialFile.FileName);
                return validationResult;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "편집 후 재검수 실패: 파일={FileName}", spatialFile.FileName);
                throw;
            }
        }

        /// <summary>
        /// 편집 가능한 피처인지 확인
        /// </summary>
        public async Task<bool> CanEditFeatureAsync(SpatialFileInfo spatialFile, string featureId)
        {
            try
            {
                // 파일 형식 확인 (SHP 파일은 편집 제한이 있을 수 있음)
                if (spatialFile.Format == SpatialFileFormat.SHP)
                {
                    // SHP 파일의 경우 일부 제약사항이 있을 수 있음
                    _logger.LogDebug("SHP 파일 편집 가능성 확인: {FileName}", spatialFile.FileName);
                }

                // 파일 접근 권한 확인
                if (!File.Exists(spatialFile.FilePath))
                {
                    return false;
                }

                // 파일이 읽기 전용인지 확인
                var fileInfo = new FileInfo(spatialFile.FilePath);
                if (fileInfo.IsReadOnly)
                {
                    return false;
                }

                // 다른 세션에서 편집 중인지 확인
                var existingSession = _activeSessions.Values
                    .FirstOrDefault(s => s.SpatialFile.FilePath == spatialFile.FilePath && 
                                        s.FeatureId == featureId && 
                                        s.Status == EditSessionStatus.Active);

                if (existingSession != null)
                {
                    _logger.LogWarning("피처가 이미 편집 중입니다: 세션={SessionId}, 피처={FeatureId}", 
                        existingSession.SessionId, featureId);
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "편집 가능성 확인 실패: 파일={FileName}, 피처={FeatureId}", 
                    spatialFile.FileName, featureId);
                return false;
            }
        }

        /// <summary>
        /// 피처 정보 조회
        /// </summary>
        public async Task<FeatureInfo> GetFeatureInfoAsync(SpatialFileInfo spatialFile, string featureId)
        {
            try
            {
                _logger.LogDebug("피처 정보 조회: 파일={FileName}, 피처={FeatureId}", 
                    spatialFile.FileName, featureId);

                return await Task.Run(() =>
                {
                    // 실제 구현에서는 GDAL을 사용하여 피처 정보를 조회해야 함
                    // 여기서는 임시 구현
                    var featureInfo = new FeatureInfo
                    {
                        FeatureId = featureId,
                        TableName = spatialFile.Tables.FirstOrDefault()?.TableName ?? "Unknown",
                        GeometryType = "Point",
                        IsEditable = true,
                        IsLocked = false
                    };

                    // 임시 속성 데이터
                    featureInfo.Attributes["ID"] = featureId;
                    featureInfo.Attributes["NAME"] = $"Feature_{featureId}";
                    featureInfo.Attributes["TYPE"] = "Test";

                    // 임시 지오메트리 (포인트)
                    var geometryFactory = new GeometryFactory();
                    featureInfo.Geometry = geometryFactory.CreatePoint(new Coordinate(127.0, 37.5));

                    return featureInfo;
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "피처 정보 조회 실패: 파일={FileName}, 피처={FeatureId}", 
                    spatialFile.FileName, featureId);
                return null;
            }
        }

        /// <summary>
        /// 활성 편집 세션 목록 조회
        /// </summary>
        public IEnumerable<EditSession> GetActiveEditSessions()
        {
            return _activeSessions.Values.Where(s => s.Status == EditSessionStatus.Active).ToList();
        }

        #region 내부 메서드

        /// <summary>
        /// 속성 값 유효성 검사
        /// </summary>
        private async Task<List<ValidationError>> ValidateAttributeValueAsync(string attributeName, object value, EditSession editSession)
        {
            var errors = new List<ValidationError>();

            try
            {
                // 기본 유효성 검사
                if (value == null)
                {
                    // null 값 허용 여부 확인 (실제 구현에서는 스키마 정보 확인 필요)
                    // 여기서는 임시로 경고만 추가
                    errors.Add(new ValidationError
                    {
                        TableName = editSession.SpatialFile.FileName,
                        FeatureId = editSession.FeatureId,
                        Message = $"속성 '{attributeName}'에 null 값이 설정되었습니다.",
                        Severity = ErrorSeverity.Warning,
                        ErrorType = ErrorType.SchemaError
                    });
                }

                // 데이터 타입 검사 (실제 구현에서는 스키마 정보 기반으로 검사)
                // 여기서는 간단한 검사만 수행
                if (value is string stringValue && stringValue.Length > 255)
                {
                    errors.Add(new ValidationError
                    {
                        TableName = editSession.SpatialFile.FileName,
                        FeatureId = editSession.FeatureId,
                        Message = $"속성 '{attributeName}' 값이 너무 깁니다 (최대 255자).",
                        Severity = ErrorSeverity.Error,
                        ErrorType = ErrorType.SchemaError
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "속성 값 유효성 검사 실패: 속성={AttributeName}", attributeName);
            }

            return errors;
        }

        /// <summary>
        /// 지오메트리 유효성 검사
        /// </summary>
        private async Task<List<ValidationError>> ValidateGeometryAsync(IGeometry geometry, EditSession editSession)
        {
            var errors = new List<ValidationError>();

            try
            {
                if (geometry == null)
                {
                    errors.Add(new ValidationError
                    {
                        TableName = editSession.SpatialFile.FileName,
                        FeatureId = editSession.FeatureId,
                        Message = "지오메트리가 null입니다.",
                        Severity = ErrorSeverity.Error,
                        ErrorType = ErrorType.GeometryError
                    });
                    return errors;
                }

                // 지오메트리 유효성 검사
                if (!geometry.IsValid)
                {
                    errors.Add(new ValidationError
                    {
                        TableName = editSession.SpatialFile.FileName,
                        FeatureId = editSession.FeatureId,
                        Message = "지오메트리가 유효하지 않습니다.",
                        Severity = ErrorSeverity.Error,
                        ErrorType = ErrorType.GeometryError
                    });
                }

                // 빈 지오메트리 검사
                if (geometry.IsEmpty)
                {
                    errors.Add(new ValidationError
                    {
                        TableName = editSession.SpatialFile.FileName,
                        FeatureId = editSession.FeatureId,
                        Message = "지오메트리가 비어있습니다.",
                        Severity = ErrorSeverity.Warning,
                        ErrorType = ErrorType.GeometryError
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "지오메트리 유효성 검사 실패");
                errors.Add(new ValidationError
                {
                    TableName = editSession.SpatialFile.FileName,
                    FeatureId = editSession.FeatureId,
                    Message = $"지오메트리 유효성 검사 중 오류 발생: {ex.Message}",
                    Severity = ErrorSeverity.Error,
                    ErrorType = ErrorType.GeometryError
                });
            }

            return errors;
        }

        /// <summary>
        /// 백업 파일 생성
        /// </summary>
        private async Task<string> CreateBackupFileAsync(SpatialFileInfo spatialFile)
        {
            try
            {
                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                var backupFileName = $"{Path.GetFileNameWithoutExtension(spatialFile.FileName)}_backup_{timestamp}{Path.GetExtension(spatialFile.FileName)}";
                var backupPath = Path.Combine(Path.GetDirectoryName(spatialFile.FilePath), backupFileName);

                await Task.Run(() =>
                {
                    File.Copy(spatialFile.FilePath, backupPath, true);
                    
                    // SHP 파일인 경우 관련 파일들도 복사
                    if (spatialFile.Format == SpatialFileFormat.SHP)
                    {
                        var baseName = Path.GetFileNameWithoutExtension(spatialFile.FilePath);
                        var directory = Path.GetDirectoryName(spatialFile.FilePath);
                        var backupBaseName = Path.GetFileNameWithoutExtension(backupPath);
                        var backupDirectory = Path.GetDirectoryName(backupPath);
                        
                        var extensions = new[] { ".shx", ".dbf", ".prj", ".cpg" };
                        foreach (var ext in extensions)
                        {
                            var sourceFile = Path.Combine(directory, baseName + ext);
                            var backupFile = Path.Combine(backupDirectory, backupBaseName + ext);
                            
                            if (File.Exists(sourceFile))
                            {
                                File.Copy(sourceFile, backupFile, true);
                            }
                        }
                    }
                });

                _logger.LogInformation("백업 파일 생성됨: {BackupPath}", backupPath);
                return backupPath;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "백업 파일 생성 실패: {FilePath}", spatialFile.FilePath);
                throw;
            }
        }

        /// <summary>
        /// 파일에 변경사항 저장
        /// </summary>
        private async Task<bool> SaveChangesToFileAsync(EditSession editSession)
        {
            try
            {
                _logger.LogDebug("파일에 변경사항 저장: 세션={SessionId}", editSession.SessionId);

                return await Task.Run(() =>
                {
                    // 실제 구현에서는 GDAL을 사용하여 파일에 변경사항을 저장해야 함
                    // 여기서는 임시 구현으로 성공 반환
                    
                    _logger.LogDebug("변경사항 저장 완료: 세션={SessionId}", editSession.SessionId);
                    return true;
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "파일 저장 실패: 세션={SessionId}", editSession.SessionId);
                return false;
            }
        }

        #endregion
    }
}
