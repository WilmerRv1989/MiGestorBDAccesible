using MiGestorBDAccesible.Core.Conexiones;
using MiGestorBDAccesible.Core.Estructura;
using Npgsql;
using System.Data.Common;
using System.Text.RegularExpressions;

namespace MiGestorBDAccesible.Core.Proveedores.PostgreSql
{
    public partial class ProveedorPostgreSql
    {
        private const string ConsultaColumnas = """
            SELECT a.attname, format_type(a.atttypid, a.atttypmod), NOT a.attnotnull, a.attidentity::text,
                   a.attgenerated::text, pg_get_expr(d.adbin, d.adrelid), s.seqstart, s.seqincrement
            FROM pg_attribute a
            LEFT JOIN pg_attrdef d ON d.adrelid = a.attrelid AND d.adnum = a.attnum
            LEFT JOIN pg_depend dep ON dep.refobjid = a.attrelid AND dep.refobjsubid = a.attnum
                                   AND dep.classid = 'pg_class'::regclass AND dep.deptype = 'i'
            LEFT JOIN pg_sequence s ON s.seqrelid = dep.objid
            WHERE a.attrelid = @tabla AND a.attnum > 0 AND NOT a.attisdropped
            ORDER BY a.attnum
            """;

        private const string ConsultaIndices = """
            SELECT i.relname, ix.indisunique, ix.indisprimary, con.contype::text, am.amname,
                   pg_get_indexdef(ix.indexrelid, k.ord, true),
                   k.ord <= ix.indnkeyatts AND (ix.indoption[k.ord - 1] & 1) = 1,
                   k.ord > ix.indnkeyatts
            FROM pg_index ix
            JOIN pg_class i ON i.oid = ix.indexrelid
            JOIN pg_am am ON am.oid = i.relam
            LEFT JOIN pg_constraint con ON con.conindid = ix.indexrelid AND con.conrelid = ix.indrelid AND con.contype IN ('p', 'u')
            CROSS JOIN LATERAL generate_series(1, ix.indnatts) AS k(ord)
            WHERE ix.indrelid = @tabla
            ORDER BY ix.indisprimary DESC, i.relname, k.ord
            """;

        private const string ConsultaLlaves = """
            SELECT c.oid::bigint, c.conname, sn.nspname, st.relname, tn.nspname, tt.relname,
                   sa.attname, ta.attname, c.confdeltype::text, c.confupdtype::text, c.convalidated,
                   c.conrelid::bigint, c.confrelid::bigint
            FROM pg_constraint c
            JOIN pg_class st ON st.oid = c.conrelid
            JOIN pg_namespace sn ON sn.oid = st.relnamespace
            JOIN pg_class tt ON tt.oid = c.confrelid
            JOIN pg_namespace tn ON tn.oid = tt.relnamespace
            CROSS JOIN LATERAL unnest(c.conkey, c.confkey) WITH ORDINALITY AS k(origen, destino, ord)
            JOIN pg_attribute sa ON sa.attrelid = c.conrelid AND sa.attnum = k.origen
            JOIN pg_attribute ta ON ta.attrelid = c.confrelid AND ta.attnum = k.destino
            WHERE c.contype = 'f'
              AND (c.conrelid = @tabla OR c.confrelid = @tabla
                   OR c.conrelid IN (SELECT r.conrelid FROM pg_constraint r WHERE r.contype = 'f' AND r.confrelid = @tabla))
            ORDER BY c.conname, k.ord
            """;

        private const string ConsultaClavesReferenciantes = """
            SELECT c.conrelid::bigint, a.attname
            FROM pg_constraint c
            CROSS JOIN LATERAL unnest(c.conkey) AS k(numero)
            JOIN pg_attribute a ON a.attrelid = c.conrelid AND a.attnum = k.numero
            WHERE c.contype = 'p'
              AND c.conrelid IN (SELECT r.conrelid FROM pg_constraint r WHERE r.contype = 'f' AND r.confrelid = @tabla)
            """;

        private const string ConsultaRestricciones = """
            SELECT c.conname, pg_get_constraintdef(c.oid, true),
                   CASE WHEN cardinality(c.conkey) = 1
                        THEN (SELECT a.attname FROM pg_attribute a WHERE a.attrelid = c.conrelid AND a.attnum = c.conkey[1])
                   END
            FROM pg_constraint c
            WHERE c.conrelid = @tabla AND c.contype = 'c'
            ORDER BY c.conname
            """;

        private static readonly Regex _sinCheck = new(@"^CHECK\s*\((.*)\)(\s+NOT VALID)?$", RegexOptions.Singleline);

        public async Task<DetalleTablaModelo> ObtenerDetalleTablaAsync(
            ConexionActiva conexion,
            string baseDatos,
            TablaModelo tabla,
            CancellationToken cancelacion = default)
        {
            await using var conexionPg = await AbrirAsync(conexion, baseDatos, cancelacion);

            if (await EscalarAsync(conexionPg, "SELECT to_regclass(quote_ident(@esquema) || '.' || quote_ident(@nombre))::oid::bigint", tabla, cancelacion) is not long idTabla)
            {
                throw new InvalidOperationException("La tabla ya no existe en la base de datos.");
            }

            var filas = (long)(await EscalarAsync(conexionPg, $"SELECT count(*) FROM {Delimitar(tabla.Esquema)}.{Delimitar(tabla.Nombre)}", null, cancelacion))!;

            var columnas = await LeerAsync(conexionPg, ConsultaColumnas, idTabla, cancelacion, LeerColumna);

            var indices = new List<IndiceModelo>();
            await LeerAsync(conexionPg, ConsultaIndices, idTabla, cancelacion, lector =>
            {
                var nombre = lector.GetString(0);
                var indice = indices.FirstOrDefault(i => i.Nombre == nombre);

                if (indice is null)
                {
                    var restriccion = lector.IsDBNull(3) ? null : lector.GetString(3);
                    indice = new IndiceModelo
                    {
                        Nombre = nombre,
                        EsUnico = lector.GetBoolean(1),
                        EsClavePrincipal = lector.GetBoolean(2),
                        EsRestriccionUnica = restriccion == "u",
                        Metodo = lector.GetString(4)
                    };
                    indices.Add(indice);
                }

                indice.Columnas.Add(new ColumnaIndiceModelo
                {
                    Nombre = lector.GetString(5).Trim('"'),
                    Descendente = !lector.IsDBNull(6) && lector.GetBoolean(6),
                    Incluida = lector.GetBoolean(7)
                });

                return indice;
            });

            var llaves = new Dictionary<long, (LlaveForaneaModelo Llave, long Origen, long Destino)>();
            await LeerAsync(conexionPg, ConsultaLlaves, idTabla, cancelacion, lector =>
            {
                var id = lector.GetInt64(0);

                if (!llaves.TryGetValue(id, out var registro))
                {
                    registro = (new LlaveForaneaModelo
                    {
                        Nombre = lector.GetString(1),
                        TablaOrigen = new TablaModelo { Esquema = lector.GetString(2), Nombre = lector.GetString(3), EsquemaPredeterminado = "public" },
                        TablaDestino = new TablaModelo { Esquema = lector.GetString(4), Nombre = lector.GetString(5), EsquemaPredeterminado = "public" },
                        AlEliminar = ConvertirRegla(lector.GetString(8)),
                        AlActualizar = ConvertirRegla(lector.GetString(9)),
                        Habilitada = lector.GetBoolean(10)
                    }, lector.GetInt64(11), lector.GetInt64(12));
                    llaves[id] = registro;
                }

                registro.Llave.ColumnasOrigen.Add(lector.GetString(6));
                registro.Llave.ColumnasDestino.Add(lector.GetString(7));
                return id;
            });

            var clavesReferenciantes = new Dictionary<long, HashSet<string>>();
            await LeerAsync(conexionPg, ConsultaClavesReferenciantes, idTabla, cancelacion, lector =>
            {
                var id = lector.GetInt64(0);

                if (!clavesReferenciantes.TryGetValue(id, out var claves))
                {
                    claves = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    clavesReferenciantes[id] = claves;
                }

                claves.Add(lector.GetString(1));
                return id;
            });

            var restricciones = await LeerAsync(conexionPg, ConsultaRestricciones, idTabla, cancelacion, lector =>
            {
                var definicion = lector.GetString(1);
                var coincidencia = _sinCheck.Match(definicion);

                return new RestriccionCheckModelo
                {
                    Nombre = lector.GetString(0),
                    Definicion = coincidencia.Success ? coincidencia.Groups[1].Value : definicion,
                    Columna = lector.IsDBNull(2) ? null : lector.GetString(2)
                };
            });

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
            var identidad = lector.IsDBNull(3) ? "" : lector.GetString(3);
            var generada = lector.IsDBNull(4) ? "" : lector.GetString(4);
            var expresion = lector.IsDBNull(5) ? null : lector.GetString(5);
            var esIdentidad = identidad is "a" or "d";

            return new ColumnaModelo
            {
                Nombre = lector.GetString(0),
                TipoDatos = FormatearTipo(lector.GetString(1)),
                PermiteNulos = lector.GetBoolean(2),
                EsIdentidad = esIdentidad,
                SemillaIdentidad = esIdentidad && !lector.IsDBNull(6) ? lector.GetInt64(6) : null,
                IncrementoIdentidad = esIdentidad && !lector.IsDBNull(7) ? lector.GetInt64(7) : null,
                IdentidadSiempre = esIdentidad ? identidad == "a" : null,
                ValorPredeterminado = esIdentidad || generada.Length > 0 ? null : expresion,
                Formula = generada.Length > 0 ? expresion : null,
                EsPersistente = generada == "s"
            };
        }

        public static string FormatearTipo(string tipo)
        {
            var corto = tipo
                .Replace("character varying", "varchar")
                .Replace("timestamp without time zone", "timestamp")
                .Replace("timestamp with time zone", "timestamptz")
                .Replace("time without time zone", "time")
                .Replace("time with time zone", "timetz");

            corto = Regex.Replace(corto, @"^character(?=\(|\[|$)", "char");
            corto = Regex.Replace(corto, @"^(timestamptz|timestamp|timetz|time)\((\d+)\)", "$1($2)");
            corto = Regex.Replace(corto, @"^timestamp\((\d+)\) with time zone", "timestamptz($1)");
            corto = Regex.Replace(corto, @"^timestamp\((\d+)\) without time zone", "timestamp($1)");
            corto = Regex.Replace(corto, @"^time\((\d+)\) without time zone", "time($1)");

            return Regex.Replace(corto, @"\((\d+),(\d+)\)", "($1, $2)");
        }

        private static ReglaReferencial ConvertirRegla(string codigo) => codigo switch
        {
            "r" => ReglaReferencial.Restringir,
            "c" => ReglaReferencial.Cascada,
            "n" => ReglaReferencial.EstablecerNulo,
            "d" => ReglaReferencial.EstablecerPredeterminado,
            _ => ReglaReferencial.SinAccion
        };

        private static async Task<object?> EscalarAsync(NpgsqlConnection conexion, string consulta, TablaModelo? tabla, CancellationToken cancelacion)
        {
            await using var comando = new NpgsqlCommand(consulta, conexion);

            if (tabla is not null)
            {
                comando.Parameters.AddWithValue("esquema", tabla.Esquema);
                comando.Parameters.AddWithValue("nombre", tabla.Nombre);
            }

            return await comando.ExecuteScalarAsync(cancelacion);
        }

        private static async Task<List<T>> LeerAsync<T>(
            NpgsqlConnection conexion,
            string consulta,
            long idTabla,
            CancellationToken cancelacion,
            Func<DbDataReader, T> leer)
        {
            await using var comando = new NpgsqlCommand(consulta, conexion);
            comando.Parameters.AddWithValue("tabla", NpgsqlTypes.NpgsqlDbType.Oid, (uint)idTabla);
            await using var lector = await comando.ExecuteReaderAsync(cancelacion);
            var elementos = new List<T>();

            while (await lector.ReadAsync(cancelacion))
            {
                elementos.Add(leer(lector));
            }

            return elementos;
        }

        private static string Delimitar(string nombre) => $"\"{nombre.Replace("\"", "\"\"")}\"";
    }
}
