using MiGestorBDAccesible.Core.Conexiones;
using MiGestorBDAccesible.Core.Definicion;
using MiGestorBDAccesible.Core.Estructura;
using MySqlConnector;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace MiGestorBDAccesible.Core.Proveedores.MySql
{
    public abstract partial class ProveedorMySqlBase : IProveedorBaseDatos
    {
        private static readonly HashSet<string> _basesSistema = new(StringComparer.OrdinalIgnoreCase) { "mysql", "information_schema", "performance_schema", "sys" };

        public abstract TipoMotor Motor { get; }

        public abstract CapacidadesMotorModelo Capacidades { get; }

        protected abstract bool EsEsteMotor(string version);

        public string BaseDatosAdministracion => "information_schema";

        public string EsquemaPredeterminado(ConexionActiva conexion) => "";

        protected static CapacidadesMotorModelo CrearCapacidades(int versionMinima, string nombreVersionMinima, string calculadaGuardada, IReadOnlyList<TipoObjeto> tiposObjeto) => new()
        {
            TieneEsquemas = false,
            TiposObjeto = tiposObjeto,
            PuedeDeshabilitarDisparadores = false,
            SalidasEnResultados = true,
            IndiceIncluye = false,
            IndiceFiltrado = false,
            CheckPuedeOmitirComprobacion = false,
            MetodosIndice =
            [
                ("BTREE", "árbol B, el normal: sirve para =, <, >, BETWEEN y ORDER BY"),
                ("FULLTEXT", "para buscar palabras dentro de textos largos con MATCH … AGAINST")
            ],
            ReglasAlEliminar = [ReglaReferencial.Restringir, ReglaReferencial.Cascada, ReglaReferencial.EstablecerNulo, ReglaReferencial.SinAccion],
            ReglasAlActualizar = [ReglaReferencial.Restringir, ReglaReferencial.Cascada, ReglaReferencial.EstablecerNulo, ReglaReferencial.SinAccion],
            VersionPrincipalMinima = versionMinima,
            NombreVersionMinima = nombreVersionMinima,
            Terminos = new TerminosMotorModelo
            {
                Identidad = (_, _) => "AUTO_INCREMENT",
                TieneIndicesAgrupados = false,
                NombreIdentidad = "AUTO_INCREMENT",
                ColumnaCalculada = "GENERATED ALWAYS AS … VIRTUAL",
                ColumnaCalculadaGuardada = $"GENERATED ALWAYS AS … {calculadaGuardada}",
                EjemploTiposIdentidad = "int o bigint",
                EjemploPredeterminado = "0, 1, 'texto' o una función como (CURRENT_DATE)",
                SeparadorNombres = "_",
                NombreId = "id",
                TipoId = "int",
                TipoTexto = "varchar",
                ModificarColumna = "MODIFY COLUMN",
                ComprobarExistentes = "al desmarcarla, FOREIGN_KEY_CHECKS = 0",
                RequiereEsquema = false,
                IncrementoPorColumna = false,
                TiposCompatibles = TiposCompatiblesMySql
            }
        };

        public async Task<ResultadoPruebaConexion> ProbarConexionAsync(
            ConexionModelo conexion,
            string? contrasena,
            CancellationToken cancelacion = default)
        {
            var cronometro = Stopwatch.StartNew();

            try
            {
                await using var conexionMy = new MySqlConnection(CadenaConexion(conexion, contrasena));
                await conexionMy.OpenAsync(cancelacion);

                await using var comando = new MySqlCommand("SELECT VERSION(), @@version_comment", conexionMy);
                await using var lector = await comando.ExecuteReaderAsync(cancelacion);
                await lector.ReadAsync(cancelacion);

                var version = lector.GetString(0);
                var comentario = lector.GetString(1);
                var (principal, menor) = LeerVersion(version);
                var numero = version.Split('-')[0];

                if (!EsEsteMotor(version))
                {
                    var otro = Motor == TipoMotor.MySql ? TipoMotor.MariaDb : TipoMotor.MySql;
                    return new ResultadoPruebaConexion(true, cronometro.Elapsed, $"{otro.Nombre()} {principal}.{menor}", comentario, numero,
                        Advertencia: $"Atención: este servidor es {otro.Nombre()}, no {Motor.Nombre()}. Cambia el motor de la conexión a {otro.Nombre()} para que todo funcione bien.");
                }

                return new ResultadoPruebaConexion(true, cronometro.Elapsed, $"{Motor.Nombre()} {principal}.{menor}", comentario, numero,
                    Advertencia: Capacidades.AvisoVersion(principal * 100 + menor));
            }
            catch (Exception ex) when (ex is MySqlException or TimeoutException)
            {
                return new ResultadoPruebaConexion(false, cronometro.Elapsed, Error: MensajeError(conexion, ex));
            }
            catch (InvalidOperationException ex)
            {
                return new ResultadoPruebaConexion(false, cronometro.Elapsed, Error: ex.Message);
            }
        }

        public static (int Principal, int Menor) LeerVersion(string version)
        {
            var coincidencia = Regex.Match(version, @"^(\d+)\.(\d+)");
            return coincidencia.Success
                ? (int.Parse(coincidencia.Groups[1].Value), int.Parse(coincidencia.Groups[2].Value))
                : (0, 0);
        }

        public async Task<List<BaseDatosModelo>> ObtenerBasesDatosAsync(
            ConexionActiva conexion,
            CancellationToken cancelacion = default)
        {
            await using var conexionMy = await AbrirAsync(conexion, cancelacion);
            await using var comando = new MySqlCommand("SELECT schema_name FROM information_schema.schemata", conexionMy);
            await using var lector = await comando.ExecuteReaderAsync(cancelacion);

            var bases = new List<BaseDatosModelo>();

            while (await lector.ReadAsync(cancelacion))
            {
                var nombre = lector.GetString(0);
                bases.Add(new BaseDatosModelo { Nombre = nombre, EsSistema = _basesSistema.Contains(nombre), EnLinea = true });
            }

            return bases.OrderBy(b => b.Nombre.ToLowerInvariant(), StringComparer.Ordinal).ToList();
        }

        public async Task<List<TablaModelo>> ObtenerTablasAsync(
            ConexionActiva conexion,
            string baseDatos,
            CancellationToken cancelacion = default)
        {
            await using var conexionMy = await AbrirAsync(conexion, cancelacion);
            await using var comando = new MySqlCommand("""
                SELECT table_name
                FROM information_schema.tables
                WHERE table_schema = @base AND table_type = 'BASE TABLE'
                """, conexionMy);
            comando.Parameters.AddWithValue("@base", baseDatos);
            await using var lector = await comando.ExecuteReaderAsync(cancelacion);

            var tablas = new List<TablaModelo>();

            while (await lector.ReadAsync(cancelacion))
            {
                tablas.Add(new TablaModelo { Esquema = "", Nombre = lector.GetString(0), EsquemaPredeterminado = "" });
            }

            return tablas.OrderBy(t => t.Nombre.ToLowerInvariant(), StringComparer.Ordinal).ToList();
        }

        public void ReiniciarConexiones()
        {
            List<SesionMySql> cerradas;

            lock (_cerrojoSesiones)
            {
                var claves = _sesiones.Where(p => !p.Value.CambiosPendientes).Select(p => p.Key).ToList();
                cerradas = claves.Select(k => _sesiones[k]).ToList();

                foreach (var clave in claves)
                {
                    _sesiones.Remove(clave);
                }
            }

            foreach (var sesion in cerradas)
            {
                sesion.Conexion.Dispose();
            }

            MySqlConnection.ClearAllPools();
        }

        public string CadenaConexion(ConexionModelo conexion, string? contrasena, string? baseDatos = null)
        {
            var constructor = new MySqlConnectionStringBuilder
            {
                Server = conexion.Servidor,
                Port = (uint)(conexion.Puerto ?? Motor.PuertoPredeterminado()),
                UserID = conexion.Usuario ?? "",
                Password = contrasena ?? "",
                ConnectionTimeout = 6,
                ApplicationName = "MiGestorBDAccesible",
                SslMode = MySqlSslMode.Preferred,
                TreatTinyAsBoolean = false,
                AllowZeroDateTime = true,
                AllowUserVariables = true
            };

            var baseInicial = baseDatos ?? conexion.BaseDatosInicial;

            if (!string.IsNullOrWhiteSpace(baseInicial))
            {
                constructor.Database = baseInicial;
            }

            return constructor.ConnectionString;
        }

        private async Task<MySqlConnection> AbrirAsync(ConexionActiva conexion, CancellationToken cancelacion)
        {
            for (var intento = 1; ; intento++)
            {
                var conexionMy = new MySqlConnection(CadenaConexion(conexion.Conexion, conexion.Contrasena));

                try
                {
                    await conexionMy.OpenAsync(cancelacion);
                    return conexionMy;
                }
                catch (Exception ex) when (EsErrorDeConexion(ex))
                {
                    MySqlConnection.ClearPool(conexionMy);
                    await conexionMy.DisposeAsync();

                    if (intento >= 2)
                    {
                        throw new ConexionPerdidaException(MensajeError(conexion.Conexion, ex), ex);
                    }

                    await Task.Delay(500, cancelacion);
                }
                catch
                {
                    await conexionMy.DisposeAsync();
                    throw;
                }
            }
        }

        private static bool EsErrorDeConexion(Exception ex) =>
            ex is TimeoutException
            || ex is MySqlException { ErrorCode: MySqlErrorCode.UnableToConnectToHost or MySqlErrorCode.ServerShutdown }
            || ex is MySqlException { InnerException: System.Net.Sockets.SocketException or IOException };

        protected string MensajeError(ConexionModelo conexion, Exception ex) => ex switch
        {
            MySqlException { ErrorCode: MySqlErrorCode.AccessDenied } => $"Usuario o contraseña incorrectos. Detalle: {ex.Message}",
            MySqlException { ErrorCode: MySqlErrorCode.UnknownDatabase } => $"El servidor responde, pero no tiene la base de datos {conexion.BaseDatosInicial}. Revisa la base inicial o déjala vacía. Detalle: {ex.Message}",
            MySqlException { ErrorCode: MySqlErrorCode.DatabaseAccessDenied } => $"La base {conexion.BaseDatosInicial} no existe, o el usuario {conexion.Usuario} no tiene permiso para entrar a ella: {Motor.Nombre()} no distingue los dos casos. Revisa la base inicial o déjala vacía. Detalle: {ex.Message}",
            MySqlException { ErrorCode: MySqlErrorCode.HostNotPrivileged } => $"El servidor no permite entrar con el usuario {conexion.Usuario} desde este equipo. Detalle: {ex.Message}",
            MySqlException when ex.Message.Contains("caching_sha2_password", StringComparison.Ordinal) =>
                $"El servidor pide una conexión cifrada para esta forma de contraseña, y no se pudo establecer. Revisa que el servidor tenga activado SSL. Detalle: {ex.Message}",
            _ when EsErrorDeConexion(ex) =>
                $"No se pudo conectar con {conexion.Direccion}. Comprueba que el servidor esté encendido; si usas Docker, que Docker Desktop y el contenedor de {Motor.Nombre()} estén en marcha. Detalle: {ex.Message}",
            _ => ex.Message
        };

    }
}
