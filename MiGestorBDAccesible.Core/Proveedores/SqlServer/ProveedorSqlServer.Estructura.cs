using MiGestorBDAccesible.Core.Conexiones;
using MiGestorBDAccesible.Core.Estructura;
using Microsoft.Data.SqlClient;

namespace MiGestorBDAccesible.Core.Proveedores.SqlServer
{
    public partial class ProveedorSqlServer
    {
        private const string ConsultaBasesDatos = """
            SELECT name, database_id, state_desc, CAST(ISNULL(HAS_DBACCESS(name), 0) AS bit)
            FROM sys.databases
            ORDER BY name
            """;

        private const string ConsultaTablas = """
            SELECT s.name, t.name
            FROM sys.tables t
            JOIN sys.schemas s ON s.schema_id = t.schema_id
            WHERE t.is_ms_shipped = 0
            ORDER BY s.name, t.name
            """;

        private const string ConsultaDetalleTabla = """
            DECLARE @id int = OBJECT_ID(@nombre);

            IF @id IS NULL
                THROW 50000, 'La tabla ya no existe en la base de datos.', 1;

            SELECT @id,
                   ISNULL((SELECT SUM(p.rows) FROM sys.partitions p
                           WHERE p.object_id = @id AND p.index_id IN (0, 1)), 0);

            SELECT c.name, ty.name, c.max_length, c.precision, c.scale, c.is_nullable, c.is_identity,
                   CAST(ic.seed_value AS bigint), CAST(ic.increment_value AS bigint),
                   dc.definition, cc.definition, ISNULL(cc.is_persisted, 0), dc.name
            FROM sys.columns c
            JOIN sys.types ty ON ty.user_type_id = c.user_type_id
            LEFT JOIN sys.identity_columns ic ON ic.object_id = c.object_id AND ic.column_id = c.column_id
            LEFT JOIN sys.default_constraints dc ON dc.parent_object_id = c.object_id AND dc.parent_column_id = c.column_id
            LEFT JOIN sys.computed_columns cc ON cc.object_id = c.object_id AND cc.column_id = c.column_id
            WHERE c.object_id = @id
            ORDER BY c.column_id;

            SELECT i.name, i.type_desc, i.is_unique, i.is_primary_key, i.is_unique_constraint,
                   col.name, ic.is_descending_key, ic.is_included_column
            FROM sys.indexes i
            JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
            JOIN sys.columns col ON col.object_id = ic.object_id AND col.column_id = ic.column_id
            WHERE i.object_id = @id AND i.type > 0 AND i.is_hypothetical = 0
            ORDER BY i.is_primary_key DESC, i.name, ic.is_included_column, ic.key_ordinal, ic.index_column_id;

            WITH tablas AS (
                SELECT @id AS object_id
                UNION
                SELECT parent_object_id FROM sys.foreign_keys WHERE referenced_object_id = @id
            )
            SELECT fk.object_id, fk.name,
                   SCHEMA_NAME(po.schema_id), po.name, SCHEMA_NAME(ro.schema_id), ro.name,
                   pc.name, rc.name,
                   fk.delete_referential_action, fk.update_referential_action, fk.is_disabled,
                   fk.parent_object_id, fk.referenced_object_id
            FROM sys.foreign_keys fk
            JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id = fk.object_id
            JOIN sys.objects po ON po.object_id = fk.parent_object_id
            JOIN sys.objects ro ON ro.object_id = fk.referenced_object_id
            JOIN sys.columns pc ON pc.object_id = fkc.parent_object_id AND pc.column_id = fkc.parent_column_id
            JOIN sys.columns rc ON rc.object_id = fkc.referenced_object_id AND rc.column_id = fkc.referenced_column_id
            WHERE fk.parent_object_id IN (SELECT object_id FROM tablas) OR fk.referenced_object_id = @id
            ORDER BY fk.name, fkc.constraint_column_id;

            SELECT i.object_id, col.name
            FROM sys.indexes i
            JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
            JOIN sys.columns col ON col.object_id = ic.object_id AND col.column_id = ic.column_id
            WHERE i.is_primary_key = 1
              AND ic.key_ordinal > 0
              AND i.object_id IN (SELECT parent_object_id FROM sys.foreign_keys WHERE referenced_object_id = @id);

            SELECT cc.name, cc.definition, col.name
            FROM sys.check_constraints cc
            LEFT JOIN sys.columns col ON col.object_id = cc.parent_object_id AND col.column_id = cc.parent_column_id
            WHERE cc.parent_object_id = @id
            ORDER BY cc.name;
            """;

        public async Task<List<BaseDatosModelo>> ObtenerBasesDatosAsync(
            ConexionActiva conexion,
            CancellationToken cancelacion = default)
        {
            await using var conexionSql = await AbrirAsync(conexion, null, cancelacion);
            await using var comando = new SqlCommand(ConsultaBasesDatos, conexionSql);
            await using var lector = await comando.ExecuteReaderAsync(cancelacion);

            var bases = new List<BaseDatosModelo>();

            while (await lector.ReadAsync(cancelacion))
            {
                bases.Add(new BaseDatosModelo
                {
                    Nombre = lector.GetString(0),
                    EsSistema = lector.GetInt32(1) <= 4,
                    EnLinea = lector.GetString(2) == "ONLINE" && lector.GetBoolean(3)
                });
            }

            return bases;
        }

        public async Task<List<TablaModelo>> ObtenerTablasAsync(
            ConexionActiva conexion,
            string baseDatos,
            CancellationToken cancelacion = default)
        {
            await using var conexionSql = await AbrirAsync(conexion, baseDatos, cancelacion);
            await using var comando = new SqlCommand(ConsultaTablas, conexionSql);
            await using var lector = await comando.ExecuteReaderAsync(cancelacion);

            var tablas = new List<TablaModelo>();

            while (await lector.ReadAsync(cancelacion))
            {
                tablas.Add(new TablaModelo { Esquema = lector.GetString(0), Nombre = lector.GetString(1) });
            }

            return tablas;
        }

        public async Task<DetalleTablaModelo> ObtenerDetalleTablaAsync(
            ConexionActiva conexion,
            string baseDatos,
            TablaModelo tabla,
            CancellationToken cancelacion = default)
        {
            await using var conexionSql = await AbrirAsync(conexion, baseDatos, cancelacion);
            await using var comando = new SqlCommand(ConsultaDetalleTabla, conexionSql);
            comando.Parameters.AddWithValue("@nombre", $"{Delimitar(tabla.Esquema)}.{Delimitar(tabla.Nombre)}");

            await using var lector = await comando.ExecuteReaderAsync(cancelacion);

            await lector.ReadAsync(cancelacion);
            var idTabla = lector.GetInt32(0);
            var filas = lector.GetInt64(1);

            await lector.NextResultAsync(cancelacion);
            var columnas = new List<ColumnaModelo>();
            while (await lector.ReadAsync(cancelacion))
            {
                columnas.Add(new ColumnaModelo
                {
                    Nombre = lector.GetString(0),
                    TipoDatos = FormatearTipo(lector.GetString(1), lector.GetInt16(2), lector.GetByte(3), lector.GetByte(4)),
                    PermiteNulos = lector.GetBoolean(5),
                    EsIdentidad = lector.GetBoolean(6),
                    SemillaIdentidad = lector.IsDBNull(7) ? null : lector.GetInt64(7),
                    IncrementoIdentidad = lector.IsDBNull(8) ? null : lector.GetInt64(8),
                    ValorPredeterminado = lector.IsDBNull(9) ? null : lector.GetString(9),
                    Formula = lector.IsDBNull(10) ? null : lector.GetString(10),
                    EsPersistente = lector.GetBoolean(11),
                    NombrePredeterminado = lector.IsDBNull(12) ? null : lector.GetString(12)
                });
            }

            await lector.NextResultAsync(cancelacion);
            var indices = new List<IndiceModelo>();
            while (await lector.ReadAsync(cancelacion))
            {
                var nombre = lector.GetString(0);
                var indice = indices.FirstOrDefault(i => i.Nombre == nombre);

                if (indice is null)
                {
                    indice = new IndiceModelo
                    {
                        Nombre = nombre,
                        EsAgrupado = lector.GetString(1) == "CLUSTERED",
                        EsUnico = lector.GetBoolean(2),
                        EsClavePrincipal = lector.GetBoolean(3),
                        EsRestriccionUnica = lector.GetBoolean(4)
                    };
                    indices.Add(indice);
                }

                indice.Columnas.Add(new ColumnaIndiceModelo
                {
                    Nombre = lector.GetString(5),
                    Descendente = lector.GetBoolean(6),
                    Incluida = lector.GetBoolean(7)
                });
            }

            await lector.NextResultAsync(cancelacion);
            var llaves = new Dictionary<int, (LlaveForaneaModelo Llave, int Origen, int Destino)>();
            while (await lector.ReadAsync(cancelacion))
            {
                var id = lector.GetInt32(0);

                if (!llaves.TryGetValue(id, out var registro))
                {
                    registro = (new LlaveForaneaModelo
                    {
                        Nombre = lector.GetString(1),
                        TablaOrigen = new TablaModelo { Esquema = lector.GetString(2), Nombre = lector.GetString(3) },
                        TablaDestino = new TablaModelo { Esquema = lector.GetString(4), Nombre = lector.GetString(5) },
                        AlEliminar = ConvertirRegla(lector.GetByte(8)),
                        AlActualizar = ConvertirRegla(lector.GetByte(9)),
                        Habilitada = !lector.GetBoolean(10)
                    }, lector.GetInt32(11), lector.GetInt32(12));
                    llaves[id] = registro;
                }

                registro.Llave.ColumnasOrigen.Add(lector.GetString(6));
                registro.Llave.ColumnasDestino.Add(lector.GetString(7));
            }

            await lector.NextResultAsync(cancelacion);
            var clavesReferenciantes = new Dictionary<int, HashSet<string>>();
            while (await lector.ReadAsync(cancelacion))
            {
                var id = lector.GetInt32(0);

                if (!clavesReferenciantes.TryGetValue(id, out var claves))
                {
                    claves = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    clavesReferenciantes[id] = claves;
                }

                claves.Add(lector.GetString(1));
            }

            await lector.NextResultAsync(cancelacion);
            var restricciones = new List<RestriccionCheckModelo>();
            while (await lector.ReadAsync(cancelacion))
            {
                restricciones.Add(new RestriccionCheckModelo
                {
                    Nombre = lector.GetString(0),
                    Definicion = lector.GetString(1),
                    Columna = lector.IsDBNull(2) ? null : lector.GetString(2)
                });
            }

            var registros = llaves.Values.ToList();

            return new DetalleTablaModelo
            {
                Tabla = tabla,
                Filas = filas,
                Columnas = columnas,
                Indices = indices,
                LlavesForaneas = registros.Where(r => r.Origen == idTabla).Select(r => r.Llave).ToList(),
                Referencias = registros.Where(r => r.Destino == idTabla).Select(r => r.Llave).ToList(),
                Restricciones = restricciones,
                RelacionesMuchosAMuchos = DetectorRelaciones.BuscarMuchosAMuchos(idTabla, registros, clavesReferenciantes)
            };
        }

        private static readonly HashSet<int> _erroresConexion = [-2, -1, 2, 53, 64, 121, 233, 258, 1231, 10053, 10054, 10060, 10061, 11001];

        private static async Task<SqlConnection> AbrirAsync(
            ConexionActiva conexion,
            string? baseDatos,
            CancellationToken cancelacion)
        {
            for (var intento = 1; ; intento++)
            {
                var conexionSql = new SqlConnection(CadenaConexion(conexion.Conexion, conexion.Contrasena, baseDatos));

                try
                {
                    await conexionSql.OpenAsync(cancelacion);
                    return conexionSql;
                }
                catch (SqlException ex) when (EsErrorDeConexion(ex))
                {
                    SqlConnection.ClearPool(conexionSql);
                    await conexionSql.DisposeAsync();

                    if (intento >= 2)
                    {
                        throw new ConexionPerdidaException(MensajeConexion(conexion.Conexion, ex), ex);
                    }

                    await Task.Delay(500, cancelacion);
                }
                catch
                {
                    await conexionSql.DisposeAsync();
                    throw;
                }
            }
        }

        private static bool EsErrorDeConexion(SqlException ex) =>
            ex.Class >= 20 || _erroresConexion.Contains(ex.Number);

        private static string MensajeConexion(ConexionModelo conexion, SqlException ex) =>
            $"No se pudo conectar con {conexion.Direccion}. Comprueba que el servidor esté encendido; si usas Docker, que Docker Desktop y el contenedor estén en marcha. Detalle: {ex.Message}";

        private static string Delimitar(string nombre) => $"[{nombre.Replace("]", "]]")}]";

        private static ReglaReferencial ConvertirRegla(byte valor) => valor switch
        {
            1 => ReglaReferencial.Cascada,
            2 => ReglaReferencial.EstablecerNulo,
            3 => ReglaReferencial.EstablecerPredeterminado,
            _ => ReglaReferencial.SinAccion
        };

        private static string FormatearTipo(string tipo, short longitud, byte precision, byte escala) => tipo switch
        {
            "nvarchar" or "nchar" => $"{tipo}({(longitud == -1 ? "max" : (longitud / 2).ToString())})",
            "varchar" or "char" or "varbinary" or "binary" => $"{tipo}({(longitud == -1 ? "max" : longitud.ToString())})",
            "decimal" or "numeric" => $"{tipo}({precision}, {escala})",
            "datetime2" or "time" or "datetimeoffset" => escala == 7 ? tipo : $"{tipo}({escala})",
            _ => tipo
        };
    }
}
