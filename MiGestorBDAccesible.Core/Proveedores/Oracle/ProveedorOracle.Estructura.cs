using MiGestorBDAccesible.Core.Conexiones;
using MiGestorBDAccesible.Core.Estructura;
using Oracle.ManagedDataAccess.Client;
using System.Data.Common;
using System.Text.RegularExpressions;

namespace MiGestorBDAccesible.Core.Proveedores.Oracle
{
    public partial class ProveedorOracle
    {
        private const string ConsultaColumnas = """
            SELECT c.column_name, c.data_type, c.data_length, c.char_length, c.char_used,
                   c.data_precision, c.data_scale, c.nullable, c.virtual_column, c.identity_column,
                   c.data_default, i.generation_type, i.identity_options
            FROM all_tab_cols c
            LEFT JOIN all_tab_identity_cols i
                   ON i.owner = c.owner AND i.table_name = c.table_name AND i.column_name = c.column_name
            WHERE c.owner = :esquema AND c.table_name = :tabla AND c.hidden_column = 'NO'
            ORDER BY c.column_id
            """;

        private const string ConsultaIndices = """
            SELECT i.index_name, i.uniqueness, con.constraint_type,
                   ic.column_name, ic.descend, e.column_expression
            FROM all_indexes i
            JOIN all_ind_columns ic ON ic.index_owner = i.owner AND ic.index_name = i.index_name
            LEFT JOIN all_constraints con
                   ON con.owner = i.table_owner AND con.table_name = i.table_name
                  AND con.index_name = i.index_name AND con.constraint_type IN ('P', 'U')
            LEFT JOIN all_ind_expressions e
                   ON e.index_owner = i.owner AND e.index_name = i.index_name AND e.column_position = ic.column_position
            WHERE i.table_owner = :esquema AND i.table_name = :tabla AND i.index_type <> 'LOB'
            ORDER BY CASE con.constraint_type WHEN 'P' THEN 0 ELSE 1 END, i.index_name, ic.column_position
            """;

        private const string ConsultaReferenciantes = """
            SELECT f.owner, f.table_name
            FROM all_constraints f
            JOIN all_constraints p ON p.owner = f.r_owner AND p.constraint_name = f.r_constraint_name
            WHERE f.constraint_type = 'R' AND p.owner = :esquema AND p.table_name = :tabla
            """;

        private const string ConsultaLlaves = $"""
            SELECT fk.owner, fk.constraint_name, fk.table_name, pk.owner, pk.table_name,
                   fc.column_name, pc.column_name, fk.delete_rule, fk.status
            FROM all_constraints fk
            JOIN all_constraints pk ON pk.owner = fk.r_owner AND pk.constraint_name = fk.r_constraint_name
            JOIN all_cons_columns fc ON fc.owner = fk.owner AND fc.constraint_name = fk.constraint_name
            JOIN all_cons_columns pc ON pc.owner = pk.owner AND pc.constraint_name = pk.constraint_name
                                    AND pc.position = fc.position
            WHERE fk.constraint_type = 'R'
              AND ((fk.owner = :esquema AND fk.table_name = :tabla)
                   OR (pk.owner = :esquema AND pk.table_name = :tabla)
                   OR (fk.owner, fk.table_name) IN ({ConsultaReferenciantes}))
            ORDER BY fk.owner, fk.constraint_name, fc.position
            """;

        private const string ConsultaClavesReferenciantes = $"""
            SELECT c.owner, c.table_name, cc.column_name
            FROM all_constraints c
            JOIN all_cons_columns cc ON cc.owner = c.owner AND cc.constraint_name = c.constraint_name
            WHERE c.constraint_type = 'P'
              AND (c.owner, c.table_name) IN ({ConsultaReferenciantes})
            """;

        private const string ConsultaRestricciones = """
            SELECT c.constraint_name, c.search_condition,
                   (SELECT MIN(x.column_name) FROM all_cons_columns x
                    WHERE x.owner = c.owner AND x.constraint_name = c.constraint_name),
                   (SELECT COUNT(*) FROM all_cons_columns x
                    WHERE x.owner = c.owner AND x.constraint_name = c.constraint_name)
            FROM all_constraints c
            WHERE c.owner = :esquema AND c.table_name = :tabla AND c.constraint_type = 'C'
            ORDER BY c.constraint_name
            """;

        private static readonly Regex _soloNoNulo = new("""^\s*"[^"]+"\s+IS\s+NOT\s+NULL\s*$""", RegexOptions.IgnoreCase);

        private static readonly Regex _opcionesIdentidad = new(@"START WITH: (-?\d+), INCREMENT BY: (-?\d+)");

        public async Task<DetalleTablaModelo> ObtenerDetalleTablaAsync(
            ConexionActiva conexion,
            string baseDatos,
            TablaModelo tabla,
            CancellationToken cancelacion = default)
        {
            await using var conexionOracle = await AbrirAsync(conexion, cancelacion);

            var existe = await EscalarAsync(conexionOracle,
                "SELECT COUNT(*) FROM all_tables WHERE owner = :esquema AND table_name = :tabla", tabla, cancelacion);

            if (existe == 0)
            {
                throw new InvalidOperationException("La tabla ya no existe en el esquema.");
            }

            var filas = await EscalarAsync(conexionOracle,
                $"SELECT COUNT(*) FROM {Delimitar(tabla.Esquema)}.{Delimitar(tabla.Nombre)}", null, cancelacion);

            var columnas = await LeerAsync(conexionOracle, ConsultaColumnas, tabla, cancelacion, LeerColumna);

            var indices = new List<IndiceModelo>();
            await LeerAsync(conexionOracle, ConsultaIndices, tabla, cancelacion, lector =>
            {
                var nombre = lector.GetString(0);
                var indice = indices.FirstOrDefault(i => i.Nombre == nombre);

                if (indice is null)
                {
                    var restriccion = lector.IsDBNull(2) ? null : lector.GetString(2);
                    indice = new IndiceModelo
                    {
                        Nombre = nombre,
                        EsUnico = lector.GetString(1) == "UNIQUE",
                        EsClavePrincipal = restriccion == "P",
                        EsRestriccionUnica = restriccion == "U"
                    };
                    indices.Add(indice);
                }

                var expresion = lector.IsDBNull(5) ? null : lector.GetString(5);
                indice.Columnas.Add(new ColumnaIndiceModelo
                {
                    Nombre = expresion is null ? lector.GetString(3) : expresion.Replace("\"", "").Trim(),
                    Descendente = lector.GetString(4) == "DESC"
                });

                return indice;
            });

            var llaves = new Dictionary<string, (LlaveForaneaModelo Llave, string Origen, string Destino)>();
            await LeerAsync(conexionOracle, ConsultaLlaves, tabla, cancelacion, lector =>
            {
                var id = $"{lector.GetString(0)}.{lector.GetString(1)}";

                if (!llaves.TryGetValue(id, out var registro))
                {
                    var origen = new TablaModelo { Esquema = lector.GetString(0), Nombre = lector.GetString(2), EsquemaPredeterminado = tabla.Esquema };
                    var destino = new TablaModelo { Esquema = lector.GetString(3), Nombre = lector.GetString(4), EsquemaPredeterminado = tabla.Esquema };

                    registro = (new LlaveForaneaModelo
                    {
                        Nombre = lector.GetString(1),
                        TablaOrigen = origen,
                        TablaDestino = destino,
                        AlEliminar = lector.GetString(7) switch
                        {
                            "CASCADE" => ReglaReferencial.Cascada,
                            "SET NULL" => ReglaReferencial.EstablecerNulo,
                            _ => ReglaReferencial.SinAccion
                        },
                        AlActualizar = ReglaReferencial.SinAccion,
                        Habilitada = lector.GetString(8) == "ENABLED"
                    }, origen.NombreCompleto, destino.NombreCompleto);
                    llaves[id] = registro;
                }

                registro.Llave.ColumnasOrigen.Add(lector.GetString(5));
                registro.Llave.ColumnasDestino.Add(lector.GetString(6));
                return id;
            });

            var clavesReferenciantes = new Dictionary<string, HashSet<string>>();
            await LeerAsync(conexionOracle, ConsultaClavesReferenciantes, tabla, cancelacion, lector =>
            {
                var id = $"{lector.GetString(0)}.{lector.GetString(1)}";

                if (!clavesReferenciantes.TryGetValue(id, out var claves))
                {
                    claves = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    clavesReferenciantes[id] = claves;
                }

                claves.Add(lector.GetString(2));
                return id;
            });

            var restricciones = (await LeerAsync(conexionOracle, ConsultaRestricciones, tabla, cancelacion, lector =>
                {
                    var definicion = lector.IsDBNull(1) ? "" : lector.GetString(1).Trim();

                    return _soloNoNulo.IsMatch(definicion) ? null : new RestriccionCheckModelo
                    {
                        Nombre = lector.GetString(0),
                        Definicion = definicion,
                        Columna = Convert.ToInt32(lector.GetValue(3)) == 1 && !lector.IsDBNull(2) ? lector.GetString(2) : null
                    };
                }))
                .OfType<RestriccionCheckModelo>()
                .ToList();

            var idTabla = tabla.NombreCompleto;
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

        private static ColumnaModelo LeerColumna(DbDataReader lector)
        {
            var esIdentidad = lector.GetString(9) == "YES";
            var esVirtual = lector.GetString(8) == "YES";
            var expresion = lector.IsDBNull(10) ? null : lector.GetString(10).Trim();
            var opciones = lector.IsDBNull(12) ? null : _opcionesIdentidad.Match(lector.GetString(12));

            return new ColumnaModelo
            {
                Nombre = lector.GetString(0),
                TipoDatos = FormatearTipo(lector.GetString(1), Entero(lector, 2), Entero(lector, 3),
                    lector.IsDBNull(4) ? null : lector.GetString(4), Entero(lector, 5), Entero(lector, 6)),
                PermiteNulos = lector.GetString(7) == "Y",
                EsIdentidad = esIdentidad,
                SemillaIdentidad = opciones is { Success: true } ? long.Parse(opciones.Groups[1].Value) : null,
                IncrementoIdentidad = opciones is { Success: true } ? long.Parse(opciones.Groups[2].Value) : null,
                IdentidadSiempre = esIdentidad && !lector.IsDBNull(11) ? lector.GetString(11) == "ALWAYS" : null,
                ValorPredeterminado = esIdentidad || esVirtual || string.IsNullOrEmpty(expresion) ? null : expresion,
                Formula = esVirtual ? expresion : null
            };
        }

        public static string FormatearTipo(string tipo, int? longitud, int? caracteres, string? unidad, int? precision, int? escala) => tipo switch
        {
            "VARCHAR2" or "CHAR" => unidad == "C" ? $"{tipo}({caracteres} CHAR)" : $"{tipo}({caracteres ?? longitud})",
            "NVARCHAR2" or "NCHAR" => $"{tipo}({caracteres})",
            "RAW" => $"RAW({longitud})",
            "FLOAT" => precision is null ? "FLOAT" : $"FLOAT({precision})",
            "NUMBER" => (precision, escala) switch
            {
                (null, null) => "NUMBER",
                (null, 0) => "NUMBER(38, 0)",
                (null, _) => $"NUMBER(*, {escala})",
                (_, null or 0) => $"NUMBER({precision})",
                _ => $"NUMBER({precision}, {escala})"
            },
            _ => tipo
        };

        private static int? Entero(DbDataReader lector, int indice) =>
            lector.IsDBNull(indice) ? null : Convert.ToInt32(lector.GetValue(indice));

        private static async Task<long> EscalarAsync(OracleConnection conexion, string consulta, TablaModelo? tabla, CancellationToken cancelacion)
        {
            await using var comando = Comando(conexion, consulta, tabla);
            return Convert.ToInt64(await comando.ExecuteScalarAsync(cancelacion));
        }

        private static async Task<List<T>> LeerAsync<T>(
            OracleConnection conexion,
            string consulta,
            TablaModelo tabla,
            CancellationToken cancelacion,
            Func<DbDataReader, T> leer)
        {
            await using var comando = Comando(conexion, consulta, tabla);
            await using var lector = await comando.ExecuteReaderAsync(cancelacion);
            var elementos = new List<T>();

            while (await lector.ReadAsync(cancelacion))
            {
                elementos.Add(leer(lector));
            }

            return elementos;
        }

        private static OracleCommand Comando(OracleConnection conexion, string consulta, TablaModelo? tabla)
        {
            var comando = conexion.CreateCommand();
            comando.CommandText = consulta;
            comando.BindByName = true;
            comando.InitialLONGFetchSize = -1;

            if (tabla is not null)
            {
                comando.Parameters.Add("esquema", tabla.Esquema);
                comando.Parameters.Add("tabla", tabla.Nombre);
            }

            return comando;
        }

        private static string Delimitar(string nombre) => $"\"{nombre.Replace("\"", "\"\"")}\"";
    }
}
