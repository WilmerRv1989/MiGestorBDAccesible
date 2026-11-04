using MiGestorBDAccesible.Core.Conexiones;
using MiGestorBDAccesible.Core.Definicion;
using MiGestorBDAccesible.Core.Ejecucion;
using MiGestorBDAccesible.Core.Estructura;
using Npgsql;
using System.Diagnostics;
using System.Net.Sockets;

namespace MiGestorBDAccesible.Core.Proveedores.PostgreSql
{
    public partial class ProveedorPostgreSql : IProveedorBaseDatos
    {
        private static readonly CapacidadesMotorModelo _capacidades = new()
        {
            SesionPorBase = true,
            SalidasEnResultados = true,
            DdlTransaccional = true,
            IndiceConcurrente = true,
            MetodosIndice =
            [
                ("btree", "árbol B, el normal: sirve para =, <, >, BETWEEN y ORDER BY"),
                ("hash", "solo para comparar con ="),
                ("gin", "para buscar dentro de arreglos, jsonb y textos completos"),
                ("gist", "para datos geométricos, rangos y búsquedas por cercanía"),
                ("brin", "muy pequeño, para tablas enormes ordenadas por esa columna, como fechas")
            ],
            ReglasAlEliminar = [.. CapacidadesMotorModelo.ReglasEstandar, ReglaReferencial.Restringir],
            ReglasAlActualizar = [.. CapacidadesMotorModelo.ReglasEstandar, ReglaReferencial.Restringir],
            VersionPrincipalMinima = 14,
            NombreVersionMinima = "PostgreSQL 14",
            Terminos = new TerminosMotorModelo
            {
                Identidad = (semilla, incremento) => (semilla ?? 1) == 1 && (incremento ?? 1) == 1
                    ? "GENERATED AS IDENTITY"
                    : $"GENERATED AS IDENTITY (START WITH {semilla} INCREMENT BY {incremento})",
                TieneIndicesAgrupados = false,
                NombreIdentidad = "GENERATED AS IDENTITY",
                ColumnaCalculada = "GENERATED ALWAYS AS … VIRTUAL",
                ColumnaCalculadaGuardada = "GENERATED ALWAYS AS … STORED",
                EjemploTiposIdentidad = "integer o bigint",
                EjemploPredeterminado = "0, 1, 'texto' o una función como now()",
                ComprobarExistentes = "al desmarcarla, NOT VALID",
                SeparadorNombres = "_",
                NombreId = "id",
                TipoId = "integer",
                TipoTexto = "varchar",
                TiposCompatibles = TiposCompatiblesPostgreSql,
                LlaveDeshabilitada = "NOT VALID",
                FraseLlaveDeshabilitada = "Las filas que ya existían no se comprobaron al crear esta llave; las nuevas sí se comprueban"
            }
        };

        private static readonly HashSet<string> _basesSistema = new(StringComparer.Ordinal) { "postgres", "template0", "template1" };

        public TipoMotor Motor => TipoMotor.PostgreSql;

        public CapacidadesMotorModelo Capacidades => _capacidades;

        public string BaseDatosAdministracion => "postgres";

        public string EsquemaPredeterminado(ConexionActiva conexion) => "public";

        public async Task<ResultadoPruebaConexion> ProbarConexionAsync(
            ConexionModelo conexion,
            string? contrasena,
            CancellationToken cancelacion = default)
        {
            var cronometro = Stopwatch.StartNew();

            try
            {
                await using var conexionPg = new NpgsqlConnection(CadenaConexion(conexion, contrasena));
                await conexionPg.OpenAsync(cancelacion);

                await using var comando = new NpgsqlCommand("SELECT current_setting('server_version'), current_setting('server_version_num')::int", conexionPg);
                await using var lector = await comando.ExecuteReaderAsync(cancelacion);
                await lector.ReadAsync(cancelacion);

                var version = lector.GetString(0).Split(' ')[0];
                var versionPrincipal = lector.GetInt32(1) / 10000;

                return new ResultadoPruebaConexion(true, cronometro.Elapsed, $"PostgreSQL {versionPrincipal}", "edición comunitaria", version,
                    Advertencia: _capacidades.AvisoVersion(versionPrincipal));
            }
            catch (Exception ex) when (ex is NpgsqlException or TimeoutException)
            {
                return new ResultadoPruebaConexion(false, cronometro.Elapsed, Error: MensajeError(conexion, ex));
            }
            catch (InvalidOperationException ex)
            {
                return new ResultadoPruebaConexion(false, cronometro.Elapsed, Error: ex.Message);
            }
        }

        public async Task<List<BaseDatosModelo>> ObtenerBasesDatosAsync(
            ConexionActiva conexion,
            CancellationToken cancelacion = default)
        {
            await using var conexionPg = await AbrirAsync(conexion, null, cancelacion);
            await using var comando = new NpgsqlCommand("""
                SELECT datname, datallowconn, has_database_privilege(datname, 'CONNECT')
                FROM pg_database
                ORDER BY datname
                """, conexionPg);
            await using var lector = await comando.ExecuteReaderAsync(cancelacion);

            var bases = new List<BaseDatosModelo>();

            while (await lector.ReadAsync(cancelacion))
            {
                var nombre = lector.GetString(0);
                bases.Add(new BaseDatosModelo
                {
                    Nombre = nombre,
                    EsSistema = _basesSistema.Contains(nombre),
                    EnLinea = lector.GetBoolean(1) && lector.GetBoolean(2)
                });
            }

            return bases;
        }

        public async Task<List<TablaModelo>> ObtenerTablasAsync(
            ConexionActiva conexion,
            string baseDatos,
            CancellationToken cancelacion = default)
        {
            await using var conexionPg = await AbrirAsync(conexion, baseDatos, cancelacion);
            await using var comando = new NpgsqlCommand("""
                SELECT n.nspname, c.relname
                FROM pg_class c
                JOIN pg_namespace n ON n.oid = c.relnamespace
                WHERE c.relkind IN ('r', 'p')
                  AND NOT c.relispartition
                  AND n.nspname NOT IN ('pg_catalog', 'information_schema')
                  AND n.nspname NOT LIKE 'pg_toast%'
                  AND n.nspname NOT LIKE 'pg_temp%'
                ORDER BY n.nspname, c.relname
                """, conexionPg);
            await using var lector = await comando.ExecuteReaderAsync(cancelacion);

            var tablas = new List<TablaModelo>();

            while (await lector.ReadAsync(cancelacion))
            {
                tablas.Add(new TablaModelo { Esquema = lector.GetString(0), Nombre = lector.GetString(1), EsquemaPredeterminado = "public" });
            }

            return tablas;
        }

        public void ReiniciarConexiones()
        {
            List<SesionPostgreSql> cerradas;

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

            NpgsqlConnection.ClearAllPools();
        }

        public static string CadenaConexion(ConexionModelo conexion, string? contrasena, string? baseDatos = null)
        {
            var constructor = new NpgsqlConnectionStringBuilder
            {
                Host = conexion.Servidor,
                Port = conexion.Puerto ?? TipoMotor.PostgreSql.PuertoPredeterminado(),
                Database = baseDatos ?? conexion.BaseDatosInicial ?? TipoMotor.PostgreSql.BaseInicialPredeterminada(),
                Username = conexion.Usuario ?? "",
                Password = contrasena ?? "",
                Timeout = 6,
                ApplicationName = "MiGestorBDAccesible",
                IncludeErrorDetail = true
            };

            return constructor.ConnectionString;
        }

        private static async Task<NpgsqlConnection> AbrirAsync(ConexionActiva conexion, string? baseDatos, CancellationToken cancelacion)
        {
            for (var intento = 1; ; intento++)
            {
                var conexionPg = new NpgsqlConnection(CadenaConexion(conexion.Conexion, conexion.Contrasena, baseDatos));

                try
                {
                    await conexionPg.OpenAsync(cancelacion);
                    return conexionPg;
                }
                catch (Exception ex) when (EsErrorDeConexion(ex))
                {
                    NpgsqlConnection.ClearPool(conexionPg);
                    await conexionPg.DisposeAsync();

                    if (intento >= 2)
                    {
                        throw new ConexionPerdidaException(MensajeError(conexion.Conexion, ex), ex);
                    }

                    await Task.Delay(500, cancelacion);
                }
                catch
                {
                    await conexionPg.DisposeAsync();
                    throw;
                }
            }
        }

        private static bool EsErrorDeConexion(Exception ex) =>
            ex is TimeoutException
            || (ex is NpgsqlException npgsql && ex is not PostgresException && (npgsql.IsTransient || npgsql.InnerException is SocketException or IOException))
            || ex is PostgresException { SqlState: "57P01" or "57P02" or "57P03" };

        private static string MensajeError(ConexionModelo conexion, Exception ex) => ex switch
        {
            PostgresException { SqlState: "28P01" } => $"Usuario o contraseña incorrectos. Detalle: {ex.Message}",
            PostgresException { SqlState: "3D000" } => $"El servidor responde, pero no tiene la base de datos {conexion.BaseDatosInicial ?? "postgres"}. Revisa la base inicial. Detalle: {ex.Message}",
            PostgresException { SqlState: "28000" } => $"El servidor no permite entrar con el usuario {conexion.Usuario} desde este equipo. Detalle: {ex.Message}",
            _ when EsErrorDeConexion(ex) =>
                $"No se pudo conectar con {conexion.Direccion}. Comprueba que el servidor esté encendido; si usas Docker, que Docker Desktop y el contenedor de PostgreSQL estén en marcha. Detalle: {ex.Message}",
            _ => ex.Message
        };

        private static NotSupportedException Pendiente(string funcion) =>
            new($"{funcion} todavía no está disponible; llega en una próxima etapa.");
    }
}
