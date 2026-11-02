using MiGestorBDAccesible.Core.Conexiones;
using MiGestorBDAccesible.Core.Definicion;
using MiGestorBDAccesible.Core.Ejecucion;
using MiGestorBDAccesible.Core.Estructura;
using Oracle.ManagedDataAccess.Client;
using System.Diagnostics;

namespace MiGestorBDAccesible.Core.Proveedores.Oracle
{
    public partial class ProveedorOracle : IProveedorBaseDatos
    {
        private static readonly CapacidadesMotorModelo _capacidades = new()
        {
            ContenedoresTitulo = "Esquemas",
            ContenedoresSistemaTitulo = "Esquemas del sistema",
            PuedeCrearContenedores = false,
            VersionPrincipalMinima = 19,
            NombreVersionMinima = "Oracle 19c",
            TienePapelera = true,
            TiposObjeto = [TipoObjeto.Vista, TipoObjeto.Procedimiento, TipoObjeto.Funcion, TipoObjeto.Paquete, TipoObjeto.Secuencia],
            TituloDisparadoresBase = "Disparadores del esquema",
            IndiceIncluye = false,
            IndiceFiltrado = false,
            ReglasAlEliminar = [ReglaReferencial.SinAccion, ReglaReferencial.Cascada, ReglaReferencial.EstablecerNulo],
            ReglasAlActualizar = [],
            Terminos = new TerminosMotorModelo
            {
                Identidad = (semilla, incremento) => (semilla ?? 1) == 1 && (incremento ?? 1) == 1
                    ? "GENERATED AS IDENTITY"
                    : $"GENERATED AS IDENTITY (START WITH {semilla} INCREMENT BY {incremento})",
                TieneIndicesAgrupados = false,
                TieneReglasAlActualizar = false,
                LlaveDeshabilitada = "DISABLE",
                ColumnaCalculada = "GENERATED ALWAYS AS … VIRTUAL",
                NombreIdentidad = "GENERATED AS IDENTITY",
                EjemploTiposIdentidad = "NUMBER o INTEGER",
                EjemploPredeterminado = "0, 1, 'texto' o una función como SYSDATE",
                ComprobarExistentes = "ENABLE VALIDATE",
                SeparadorNombres = "_",
                NombreId = "ID",
                TipoId = "NUMBER",
                TipoTexto = "VARCHAR2",
                ModificarColumna = "MODIFY",
                TiposCompatibles = TiposCompatiblesOracle
            }
        };

        private static readonly HashSet<int> _erroresConexion = [3113, 3114, 3135, 12170, 12514, 12528, 12535, 12537, 12541, 12543, 12545, 12560, 12571, 50000];

        public TipoMotor Motor => TipoMotor.Oracle;

        public CapacidadesMotorModelo Capacidades => _capacidades;

        public string BaseDatosAdministracion => "";

        public string EsquemaPredeterminado(ConexionActiva conexion) => (conexion.Conexion.Usuario ?? "").Trim().ToUpperInvariant();

        public async Task<ResultadoPruebaConexion> ProbarConexionAsync(
            ConexionModelo conexion,
            string? contrasena,
            CancellationToken cancelacion = default)
        {
            var cronometro = Stopwatch.StartNew();

            try
            {
                await using var conexionOracle = new OracleConnection(CadenaConexion(conexion, contrasena));
                await conexionOracle.OpenAsync(cancelacion);

                var (producto, version) = await LeerVersionAsync(conexionOracle, cancelacion);
                var (nombre, edicion) = SepararEdicion(producto);
                var versionPrincipal = int.TryParse(version?.Split('.')[0], out var principal) ? principal : 0;

                return new ResultadoPruebaConexion(true, cronometro.Elapsed, nombre, edicion, version,
                    Advertencia: _capacidades.AvisoVersion(versionPrincipal));
            }
            catch (OracleException ex)
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
            await using var conexionOracle = await AbrirAsync(conexion, cancelacion);
            await using var comando = conexionOracle.CreateCommand();
            comando.CommandText = "SELECT username, oracle_maintained FROM all_users ORDER BY username";

            await using var lector = await comando.ExecuteReaderAsync(cancelacion);
            var esquemas = new List<BaseDatosModelo>();

            while (await lector.ReadAsync(cancelacion))
            {
                esquemas.Add(new BaseDatosModelo
                {
                    Nombre = lector.GetString(0),
                    EsSistema = !lector.IsDBNull(1) && lector.GetString(1) == "Y"
                });
            }

            return esquemas;
        }

        public async Task<List<TablaModelo>> ObtenerTablasAsync(
            ConexionActiva conexion,
            string baseDatos,
            CancellationToken cancelacion = default)
        {
            await using var conexionOracle = await AbrirAsync(conexion, cancelacion);
            await using var comando = conexionOracle.CreateCommand();
            comando.BindByName = true;
            comando.CommandText = """
                SELECT owner, table_name
                FROM all_tables
                WHERE owner = :esquema
                  AND nested = 'NO'
                  AND secondary = 'N'
                  AND dropped = 'NO'
                  AND table_name NOT LIKE 'BIN$%'
                ORDER BY table_name
                """;
            comando.Parameters.Add("esquema", baseDatos);

            await using var lector = await comando.ExecuteReaderAsync(cancelacion);
            var tablas = new List<TablaModelo>();

            while (await lector.ReadAsync(cancelacion))
            {
                tablas.Add(new TablaModelo { Esquema = lector.GetString(0), Nombre = lector.GetString(1), EsquemaPredeterminado = baseDatos });
            }

            return tablas;
        }

        public string ScriptCrearBaseDatos(string nombre) => throw Pendiente("Crear esquemas");

        public string ScriptEliminarBaseDatos(string nombre) => throw Pendiente("Eliminar esquemas");

        public void ReiniciarConexiones() => OracleConnection.ClearAllPools();

        public static string CadenaConexion(ConexionModelo conexion, string? contrasena)
        {
            var constructor = new OracleConnectionStringBuilder
            {
                DataSource = $"//{conexion.Servidor}:{conexion.Puerto ?? TipoMotor.Oracle.PuertoPredeterminado()}/{conexion.NombreServicio}",
                UserID = conexion.Usuario ?? "",
                Password = contrasena ?? "",
                ConnectionTimeout = 6
            };

            return constructor.ConnectionString;
        }

        private static async Task<OracleConnection> AbrirAsync(ConexionActiva conexion, CancellationToken cancelacion)
        {
            for (var intento = 1; ; intento++)
            {
                var conexionOracle = new OracleConnection(CadenaConexion(conexion.Conexion, conexion.Contrasena));

                try
                {
                    await conexionOracle.OpenAsync(cancelacion);
                    return conexionOracle;
                }
                catch (OracleException ex) when (_erroresConexion.Contains(ex.Number))
                {
                    OracleConnection.ClearPool(conexionOracle);
                    await conexionOracle.DisposeAsync();

                    if (intento >= 2)
                    {
                        throw new ConexionPerdidaException(MensajeError(conexion.Conexion, ex), ex);
                    }

                    await Task.Delay(500, cancelacion);
                }
                catch
                {
                    await conexionOracle.DisposeAsync();
                    throw;
                }
            }
        }

        private static string MensajeError(ConexionModelo conexion, OracleException ex) => ex.Number switch
        {
            1017 => $"Usuario o contraseña incorrectos. Detalle: {ex.Message}",
            12514 => $"El servidor responde, pero no conoce el servicio {conexion.NombreServicio}. Revisa el nombre de servicio; en Oracle Free suele ser FREEPDB1. Detalle: {ex.Message}",
            28000 => $"La cuenta {conexion.Usuario} está bloqueada. Detalle: {ex.Message}",
            _ when _erroresConexion.Contains(ex.Number) =>
                $"No se pudo conectar con {conexion.Direccion}. Comprueba que el servidor esté encendido; si usas Docker, que Docker Desktop y el contenedor de Oracle estén en marcha. Detalle: {ex.Message}",
            _ => ex.Message
        };

        private static async Task<(string Producto, string? Version)> LeerVersionAsync(OracleConnection conexion, CancellationToken cancelacion)
        {
            foreach (var columna in new[] { "version_full", "version" })
            {
                try
                {
                    await using var comando = conexion.CreateCommand();
                    comando.CommandText = $"""
                        SELECT product, {columna}
                        FROM product_component_version
                        WHERE product LIKE 'Oracle%'
                        ORDER BY product
                        """;

                    await using var lector = await comando.ExecuteReaderAsync(cancelacion);

                    if (await lector.ReadAsync(cancelacion))
                    {
                        return (lector.IsDBNull(0) ? "Oracle" : lector.GetString(0).Trim(), lector.IsDBNull(1) ? null : lector.GetString(1));
                    }
                }
                catch (OracleException ex) when (ex.Number == 904)
                {
                }
            }

            return ("Oracle", null);
        }

        private static (string Nombre, string Edicion) SepararEdicion(string producto)
        {
            foreach (var edicion in new[] { "Free", "Enterprise Edition", "Standard Edition 2", "Standard Edition", "Express Edition" })
            {
                if (producto.EndsWith($" {edicion}", StringComparison.OrdinalIgnoreCase))
                {
                    return (producto[..^(edicion.Length + 1)], edicion == "Free" ? "edición Free" : edicion);
                }
            }

            return (producto, "edición desconocida");
        }

        private static NotSupportedException Pendiente(string funcion) =>
            new($"{funcion} todavía no está disponible; llega en una próxima etapa.");
    }
}
