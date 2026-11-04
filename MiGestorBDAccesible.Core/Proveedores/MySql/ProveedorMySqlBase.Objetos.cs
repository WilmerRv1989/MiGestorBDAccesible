using MiGestorBDAccesible.Core.Conexiones;
using MiGestorBDAccesible.Core.Estructura;
using MySqlConnector;
using System.Globalization;
using System.Text.RegularExpressions;

namespace MiGestorBDAccesible.Core.Proveedores.MySql
{
    public abstract partial class ProveedorMySqlBase
    {
        private static readonly Regex _definidor = new(@"\s*DEFINER\s*=\s*(`[^`]*`|'[^']*'|\S+)@(`[^`]*`|'[^']*'|\S+)", RegexOptions.IgnoreCase);

        private static readonly Regex _numerico = new(@"^(tinyint|smallint|mediumint|int|integer|bigint|decimal|numeric|float|double|real|bit|year)\b", RegexOptions.IgnoreCase);

        public async Task<Dictionary<TipoObjeto, int>> ContarObjetosAsync(
            ConexionActiva conexion,
            string baseDatos,
            CancellationToken cancelacion = default)
        {
            await using var conexionMy = await AbrirAsync(conexion, cancelacion);
            await using var comando = new MySqlCommand("""
                SELECT
                    (SELECT COUNT(*) FROM information_schema.views WHERE table_schema = @base),
                    (SELECT COUNT(*) FROM information_schema.routines WHERE routine_schema = @base AND routine_type = 'PROCEDURE'),
                    (SELECT COUNT(*) FROM information_schema.routines WHERE routine_schema = @base AND routine_type = 'FUNCTION'),
                    (SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = @base AND table_type = 'SEQUENCE')
                """, conexionMy);
            comando.Parameters.AddWithValue("@base", baseDatos);
            await using var lector = await comando.ExecuteReaderAsync(cancelacion);
            await lector.ReadAsync(cancelacion);

            return new Dictionary<TipoObjeto, int>
            {
                [TipoObjeto.Vista] = Convert.ToInt32(lector.GetValue(0), CultureInfo.InvariantCulture),
                [TipoObjeto.Procedimiento] = Convert.ToInt32(lector.GetValue(1), CultureInfo.InvariantCulture),
                [TipoObjeto.Funcion] = Convert.ToInt32(lector.GetValue(2), CultureInfo.InvariantCulture),
                [TipoObjeto.Secuencia] = Convert.ToInt32(lector.GetValue(3), CultureInfo.InvariantCulture)
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
                TipoObjeto.Vista => "SELECT table_name FROM information_schema.views WHERE table_schema = @base",
                TipoObjeto.Procedimiento => "SELECT routine_name FROM information_schema.routines WHERE routine_schema = @base AND routine_type = 'PROCEDURE'",
                TipoObjeto.Funcion => "SELECT routine_name FROM information_schema.routines WHERE routine_schema = @base AND routine_type = 'FUNCTION'",
                TipoObjeto.Secuencia => "SELECT table_name FROM information_schema.tables WHERE table_schema = @base AND table_type = 'SEQUENCE'",
                _ => null
            };

            if (consulta is null)
            {
                return [];
            }

            await using var conexionMy = await AbrirAsync(conexion, cancelacion);
            var nombres = await LeerAsync(conexionMy, consulta, baseDatos, "", cancelacion, lector => lector.GetString(0));

            return nombres.OrderBy(n => n.ToLowerInvariant(), StringComparer.Ordinal)
                .Select(n => new ObjetoBaseDatosModelo { Tipo = tipo, Esquema = "", Nombre = n })
                .ToList();
        }

        public async Task<List<ObjetoBaseDatosModelo>> ObtenerDisparadoresTablaAsync(
            ConexionActiva conexion,
            string baseDatos,
            TablaModelo tabla,
            CancellationToken cancelacion = default)
        {
            await using var conexionMy = await AbrirAsync(conexion, cancelacion);
            var nombres = await LeerAsync(conexionMy, """
                SELECT trigger_name FROM information_schema.triggers
                WHERE event_object_schema = @base AND event_object_table = @tabla
                ORDER BY action_order
                """, baseDatos, tabla.Nombre, cancelacion, lector => lector.GetString(0));

            return nombres.Select(n => new ObjetoBaseDatosModelo { Tipo = TipoObjeto.Disparador, Esquema = "", Nombre = n, Tabla = tabla }).ToList();
        }

        public async Task<DetalleObjetoModelo> ObtenerDetalleObjetoAsync(
            ConexionActiva conexion,
            string baseDatos,
            ObjetoBaseDatosModelo objeto,
            CancellationToken cancelacion = default)
        {
            await using var conexionMy = await AbrirAsync(conexion, cancelacion);

            return objeto.Tipo switch
            {
                TipoObjeto.Vista => new DetalleObjetoModelo
                {
                    Objeto = objeto,
                    Columnas = await LeerAsync(conexionMy, ConsultaColumnas, baseDatos, objeto.Nombre, cancelacion, lector => LeerColumna(lector, 1, 1))
                },
                TipoObjeto.Procedimiento or TipoObjeto.Funcion => await LeerRutinaAsync(conexionMy, baseDatos, objeto, cancelacion),
                TipoObjeto.Disparador => await LeerDisparadorAsync(conexionMy, baseDatos, objeto, cancelacion),
                TipoObjeto.Secuencia => await LeerSecuenciaAsync(conexionMy, baseDatos, objeto, cancelacion),
                _ => new DetalleObjetoModelo { Objeto = objeto }
            };
        }

        private static async Task<DetalleObjetoModelo> LeerRutinaAsync(MySqlConnection conexion, string baseDatos, ObjetoBaseDatosModelo objeto, CancellationToken cancelacion)
        {
            var filas = await LeerAsync(conexion, """
                SELECT ordinal_position, parameter_mode, parameter_name, dtd_identifier
                FROM information_schema.parameters
                WHERE specific_schema = @base AND specific_name = @tabla
                ORDER BY ordinal_position
                """, baseDatos, objeto.Nombre, cancelacion, lector => (
                    Posicion: Convert.ToInt32(lector.GetValue(0), CultureInfo.InvariantCulture),
                    Modo: lector.IsDBNull(1) ? null : lector.GetString(1),
                    Nombre: lector.IsDBNull(2) ? "" : lector.GetString(2),
                    Tipo: FormatearTipo(lector.GetString(3))));

            return new DetalleObjetoModelo
            {
                Objeto = objeto,
                Retorno = filas.FirstOrDefault(f => f.Posicion == 0).Tipo,
                Parametros = filas.Where(f => f.Posicion > 0).Select(f => new ParametroModelo
                {
                    Nombre = f.Nombre,
                    TipoDatos = f.Tipo,
                    Direccion = f.Modo switch
                    {
                        "OUT" => DireccionParametro.Salida,
                        "INOUT" => DireccionParametro.EntradaSalida,
                        _ => DireccionParametro.Entrada
                    }
                }).ToList()
            };
        }

        private static async Task<DetalleObjetoModelo> LeerDisparadorAsync(MySqlConnection conexion, string baseDatos, ObjetoBaseDatosModelo objeto, CancellationToken cancelacion)
        {
            var datos = (await LeerAsync(conexion, """
                SELECT action_timing, event_manipulation
                FROM information_schema.triggers
                WHERE trigger_schema = @base AND trigger_name = @tabla
                """, baseDatos, objeto.Nombre, cancelacion, lector => (Momento: lector.GetString(0), Evento: lector.GetString(1)))).Single();

            return new DetalleObjetoModelo { Objeto = objeto, Momento = datos.Momento, Eventos = [datos.Evento], PorFila = true };
        }

        private static async Task<DetalleObjetoModelo> LeerSecuenciaAsync(MySqlConnection conexion, string baseDatos, ObjetoBaseDatosModelo objeto, CancellationToken cancelacion)
        {
            await using var comando = new MySqlCommand(
                $"SELECT next_not_cached_value, minimum_value, maximum_value, start_value, increment, cycle_option FROM {Delimitar(baseDatos)}.{Delimitar(objeto.Nombre)}", conexion);
            await using var lector = await comando.ExecuteReaderAsync(cancelacion);
            await lector.ReadAsync(cancelacion);

            string Texto(int indice) => Convert.ToString(lector.GetValue(indice), CultureInfo.InvariantCulture) ?? "";

            return new DetalleObjetoModelo
            {
                Objeto = objeto,
                Retorno = "bigint",
                ValorActual = Texto(0) == Texto(3) ? null : $"anterior a {Texto(0)}",
                Minimo = Texto(1),
                Maximo = Texto(2),
                Inicio = Texto(3),
                Incremento = Texto(4),
                Ciclo = Texto(5) != "0"
            };
        }

        public async Task<string> GenerarScriptObjetoAsync(
            ConexionActiva conexion,
            string baseDatos,
            ObjetoBaseDatosModelo objeto,
            CancellationToken cancelacion = default)
        {
            await using var conexionMy = await AbrirAsync(conexion, cancelacion);
            var nombre = $"{Delimitar(baseDatos)}.{Delimitar(objeto.Nombre)}";
            var (consulta, indice) = objeto.Tipo switch
            {
                TipoObjeto.Vista => ($"SHOW CREATE VIEW {nombre}", 1),
                TipoObjeto.Procedimiento => ($"SHOW CREATE PROCEDURE {nombre}", 2),
                TipoObjeto.Funcion => ($"SHOW CREATE FUNCTION {nombre}", 2),
                TipoObjeto.Disparador => ($"SHOW CREATE TRIGGER {nombre}", 2),
                _ => ($"SHOW CREATE SEQUENCE {nombre}", 1)
            };

            await using var comando = new MySqlCommand(consulta, conexionMy);
            await using var lector = await comando.ExecuteReaderAsync(cancelacion);
            await lector.ReadAsync(cancelacion);

            if (lector.IsDBNull(indice))
            {
                throw new InvalidOperationException($"{Motor.Nombre()} no deja ver el código de este objeto con tu usuario.");
            }

            return ArmarScriptObjeto(objeto.Tipo, lector.GetString(indice));
        }

        public static string ArmarScriptObjeto(TipoObjeto tipo, string definicion)
        {
            var limpio = AjustarLineas(_definidor.Replace(definicion, "", 1)).Trim();

            return tipo is TipoObjeto.Procedimiento or TipoObjeto.Funcion or TipoObjeto.Disparador
                ? $"DELIMITER $${Environment.NewLine}{limpio}$${Environment.NewLine}DELIMITER ;{Environment.NewLine}"
                : $"{limpio};{Environment.NewLine}";
        }

        public string ScriptEliminarObjeto(ObjetoBaseDatosModelo objeto) => objeto.Tipo switch
        {
            TipoObjeto.Vista => $"DROP VIEW {Identificador(objeto.Nombre)};",
            TipoObjeto.Procedimiento => $"DROP PROCEDURE {Identificador(objeto.Nombre)};",
            TipoObjeto.Funcion => $"DROP FUNCTION {Identificador(objeto.Nombre)};",
            TipoObjeto.Secuencia => $"DROP SEQUENCE {Identificador(objeto.Nombre)};",
            _ => $"DROP TRIGGER {Identificador(objeto.Nombre)};"
        };

        public string ScriptHabilitarDisparador(ObjetoBaseDatosModelo disparador, bool habilitar) =>
            throw new NotSupportedException($"{Motor.Nombre()} no permite deshabilitar disparadores.");

        public string ScriptReiniciarSecuencia(ObjetoBaseDatosModelo secuencia, long valor) =>
            $"ALTER SEQUENCE {Identificador(secuencia.Nombre)} RESTART WITH {valor};";

        public string ScriptLlamada(DetalleObjetoModelo detalle, IReadOnlyDictionary<string, string?> valores)
        {
            var objeto = detalle.Objeto;
            var nombre = Identificador(objeto.Nombre);

            if (objeto.Tipo == TipoObjeto.Funcion)
            {
                return $"SELECT {nombre}({string.Join(", ", detalle.Parametros.Select(p => LiteralParametro(p, valores)))}) AS resultado;";
            }

            var salidas = detalle.Parametros.Where(p => p.Direccion != DireccionParametro.Entrada).ToList();
            var lineas = new List<string>();

            foreach (var parametro in salidas.Where(p => p.Direccion == DireccionParametro.EntradaSalida))
            {
                lineas.Add($"SET @{parametro.Nombre} = {LiteralParametro(parametro, valores)};");
            }

            var argumentos = detalle.Parametros.Select(p => p.Direccion == DireccionParametro.Entrada ? LiteralParametro(p, valores) : $"@{p.Nombre}");
            lineas.Add($"CALL {nombre}({string.Join(", ", argumentos)});");

            if (salidas.Count > 0)
            {
                lineas.Add($"SELECT {string.Join(", ", salidas.Select(p => $"@{p.Nombre} AS {Identificador(p.Nombre)}"))};");
            }

            return string.Join(Environment.NewLine, lineas);
        }

        private static string LiteralParametro(ParametroModelo parametro, IReadOnlyDictionary<string, string?> valores)
        {
            var valor = valores.TryGetValue(parametro.Nombre, out var escrito) ? escrito?.Trim() : null;

            if (string.IsNullOrEmpty(valor) || valor.Equals("NULL", StringComparison.OrdinalIgnoreCase))
            {
                return "NULL";
            }

            return _numerico.IsMatch(parametro.TipoDatos) && decimal.TryParse(valor, NumberStyles.Number, CultureInfo.InvariantCulture, out _)
                ? valor
                : $"'{valor.Replace("\\", "\\\\").Replace("'", "''")}'";
        }

        public string ScriptSeleccionarPrimeras(DetalleObjetoModelo vista) =>
            $"""
            SELECT
            {string.Join($",{Environment.NewLine}", vista.Columnas.Select(c => $"    {Identificador(c.Nombre)}"))}
            FROM {Identificador(vista.Objeto.Nombre)}
            LIMIT 100;
            """;
    }
}
