using MiGestorBDAccesible.Core.Conexiones;
using MiGestorBDAccesible.Core.Estructura;
using Microsoft.Data.SqlClient;
using System.Text;
using System.Text.RegularExpressions;

namespace MiGestorBDAccesible.Core.Proveedores.SqlServer
{
    public partial class ProveedorSqlServer
    {
        private const string ConsultaContarObjetos = """
            SELECT
                (SELECT COUNT(*) FROM sys.views WHERE is_ms_shipped = 0),
                (SELECT COUNT(*) FROM sys.procedures WHERE is_ms_shipped = 0),
                (SELECT COUNT(*) FROM sys.objects WHERE type IN ('FN', 'IF', 'TF', 'FS', 'FT') AND is_ms_shipped = 0),
                (SELECT COUNT(*) FROM sys.sequences),
                (SELECT COUNT(*) FROM sys.triggers WHERE parent_class = 0 AND is_ms_shipped = 0)
            """;

        private static readonly Regex _inicioCreate = new(
            @"^(?<antes>(\s|--[^\n]*\n|/\*.*?\*/)*)CREATE(?!\s+OR\s+ALTER\b)\s+",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        private static readonly HashSet<string> _tiposTexto = new(StringComparer.OrdinalIgnoreCase)
        {
            "char", "varchar", "text", "nchar", "nvarchar", "ntext", "date", "datetime", "datetime2",
            "smalldatetime", "datetimeoffset", "time", "uniqueidentifier", "xml", "sql_variant"
        };

        public async Task<Dictionary<TipoObjeto, int>> ContarObjetosAsync(
            ConexionActiva conexion,
            string baseDatos,
            CancellationToken cancelacion = default)
        {
            await using var conexionSql = await AbrirAsync(conexion, baseDatos, cancelacion);
            await using var comando = new SqlCommand(ConsultaContarObjetos, conexionSql);
            await using var lector = await comando.ExecuteReaderAsync(cancelacion);
            await lector.ReadAsync(cancelacion);

            return new Dictionary<TipoObjeto, int>
            {
                [TipoObjeto.Vista] = lector.GetInt32(0),
                [TipoObjeto.Procedimiento] = lector.GetInt32(1),
                [TipoObjeto.Funcion] = lector.GetInt32(2),
                [TipoObjeto.Secuencia] = lector.GetInt32(3),
                [TipoObjeto.Disparador] = lector.GetInt32(4)
            };
        }

        public async Task<List<ObjetoBaseDatosModelo>> ObtenerObjetosAsync(
            ConexionActiva conexion,
            string baseDatos,
            TipoObjeto tipo,
            CancellationToken cancelacion = default)
        {
            var consulta = tipo switch
            {
                TipoObjeto.Vista => "SELECT SCHEMA_NAME(schema_id), name, object_id FROM sys.views WHERE is_ms_shipped = 0",
                TipoObjeto.Procedimiento => "SELECT SCHEMA_NAME(schema_id), name, object_id FROM sys.procedures WHERE is_ms_shipped = 0",
                TipoObjeto.Funcion => "SELECT SCHEMA_NAME(schema_id), name, object_id FROM sys.objects WHERE type IN ('FN', 'IF', 'TF', 'FS', 'FT') AND is_ms_shipped = 0",
                TipoObjeto.Secuencia => "SELECT SCHEMA_NAME(schema_id), name, object_id FROM sys.sequences",
                TipoObjeto.Disparador => "SELECT N'', name, object_id FROM sys.triggers WHERE parent_class = 0 AND is_ms_shipped = 0",
                _ => null
            };

            if (consulta is null)
            {
                return [];
            }

            await using var conexionSql = await AbrirAsync(conexion, baseDatos, cancelacion);
            await using var comando = new SqlCommand(consulta + " ORDER BY 1, 2", conexionSql);
            await using var lector = await comando.ExecuteReaderAsync(cancelacion);
            var objetos = new List<ObjetoBaseDatosModelo>();

            while (await lector.ReadAsync(cancelacion))
            {
                objetos.Add(new ObjetoBaseDatosModelo
                {
                    Tipo = tipo,
                    Esquema = lector.GetString(0),
                    Nombre = lector.GetString(1),
                    Id = lector.GetInt32(2)
                });
            }

            return objetos;
        }

        public async Task<List<ObjetoBaseDatosModelo>> ObtenerDisparadoresTablaAsync(
            ConexionActiva conexion,
            string baseDatos,
            TablaModelo tabla,
            CancellationToken cancelacion = default)
        {
            await using var conexionSql = await AbrirAsync(conexion, baseDatos, cancelacion);
            await using var comando = new SqlCommand("""
                SELECT OBJECT_SCHEMA_NAME(object_id), name, object_id
                FROM sys.triggers
                WHERE parent_id = OBJECT_ID(@nombre) AND parent_class = 1
                ORDER BY name
                """, conexionSql);
            comando.Parameters.AddWithValue("@nombre", NombreCompleto(tabla));

            await using var lector = await comando.ExecuteReaderAsync(cancelacion);
            var disparadores = new List<ObjetoBaseDatosModelo>();

            while (await lector.ReadAsync(cancelacion))
            {
                disparadores.Add(new ObjetoBaseDatosModelo
                {
                    Tipo = TipoObjeto.Disparador,
                    Esquema = lector.GetString(0),
                    Nombre = lector.GetString(1),
                    Id = lector.GetInt32(2),
                    Tabla = tabla
                });
            }

            return disparadores;
        }

        public async Task<DetalleObjetoModelo> ObtenerDetalleObjetoAsync(
            ConexionActiva conexion,
            string baseDatos,
            ObjetoBaseDatosModelo objeto,
            CancellationToken cancelacion = default)
        {
            await using var conexionSql = await AbrirAsync(conexion, baseDatos, cancelacion);
            var id = await IdObjetoAsync(conexionSql, objeto, cancelacion);

            return objeto.Tipo switch
            {
                TipoObjeto.Vista => new DetalleObjetoModelo { Objeto = objeto, Columnas = await LeerColumnasObjetoAsync(conexionSql, id, cancelacion) },
                TipoObjeto.Procedimiento or TipoObjeto.Funcion => await LeerRutinaAsync(conexionSql, objeto, id, cancelacion),
                TipoObjeto.Disparador => await LeerDisparadorAsync(conexionSql, objeto, id, cancelacion),
                TipoObjeto.Secuencia => await LeerSecuenciaAsync(conexionSql, objeto, id, cancelacion),
                _ => new DetalleObjetoModelo { Objeto = objeto }
            };
        }

        public async Task<string> GenerarScriptObjetoAsync(
            ConexionActiva conexion,
            string baseDatos,
            ObjetoBaseDatosModelo objeto,
            CancellationToken cancelacion = default)
        {
            var script = new StringBuilder();
            script.AppendLine($"USE {Delimitar(baseDatos)};");
            script.AppendLine("GO");
            script.AppendLine();

            if (objeto.Tipo == TipoObjeto.Secuencia)
            {
                var detalle = await ObtenerDetalleObjetoAsync(conexion, baseDatos, objeto, cancelacion);
                script.AppendLine($"CREATE SEQUENCE {NombreObjeto(objeto)}");
                script.AppendLine($"    AS {detalle.Retorno}");
                script.AppendLine($"    START WITH {detalle.Inicio}");
                script.AppendLine($"    INCREMENT BY {detalle.Incremento}");
                script.AppendLine($"    MINVALUE {detalle.Minimo}");
                script.AppendLine($"    MAXVALUE {detalle.Maximo}");
                script.AppendLine(detalle.Ciclo == true ? "    CYCLE;" : "    NO CYCLE;");
                return script.ToString();
            }

            await using var conexionSql = await AbrirAsync(conexion, baseDatos, cancelacion);
            var id = await IdObjetoAsync(conexionSql, objeto, cancelacion);
            await using var comando = new SqlCommand("SELECT OBJECT_DEFINITION(@id)", conexionSql);
            comando.Parameters.AddWithValue("@id", id);

            if (await comando.ExecuteScalarAsync(cancelacion) is not string definicion)
            {
                throw new InvalidOperationException("El objeto está cifrado (WITH ENCRYPTION) o no tienes permiso para ver su definición (VIEW DEFINITION).");
            }

            script.AppendLine(_inicioCreate.Replace(definicion.Trim(), "${antes}CREATE OR ALTER ", 1));
            script.AppendLine("GO");
            return script.ToString();
        }

        public string ScriptEliminarObjeto(ObjetoBaseDatosModelo objeto) => objeto.Tipo switch
        {
            TipoObjeto.Vista => $"DROP VIEW {NombreObjeto(objeto)};",
            TipoObjeto.Procedimiento => $"DROP PROCEDURE {NombreObjeto(objeto)};",
            TipoObjeto.Funcion => $"DROP FUNCTION {NombreObjeto(objeto)};",
            TipoObjeto.Secuencia => $"DROP SEQUENCE {NombreObjeto(objeto)};",
            TipoObjeto.Disparador when objeto.Tabla is null => $"DROP TRIGGER {Delimitar(objeto.Nombre)} ON DATABASE;",
            _ => $"DROP TRIGGER {NombreObjeto(objeto)};"
        };

        public string ScriptHabilitarDisparador(ObjetoBaseDatosModelo disparador, bool habilitar) =>
            disparador.Tabla is null
                ? $"{(habilitar ? "ENABLE" : "DISABLE")} TRIGGER {Delimitar(disparador.Nombre)} ON DATABASE;"
                : $"{(habilitar ? "ENABLE" : "DISABLE")} TRIGGER {NombreObjeto(disparador)} ON {NombreCompleto(disparador.Tabla)};";

        public string ScriptReiniciarSecuencia(ObjetoBaseDatosModelo secuencia, long valor) =>
            $"ALTER SEQUENCE {NombreObjeto(secuencia)} RESTART WITH {valor};";

        public string ScriptLlamada(DetalleObjetoModelo detalle, IReadOnlyDictionary<string, string?> valores)
        {
            var objeto = detalle.Objeto;
            var argumentos = detalle.Parametros.Select(p => LiteralParametro(p, valores)).ToList();

            if (objeto.Tipo == TipoObjeto.Funcion)
            {
                return detalle.DevuelveTabla
                    ? $"SELECT *{Environment.NewLine}FROM {NombreObjeto(objeto)}({string.Join(", ", argumentos)});"
                    : $"SELECT {NombreObjeto(objeto)}({string.Join(", ", argumentos)}) AS resultado;";
            }

            var script = new StringBuilder();
            var salidas = detalle.Parametros.Where(p => p.Direccion != DireccionParametro.Entrada).ToList();

            foreach (var salida in salidas)
            {
                var valor = salida.Direccion == DireccionParametro.EntradaSalida ? $" = {LiteralParametro(salida, valores)}" : "";
                script.AppendLine($"DECLARE {salida.Nombre} {salida.TipoDatos}{valor};");
            }

            if (salidas.Count > 0)
            {
                script.AppendLine();
            }

            var asignaciones = detalle.Parametros.Select(p => p.Direccion == DireccionParametro.Entrada
                ? $"{p.Nombre} = {LiteralParametro(p, valores)}"
                : $"{p.Nombre} = {p.Nombre} OUTPUT");

            script.Append($"EXEC {NombreObjeto(objeto)}");
            script.AppendLine(detalle.Parametros.Count == 0
                ? ";"
                : $"{Environment.NewLine}    {string.Join($",{Environment.NewLine}    ", asignaciones)};");

            foreach (var salida in salidas)
            {
                script.AppendLine($"PRINT N'{salida.Nombre} = ' + ISNULL(CONVERT(nvarchar(max), {salida.Nombre}), N'NULL');");
            }

            return script.ToString().TrimEnd();
        }

        public string ScriptSeleccionarPrimeras(DetalleObjetoModelo vista) =>
            $"""
            SELECT TOP (100)
            {string.Join($",{Environment.NewLine}", vista.Columnas.Select(c => $"    {Delimitar(c.Nombre)}"))}
            FROM {NombreObjeto(vista.Objeto)};
            """;

        private static string NombreObjeto(ObjetoBaseDatosModelo objeto) =>
            objeto.Esquema == "" ? Delimitar(objeto.Nombre) : $"{Delimitar(objeto.Esquema)}.{Delimitar(objeto.Nombre)}";

        private static string LiteralParametro(ParametroModelo parametro, IReadOnlyDictionary<string, string?> valores)
        {
            var valor = valores.TryGetValue(parametro.Nombre, out var escrito) ? escrito?.Trim() : null;

            if (string.IsNullOrEmpty(valor) || valor.Equals("NULL", StringComparison.OrdinalIgnoreCase))
            {
                return "NULL";
            }

            if (valor.Equals("DEFAULT", StringComparison.OrdinalIgnoreCase))
            {
                return "DEFAULT";
            }

            var tipo = parametro.TipoDatos.Split('(')[0];

            if (tipo is "bit")
            {
                return valor.ToLowerInvariant() switch
                {
                    "sí" or "si" or "true" or "verdadero" => "1",
                    "no" or "false" or "falso" => "0",
                    _ => valor
                };
            }

            if (!_tiposTexto.Contains(tipo))
            {
                return valor;
            }

            var prefijo = tipo.StartsWith('n') || tipo is "xml" or "sql_variant" ? "N" : "";
            return $"{prefijo}'{valor.Replace("'", "''")}'";
        }

        private static async Task<int> IdObjetoAsync(SqlConnection conexion, ObjetoBaseDatosModelo objeto, CancellationToken cancelacion)
        {
            var consulta = objeto.Tipo switch
            {
                TipoObjeto.Disparador when objeto.Tabla is null => "SELECT object_id FROM sys.triggers WHERE parent_class = 0 AND name = @nombre",
                TipoObjeto.Secuencia => "SELECT object_id FROM sys.sequences WHERE schema_id = SCHEMA_ID(@esquema) AND name = @nombre",
                _ => "SELECT OBJECT_ID(QUOTENAME(@esquema) + N'.' + QUOTENAME(@nombre))"
            };

            await using var comando = new SqlCommand(consulta, conexion);
            comando.Parameters.AddWithValue("@esquema", objeto.Esquema);
            comando.Parameters.AddWithValue("@nombre", objeto.Nombre);

            return await comando.ExecuteScalarAsync(cancelacion) is int id
                ? id
                : throw new InvalidOperationException($"{objeto.NombreCompleto} ya no existe en la base de datos.");
        }

        private static async Task<List<ColumnaModelo>> LeerColumnasObjetoAsync(SqlConnection conexion, int id, CancellationToken cancelacion)
        {
            await using var comando = new SqlCommand("""
                SELECT c.name, ty.name, c.max_length, c.precision, c.scale, c.is_nullable
                FROM sys.columns c
                JOIN sys.types ty ON ty.user_type_id = c.user_type_id
                WHERE c.object_id = @id
                ORDER BY c.column_id
                """, conexion);
            comando.Parameters.AddWithValue("@id", id);

            await using var lector = await comando.ExecuteReaderAsync(cancelacion);
            var columnas = new List<ColumnaModelo>();

            while (await lector.ReadAsync(cancelacion))
            {
                columnas.Add(new ColumnaModelo
                {
                    Nombre = lector.GetString(0),
                    TipoDatos = FormatearTipo(lector.GetString(1), lector.GetInt16(2), lector.GetByte(3), lector.GetByte(4)),
                    PermiteNulos = lector.GetBoolean(5)
                });
            }

            return columnas;
        }

        private static async Task<DetalleObjetoModelo> LeerRutinaAsync(SqlConnection conexion, ObjetoBaseDatosModelo objeto, int id, CancellationToken cancelacion)
        {
            await using var comando = new SqlCommand("""
                SELECT p.parameter_id, p.name, ty.name, p.max_length, p.precision, p.scale, p.is_output, o.type
                FROM sys.objects o
                LEFT JOIN sys.parameters p ON p.object_id = o.object_id
                LEFT JOIN sys.types ty ON ty.user_type_id = p.user_type_id
                WHERE o.object_id = @id
                ORDER BY p.parameter_id
                """, conexion);
            comando.Parameters.AddWithValue("@id", id);

            await using var lector = await comando.ExecuteReaderAsync(cancelacion);
            var parametros = new List<ParametroModelo>();
            string? retorno = null;
            var tipoObjeto = "";

            while (await lector.ReadAsync(cancelacion))
            {
                tipoObjeto = lector.GetString(7).Trim();

                if (lector.IsDBNull(0))
                {
                    continue;
                }

                var tipo = FormatearTipo(lector.GetString(2), lector.GetInt16(3), lector.GetByte(4), lector.GetByte(5));

                if (lector.GetInt32(0) == 0)
                {
                    retorno = tipo;
                    continue;
                }

                parametros.Add(new ParametroModelo
                {
                    Nombre = lector.GetString(1),
                    TipoDatos = tipo,
                    Direccion = lector.GetBoolean(6) ? DireccionParametro.Salida : DireccionParametro.Entrada
                });
            }

            await lector.CloseAsync();
            var devuelveTabla = tipoObjeto is "IF" or "TF" or "FT";

            return new DetalleObjetoModelo
            {
                Objeto = objeto,
                Parametros = parametros,
                Retorno = devuelveTabla ? "una tabla" : retorno,
                DevuelveTabla = devuelveTabla,
                Columnas = devuelveTabla ? await LeerColumnasObjetoAsync(conexion, id, cancelacion) : []
            };
        }

        private static async Task<DetalleObjetoModelo> LeerDisparadorAsync(SqlConnection conexion, ObjetoBaseDatosModelo objeto, int id, CancellationToken cancelacion)
        {
            await using var comando = new SqlCommand("""
                SELECT t.is_instead_of_trigger, t.is_disabled, e.type_desc
                FROM sys.triggers t
                LEFT JOIN sys.trigger_events e ON e.object_id = t.object_id
                WHERE t.object_id = @id
                ORDER BY e.type
                """, conexion);
            comando.Parameters.AddWithValue("@id", id);

            await using var lector = await comando.ExecuteReaderAsync(cancelacion);
            var eventos = new List<string>();
            var enLugarDe = false;
            var deshabilitado = false;

            while (await lector.ReadAsync(cancelacion))
            {
                enLugarDe = lector.GetBoolean(0);
                deshabilitado = lector.GetBoolean(1);

                if (!lector.IsDBNull(2))
                {
                    eventos.Add(lector.GetString(2).Replace('_', ' '));
                }
            }

            return new DetalleObjetoModelo
            {
                Objeto = objeto,
                Momento = enLugarDe ? "INSTEAD OF" : "AFTER",
                Eventos = eventos,
                PorFila = objeto.Tabla is null ? null : false,
                Habilitado = !deshabilitado
            };
        }

        private static async Task<DetalleObjetoModelo> LeerSecuenciaAsync(SqlConnection conexion, ObjetoBaseDatosModelo objeto, int id, CancellationToken cancelacion)
        {
            await using var comando = new SqlCommand("""
                SELECT TYPE_NAME(s.user_type_id), s.precision, s.scale,
                       CONVERT(nvarchar(50), s.current_value), CONVERT(nvarchar(50), s.start_value),
                       CONVERT(nvarchar(50), s.increment), CONVERT(nvarchar(50), s.minimum_value),
                       CONVERT(nvarchar(50), s.maximum_value), s.is_cycling, CONVERT(nvarchar(50), s.last_used_value)
                FROM sys.sequences s
                WHERE s.object_id = @id
                """, conexion);
            comando.Parameters.AddWithValue("@id", id);

            await using var lector = await comando.ExecuteReaderAsync(cancelacion);
            await lector.ReadAsync(cancelacion);

            var tipo = lector.GetString(0);
            
            return new DetalleObjetoModelo
            {
                Objeto = objeto,
                Retorno = tipo is "decimal" or "numeric" ? $"{tipo}({lector.GetByte(1)}, {lector.GetByte(2)})" : tipo,
                ValorActual = lector.IsDBNull(9) ? null : lector.GetString(9),
                Inicio = lector.GetString(4),
                Incremento = lector.GetString(5),
                Minimo = lector.GetString(6),
                Maximo = lector.GetString(7),
                Ciclo = lector.GetBoolean(8)
            };
        }
    }
}
