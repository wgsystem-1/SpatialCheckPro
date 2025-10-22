using Microsoft.Extensions.Logging;
using OSGeo.OGR;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using System;
using System.IO;
using System.Linq;
using System.Text;

namespace SpatialCheckPro.Services
{
    /// <summary>
    /// FileGDB를 SpatiaLite(SQLite) DB로 변환하는 서비스
    /// </summary>
    public class GdbToSqliteConverter
    {
        private readonly ILogger<GdbToSqliteConverter> _logger;
        private readonly IDataSourcePool _dataSourcePool;

        public GdbToSqliteConverter(ILogger<GdbToSqliteConverter> logger, IDataSourcePool dataSourcePool)
        {
            _logger = logger;
            _dataSourcePool = dataSourcePool;
        }

        /// <summary>
        /// GDB를 SQLite로 변환하고 임시 파일 경로를 반환합니다.
        /// </summary>
        public async Task<string> ConvertAsync(string gdbPath)
        {
            var tempSqlitePath = Path.Combine(Path.GetTempPath(), $"spatialcheckpro_{Guid.NewGuid()}.sqlite");
            _logger.LogInformation("임시 SpatiaLite DB 생성 시작: {Path}", tempSqlitePath);

            await Task.Run(() =>
            {
                // SpatiaLite DB 연결 및 초기화
                using var connection = new SqliteConnection($"Data Source={tempSqlitePath}");
                connection.Open();
                connection.EnableExtensions(true);
                TryInitializeSpatialite(connection);

                var gdbDataSource = _dataSourcePool.GetDataSource(gdbPath);
                if (gdbDataSource == null)
                {
                    _logger.LogError("원본 GDB를 열 수 없습니다: {Path}", gdbPath);
                    throw new Exception("원본 GDB를 열 수 없습니다.");
                }

                try
                {
                    // 각 레이어를 SQLite 테이블로 복사
                    for (int i = 0; i < gdbDataSource.GetLayerCount(); i++)
                    {
                        var layer = gdbDataSource.GetLayerByIndex(i);
                        CopyLayerToSqlite(layer, connection);
                    }
                }
                finally
                {
                    _dataSourcePool.ReturnDataSource(gdbPath, gdbDataSource);
                }
            });

            _logger.LogInformation("SpatiaLite DB 생성 완료: {Path}", tempSqlitePath);
            return tempSqlitePath;
        }

        private void CopyLayerToSqlite(Layer layer, SqliteConnection connection)
        {
            var layerName = layer.GetName();
            _logger.LogInformation("레이어 복사 중: {LayerName}", layerName);

            // 레이어 정의 및 스키마 추출
            using var layerDefn = layer.GetLayerDefn();
            var fieldCount = layerDefn.GetFieldCount();

            // 테이블명: 스키마 충돌 가능성 최소화를 위해 따옴표 처리
            var tableName = SanitizeIdentifier(layerName);

            using var cmd = connection.CreateCommand();
            using var tx = connection.BeginTransaction();
            cmd.Transaction = tx;

            try
            {
                // 기본 테이블 생성 (속성 컬럼)
                var sb = new StringBuilder();
                sb.Append($"CREATE TABLE IF NOT EXISTS \"{tableName}\" (");
                sb.Append("fid INTEGER PRIMARY KEY");

                for (int i = 0; i < fieldCount; i++)
                {
                    using var fdef = layerDefn.GetFieldDefn(i);
                    var colName = SanitizeIdentifier(fdef.GetName());
                    var colType = MapOgrFieldTypeToSqliteType(fdef.GetFieldType());
                    if (!string.Equals(colName, "fid", StringComparison.OrdinalIgnoreCase))
                    {
                        sb.Append($", \"{colName}\" {colType}");
                    }
                }
                sb.Append(")");

                cmd.CommandText = sb.ToString();
                cmd.Parameters.Clear();
                cmd.ExecuteNonQuery();

                // SpatiaLite 메타데이터 초기화는 상위에서 수행됨. 지오메트리 컬럼 추가
                var geomTypeName = GetGeometryTypeName(layer.GetGeomType());
                var srid = 0; // SRID 추출이 어려운 경우 0으로 설정
                try
                {
                    using var sref = layer.GetSpatialRef();
                    if (sref != null)
                    {
                        // EPSG 코드 직접 추출이 어렵다면 0 유지
                        // 추후 필요 시 WKT 파싱 또는 OSR API 활용 가능
                    }
                }
                catch { }

                cmd.CommandText = $"SELECT AddGeometryColumn(\"{tableName}\", 'geom', {srid}, '{geomTypeName}', 'XY')";
                cmd.Parameters.Clear();
                try
                {
                    cmd.ExecuteNonQuery();
                }
                catch (Exception ex)
                {
                    // 이미 존재하거나 SpatiaLite 미초기화 시 오류 가능 → 경고 후 계속
                    _logger.LogWarning(ex, "지오메트리 컬럼 추가 중 경고: {Table}", tableName);
                }

                // 인덱스 생성(자주 사용하는 컬럼 패턴)
                CreateCommonIndexes(connection, tableName, layerDefn);

                // 데이터 삽입 (배치)
                layer.ResetReading();
                var featureCount = (int)layer.GetFeatureCount(1);

                // INSERT 문 생성
                var colNames = Enumerable.Range(0, fieldCount)
                    .Select(i => {
                        using var fd = layerDefn.GetFieldDefn(i);
                        return SanitizeIdentifier(fd.GetName());
                    })
                    .Where(n => !string.Equals(n, "fid", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                var paramNames = colNames.Select(n => $"@{n}").ToList();
                // fid와 geom 포함
                var insertSql = new StringBuilder();
                insertSql.Append($"INSERT INTO \"{tableName}\" (");
                insertSql.Append("fid");
                if (colNames.Count > 0)
                {
                    insertSql.Append(", ");
                    insertSql.Append(string.Join(", ", colNames.Select(n => $"\"{n}\"")));
                }
                insertSql.Append(", geom) VALUES ( @fid");
                if (paramNames.Count > 0)
                {
                    insertSql.Append(", ");
                    insertSql.Append(string.Join(", ", paramNames));
                }
                insertSql.Append(", GeomFromWKB(@wkb, @srid) )");

                cmd.CommandText = insertSql.ToString();
                cmd.Parameters.Clear();
                cmd.Parameters.Add(new SqliteParameter("@fid", 0));
                foreach (var n in colNames)
                {
                    cmd.Parameters.Add(new SqliteParameter($"@{n}", DBNull.Value));
                }
                cmd.Parameters.Add(new SqliteParameter("@wkb", Array.Empty<byte>()));
                cmd.Parameters.Add(new SqliteParameter("@srid", srid));

                int inserted = 0;
                Feature? f;
                while ((f = layer.GetNextFeature()) != null)
                {
                    using (f)
                    {
                        // fid
                        var fid = f.GetFID();
                        cmd.Parameters["@fid"].Value = fid;

                        // 속성 파라미터
                        for (int i = 0; i < fieldCount; i++)
                        {
                            using var fd = layerDefn.GetFieldDefn(i);
                            var name = SanitizeIdentifier(fd.GetName());
                            if (string.Equals(name, "fid", StringComparison.OrdinalIgnoreCase)) continue;

                            var p = cmd.Parameters[$"@{name}"];
                            switch (fd.GetFieldType())
                            {
                                case FieldType.OFTInteger:
                                case FieldType.OFTInteger64:
                                    p.Value = f.GetFieldAsInteger64(i);
                                    break;
                                case FieldType.OFTReal:
                                    p.Value = f.GetFieldAsDouble(i);
                                    break;
                                case FieldType.OFTString:
                                case FieldType.OFTWideString:
                                    p.Value = f.GetFieldAsString(i) ?? (object)DBNull.Value;
                                    break;
                                case FieldType.OFTDate:
                                case FieldType.OFTTime:
                                case FieldType.OFTDateTime:
                                    p.Value = f.GetFieldAsString(i) ?? (object)DBNull.Value;
                                    break;
                                default:
                                    p.Value = f.GetFieldAsString(i) ?? (object)DBNull.Value;
                                    break;
                            }
                        }

                        // 지오메트리 WKB
                        using var geom = f.GetGeometryRef();
                        if (geom != null)
                        {
                            // OGR WKB 추출: 버퍼를 미리 할당한 뒤 내보낸다
                            var size = geom.WkbSize();
                            var wkb = new byte[size];
                            geom.ExportToWkb(wkb, wkbByteOrder.wkbNDR);
                            cmd.Parameters["@wkb"].Value = wkb;
                        }
                        else
                        {
                            cmd.Parameters["@wkb"].Value = DBNull.Value;
                        }

                        cmd.ExecuteNonQuery();
                        inserted++;
                    }
                }

                // 공간 인덱스 생성 (성능 향상)
                try
                {
                    cmd.CommandText = $"SELECT CreateSpatialIndex(\"{tableName}\", 'geom')";
                    cmd.Parameters.Clear();
                    cmd.ExecuteNonQuery();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "공간 인덱스 생성 중 경고: {Table}", tableName);
                }

                tx.Commit();
                _logger.LogInformation("레이어 복사 완료: {LayerName}, {Count}개 삽입", layerName, inserted);
            }
            catch (Exception ex)
            {
                try { tx.Rollback(); } catch { }
                _logger.LogError(ex, "레이어 복사 실패: {LayerName}", layerName);
                throw;
            }
        }

        private static string SanitizeIdentifier(string? name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "unnamed";
            // SQLite에서 허용되지 않는 문자 단순 치환
            var cleaned = new string(name.Select(ch => char.IsLetterOrDigit(ch) || ch == '_' ? ch : '_').ToArray());
            return cleaned;
        }

        private static string MapOgrFieldTypeToSqliteType(FieldType fieldType)
        {
            return fieldType switch
            {
                FieldType.OFTInteger => "INTEGER",
                FieldType.OFTInteger64 => "INTEGER",
                FieldType.OFTReal => "REAL",
                FieldType.OFTString => "TEXT",
                FieldType.OFTWideString => "TEXT",
                FieldType.OFTDate => "TEXT",
                FieldType.OFTTime => "TEXT",
                FieldType.OFTDateTime => "TEXT",
                _ => "TEXT"
            };
        }

        private static string GetGeometryTypeName(wkbGeometryType geomType)
        {
            // 대표적인 매핑만 우선 처리
            switch (geomType)
            {
                case wkbGeometryType.wkbPoint:
                case wkbGeometryType.wkbPoint25D: return "POINT";
                case wkbGeometryType.wkbLineString:
                case wkbGeometryType.wkbLineString25D: return "LINESTRING";
                case wkbGeometryType.wkbPolygon:
                case wkbGeometryType.wkbPolygon25D: return "POLYGON";
                case wkbGeometryType.wkbMultiPoint:
                case wkbGeometryType.wkbMultiPoint25D: return "MULTIPOINT";
                case wkbGeometryType.wkbMultiLineString:
                case wkbGeometryType.wkbMultiLineString25D: return "MULTILINESTRING";
                case wkbGeometryType.wkbMultiPolygon:
                case wkbGeometryType.wkbMultiPolygon25D: return "MULTIPOLYGON";
                default: return "GEOMETRY";
            }
        }

        private void CreateCommonIndexes(SqliteConnection connection, string tableName, FeatureDefn layerDefn)
        {
            // 자주 검색되는 키 컬럼 패턴
            var candidateNames = new[] { "OBJECTID", "FID", "ID", "KEY", "CODE" };
            for (int i = 0; i < layerDefn.GetFieldCount(); i++)
            {
                using var fd = layerDefn.GetFieldDefn(i);
                var name = fd.GetName();
                if (candidateNames.Any(c => name.Equals(c, StringComparison.OrdinalIgnoreCase)))
                {
                    try
                    {
                        using var cmd = connection.CreateCommand();
                        cmd.CommandText = $"CREATE INDEX IF NOT EXISTS \"idx_{SanitizeIdentifier(tableName)}_{SanitizeIdentifier(name)}\" ON \"{SanitizeIdentifier(tableName)}\"(\"{SanitizeIdentifier(name)}\")";
                        cmd.ExecuteNonQuery();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "인덱스 생성 경고: {Table}.{Column}", tableName, name);
                    }
                }
            }
        }

        private void TryInitializeSpatialite(SqliteConnection connection)
        {
            using var cmd = connection.CreateCommand();
            try
            {
                // 확장 로드 시도: 절대 경로 우선 → 기본명
                var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                var candidates = new[]
                {
                    Path.Combine(baseDir, "runtimes", "win-x64", "native", "mod_spatialite.dll"),
                    Path.Combine(baseDir, "ThirdParty", "SpatiaLite", "win-x64", "mod_spatialite.dll"),
                    Path.Combine(baseDir, "mod_spatialite.dll"),
                    "mod_spatialite"
                };

                var loaded = false;
                foreach (var c in candidates)
                {
                    try
                    {
                        cmd.CommandText = "SELECT load_extension(@p1);";
                        cmd.Parameters.Clear();
                        cmd.Parameters.Add(new SqliteParameter("@p1", c));
                        cmd.ExecuteNonQuery();
                        loaded = true;
                        _logger.LogInformation("SpatiaLite 확장 로드 성공: {Path}", c);
                        break;
                    }
                    catch { /* 다음 후보 시도 */ }
                }

                if (!loaded)
                {
                    _logger.LogWarning("SpatiaLite 확장을 로드하지 못했습니다. 공간 기능 일부가 제한될 수 있습니다.");
                }

                // 메타데이터 초기화 (이미 초기화되어도 무해)
                cmd.CommandText = "SELECT InitSpatialMetaData(1);";
                cmd.Parameters.Clear();
                cmd.ExecuteNonQuery();

                // 기본 PRAGMA 최적화
                using var pragma = connection.CreateCommand();
                pragma.CommandText = "PRAGMA foreign_keys=ON; PRAGMA journal_mode=WAL; PRAGMA synchronous=OFF;";
                pragma.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "SpatiaLite 초기화 경고");
            }
        }
    }
}
