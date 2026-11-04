using MiGestorBDAccesible.Core.Conexiones;
using MiGestorBDAccesible.Core.Estructura;
using MySqlConnector;
using System.Data.Common;
using System.Globalization;
using System.Text.RegularExpressions;

namespace MiGestorBDAccesible.Core.Proveedores.MySql
{
    public abstract partial class ProveedorMySqlBase
    {
        private const string ConsultaColumnas = """
            SELECT column_name, column_type, is_nullable = 'YES', extra, column_default, generation_expression
            FROM information_schema.columns
            WHERE table_schema = @base AND table_name = @tabla
            ORDER BY ordinal_position
            """;

        private const string ConsultaIndices = """
            SELECT s.index_name, s.non_unique = 0, s.column_name, COALESCE(s.collation = 'D', 0), s.index_type, s.sub_part,
                   EXISTS (SELECT 1 FROM information_schema.table_constraints tc
                           WHERE tc.table_schema = s.table_schema AND tc.table_name = s.table_name
                             AND tc.constraint_name = s.index_name AND tc.constraint_type = 'UNIQUE')
            FROM information_schema.statistics s
            WHERE s.table_schema = @base AND s.table_name = @tabla
            ORDER BY s.index_name = 'PRIMARY' DESC, s.index_name, s.seq_in_index
            """;

        private const string ConsultaLlaves = """
            SELECT rc.constraint_name, k.table_name, k.referenced_table_schema, k.referenced_table_name,
                   k.column_name, k.referenced_column_name, rc.delete_rule, rc.update_rule
            FROM information_schema.referential_constraints rc
            JOIN information_schema.key_column_usage k
              ON k.constraint_schema = rc.constraint_schema AND k.constraint_name = rc.constraint_name AND k.table_name = rc.table_name
            WHERE rc.constraint_schema = @base
              AND (rc.table_name = @tabla OR rc.referenced_table_name = @tabla
                   OR rc.table_name IN (SELECT r.table_name FROM information_schema.referential_constraints r
                                        WHERE r.constraint_schema = @base AND r.referenced_table_name = @tabla))
            ORDER BY rc.table_name, rc.constraint_name, k.ordinal_position
            """;

        private const string ConsultaClavesReferenciantes = """
            SELECT s.table_name, s.column_name
            FROM information_schema.statistics s
            WHERE s.table_schema = @base AND s.index_name = 'PRIMARY'
              AND s.table_name IN (SELECT r.table_name FROM information_schema.referential_constraints r
                                   WHERE r.constraint_schema = @base AND r.referenced_table_name = @tabla)
            """;

        protected abstract string ConsultaRestricciones { get; }

        private static readonly Regex _autoIncremento = new(@"\bAUTO_INCREMENT=(\d+)", RegexOptions.IgnoreCase);

        private static readonly Regex _almacenamiento = new(@"\bENGINE=(\w+)", RegexOptions.IgnoreCase);

        private static readonly Regex _identificador = new(@"`((?:[^`]|``)+)`");

        public async Task<DetalleTablaModelo> ObtenerDetalleTablaAsync(
            ConexionActiva conexion,
            string baseDatos,
            TablaModelo tabla,
            CancellationToken cancelacion = default)
        {
            await using var conexionMy = await AbrirAsync(conexion, cancelacion);
            var nombreCompleto = $"{Delimitar(baseDatos)}.{Delimitar(tabla.Nombre)}";

            string creacion;

            try
            {
                await using var comando = new MySqlCommand($"SHOW CREATE TABLE {nombreCompleto}", conexionMy);
                await using var lector = await comando.ExecuteReaderAsync(cancelacion);
                await lector.ReadAsync(cancelacion);
                creacion = lector.GetString(1);
            }
            catch (MySqlException ex) when (ex.ErrorCode == MySqlErrorCode.NoSuchTable)
            {
                throw new InvalidOperationException("La tabla ya no existe en la base de datos.");
            }

            var siguiente = _autoIncremento.Match(creacion) is { Success: true } auto ? long.Parse(auto.Groups[1].Value, CultureInfo.InvariantCulture) : (long?)null;
            var almacenamiento = _almacenamiento.Match(creacion) is { Success: true } motor ? motor.Groups[1].Value : null;
            var incremento = Convert.ToInt64(await EscalarAsync(conexionMy, "SELECT @@auto_increment_increment", cancelacion), CultureInfo.InvariantCulture);
            var filas = Convert.ToInt64(await EscalarAsync(conexionMy, $"SELECT COUNT(*) FROM {nombreCompleto}", cancelacion), CultureInfo.InvariantCulture);

            var columnas = await LeerAsync(conexionMy, ConsultaColumnas, baseDatos, tabla.Nombre, cancelacion, lector => LeerColumna(lector, siguiente ?? 1, incremento));

            var indices = new List<IndiceModelo>();
            await LeerAsync(conexionMy, ConsultaIndices, baseDatos, tabla.Nombre, cancelacion, lector =>
            {
                var nombre = lector.GetString(0);
                var indice = indices.FirstOrDefault(i => i.Nombre == nombre);

                if (indice is null)
                {
                    var metodo = lector.GetString(4);
                    indice = new IndiceModelo
                    {
                        Nombre = nombre,
                        EsUnico = Convert.ToBoolean(lector.GetValue(1)),
                        EsClavePrincipal = nombre == "PRIMARY",
                        EsRestriccionUnica = nombre != "PRIMARY" && Convert.ToBoolean(lector.GetValue(6)),
                        Metodo = metodo.Equals("BTREE", StringComparison.OrdinalIgnoreCase) ? null : metodo.ToLowerInvariant()
                    };
                    indices.Add(indice);
                }

                var columna = lector.IsDBNull(2) ? "(expresión)" : lector.GetString(2);
                indice.Columnas.Add(new ColumnaIndiceModelo
                {
                    Nombre = lector.IsDBNull(5) ? columna : $"{columna} (los primeros {lector.GetValue(5)} caracteres)",
                    Descendente = Convert.ToBoolean(lector.GetValue(3))
                });

                return indice;
            });

            var llaves = new Dictionary<(string Tabla, string Nombre), (LlaveForaneaModelo Llave, string Origen, string Destino)>();
            await LeerAsync(conexionMy, ConsultaLlaves, baseDatos, tabla.Nombre, cancelacion, lector =>
            {
                var clave = (lector.GetString(1), lector.GetString(0));

                if (!llaves.TryGetValue(clave, out var registro))
                {
                    var esquemaDestino = lector.GetString(2);
                    registro = (new LlaveForaneaModelo
                    {
                        Nombre = lector.GetString(0),
                        TablaOrigen = new TablaModelo { Esquema = "", Nombre = lector.GetString(1), EsquemaPredeterminado = "" },
                        TablaDestino = new TablaModelo
                        {
                            Esquema = esquemaDestino.Equals(baseDatos, StringComparison.OrdinalIgnoreCase) ? "" : esquemaDestino,
                            Nombre = lector.GetString(3),
                            EsquemaPredeterminado = ""
                        },
                        AlEliminar = ConvertirRegla(lector.GetString(6)),
                        AlActualizar = ConvertirRegla(lector.GetString(7))
                    }, lector.GetString(1), lector.GetString(3));
                    llaves[clave] = registro;
                }

                registro.Llave.ColumnasOrigen.Add(lector.GetString(4));
                registro.Llave.ColumnasDestino.Add(lector.GetString(5));
                return clave;
            });

            var clavesReferenciantes = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            await LeerAsync(conexionMy, ConsultaClavesReferenciantes, baseDatos, tabla.Nombre, cancelacion, lector =>
            {
                var nombre = lector.GetString(0);

                if (!clavesReferenciantes.TryGetValue(nombre, out var claves))
                {
                    claves = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    clavesReferenciantes[nombre] = claves;
                }

                claves.Add(lector.GetString(1));
                return nombre;
            });

            var restricciones = new List<RestriccionCheckModelo>();

            try
            {
                restricciones = await LeerAsync(conexionMy, ConsultaRestricciones, baseDatos, tabla.Nombre, cancelacion, lector =>
                    LeerRestriccion(lector.GetString(0), lector.GetString(1), columnas));
            }
            catch (MySqlException ex) when (ex.ErrorCode == MySqlErrorCode.UnknownTable)
            {
            }

            var registros = llaves.Values.ToList();

            return new DetalleTablaModelo
            {
                Tabla = tabla,
                Filas = filas,
                Almacenamiento = almacenamiento,
                Columnas = columnas,
                Indices = indices,
                LlavesForaneas = registros.Where(r => r.Origen.Equals(tabla.Nombre, StringComparison.OrdinalIgnoreCase)).Select(r => r.Llave).ToList(),
                Referencias = registros.Where(r => r.Destino.Equals(tabla.Nombre, StringComparison.OrdinalIgnoreCase) && r.Llave.TablaDestino.Esquema == "").Select(r => r.Llave).ToList(),
                Restricciones = restricciones,
                RelacionesMuchosAMuchos = DetectorRelaciones.BuscarMuchosAMuchos(
                    tabla.Nombre.ToLowerInvariant(),
                    registros.Select(r => (r.Llave, r.Origen.ToLowerInvariant(), r.Destino.ToLowerInvariant())).ToList(),
                    clavesReferenciantes.ToDictionary(p => p.Key.ToLowerInvariant(), p => p.Value))
            };
        }

        public static ColumnaModelo LeerColumna(DbDataReader lector, long siguiente, long incremento)
        {
            var extra = lector.IsDBNull(3) ? "" : lector.GetString(3);
            var predeterminado = lector.IsDBNull(4) ? null : lector.GetString(4);
            var expresion = lector.IsDBNull(5) ? null : lector.GetString(5);
            var esIdentidad = extra.Contains("auto_increment", StringComparison.OrdinalIgnoreCase);
            var esCalculada = extra.Contains("GENERATED", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(expresion);

            return new ColumnaModelo
            {
                Nombre = lector.GetString(0),
                TipoDatos = FormatearTipo(lector.GetString(1)),
                PermiteNulos = Convert.ToBoolean(lector.GetValue(2)),
                EsIdentidad = esIdentidad,
                IncrementoIdentidad = esIdentidad ? incremento : null,
                SiguienteIdentidad = esIdentidad ? siguiente : null,
                ValorPredeterminado = esIdentidad || esCalculada ? null : FormatearPredeterminado(predeterminado, extra, lector.GetString(1)),
                Formula = esCalculada ? QuitarDelimitadores(QuitarParentesisExternos(expresion!.Trim())) : null,
                EsPersistente = esCalculada && (extra.Contains("STORED", StringComparison.OrdinalIgnoreCase) || extra.Contains("PERSISTENT", StringComparison.OrdinalIgnoreCase))
            };
        }

        public static string FormatearTipo(string tipo)
        {
            var corto = Regex.Replace(tipo, @"^(bigint|int|mediumint|smallint)\(\d+\)", "$1");
            corto = Regex.Replace(corto, @"^tinyint\((?!1\))\d+\)", "tinyint");
            return Regex.Replace(corto, @"\((\d+),(\d+)\)", "($1, $2)");
        }

        public static string? FormatearPredeterminado(string? valor, string extra, string tipo)
        {
            if (valor is null || valor.Equals("NULL", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            if (extra.Contains("DEFAULT_GENERATED", StringComparison.OrdinalIgnoreCase)
                || valor.StartsWith('\'')
                || Regex.IsMatch(valor, @"^-?\d+(\.\d+)?$")
                || Regex.IsMatch(valor, @"^\w+\(.*\)$")
                || valor.Equals("CURRENT_TIMESTAMP", StringComparison.OrdinalIgnoreCase))
            {
                return valor;
            }

            return $"'{valor.Replace("'", "''")}'";
        }

        public static RestriccionCheckModelo LeerRestriccion(string nombre, string definicion, List<ColumnaModelo> columnas)
        {
            var texto = QuitarDelimitadores(QuitarParentesisExternos(definicion.Trim()));
            var usadas = _identificador.Matches(definicion).Select(m => m.Groups[1].Value.Replace("``", "`"))
                .Concat(columnas.Select(c => c.Nombre).Where(c => Regex.IsMatch(definicion, $@"(?<![\w`]){Regex.Escape(c)}(?![\w`])", RegexOptions.IgnoreCase)))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(c => columnas.Any(col => col.Nombre.Equals(c, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            return new RestriccionCheckModelo { Nombre = nombre, Definicion = texto, Columna = usadas.Count == 1 ? usadas[0] : null };
        }

        public static string QuitarDelimitadores(string texto) => _identificador.Replace(texto, m => m.Groups[1].Value.Replace("``", "`"));

        private static string QuitarParentesisExternos(string texto)
        {
            while (texto.Length >= 2 && texto[0] == '(' && texto[^1] == ')')
            {
                var nivel = 0;
                var envuelve = true;

                for (var i = 0; i < texto.Length - 1; i++)
                {
                    nivel += texto[i] == '(' ? 1 : texto[i] == ')' ? -1 : 0;

                    if (nivel == 0)
                    {
                        envuelve = false;
                        break;
                    }
                }

                if (!envuelve)
                {
                    break;
                }

                texto = texto[1..^1].Trim();
            }

            return texto;
        }

        private static ReglaReferencial ConvertirRegla(string regla) => regla.ToUpperInvariant() switch
        {
            "RESTRICT" => ReglaReferencial.Restringir,
            "CASCADE" => ReglaReferencial.Cascada,
            "SET NULL" => ReglaReferencial.EstablecerNulo,
            "SET DEFAULT" => ReglaReferencial.EstablecerPredeterminado,
            _ => ReglaReferencial.SinAccion
        };

        private static async Task<object?> EscalarAsync(MySqlConnection conexion, string consulta, CancellationToken cancelacion)
        {
            await using var comando = new MySqlCommand(consulta, conexion);
            return await comando.ExecuteScalarAsync(cancelacion);
        }

        private static async Task<List<T>> LeerAsync<T>(
            MySqlConnection conexion,
            string consulta,
            string baseDatos,
            string tabla,
            CancellationToken cancelacion,
            Func<DbDataReader, T> leer)
        {
            await using var comando = new MySqlCommand(consulta, conexion);
            comando.Parameters.AddWithValue("@base", baseDatos);
            comando.Parameters.AddWithValue("@tabla", tabla);
            await using var lector = await comando.ExecuteReaderAsync(cancelacion);
            var elementos = new List<T>();

            while (await lector.ReadAsync(cancelacion))
            {
                elementos.Add(leer(lector));
            }

            return elementos;
        }

        public static string Delimitar(string nombre) => $"`{nombre.Replace("`", "``")}`";
    }
}
