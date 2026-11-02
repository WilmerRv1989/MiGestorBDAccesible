using MiGestorBDAccesible.Core.Conexiones;
using Microsoft.Data.SqlClient;
using System.Diagnostics;

namespace MiGestorBDAccesible.Core.Proveedores.SqlServer
{
    public partial class ProveedorSqlServer : IProveedorBaseDatos
    {
        public TipoMotor Motor => TipoMotor.SqlServer;

        public CapacidadesMotorModelo Capacidades => CapacidadesMotorModelo.SqlServer;

        public string EsquemaPredeterminado(ConexionActiva conexion) => "dbo";

        public async Task<ResultadoPruebaConexion> ProbarConexionAsync(
            ConexionModelo conexion,
            string? contrasena,
            CancellationToken cancelacion = default)
        {
            var cronometro = Stopwatch.StartNew();

            try
            {
                await using var conexionSql = new SqlConnection(CadenaConexion(conexion, contrasena));
                await conexionSql.OpenAsync(cancelacion);

                await using var comando = conexionSql.CreateCommand();
                comando.CommandText = """
                    SELECT
                        CAST(SERVERPROPERTY('ProductMajorVersion') AS int),
                        CAST(SERVERPROPERTY('ProductVersion') AS nvarchar(128)),
                        CAST(SERVERPROPERTY('Edition') AS nvarchar(128))
                    """;

                await using var lector = await comando.ExecuteReaderAsync(cancelacion);
                await lector.ReadAsync(cancelacion);

                var version = lector.IsDBNull(1) ? null : lector.GetString(1);
                var versionPrincipal = lector.IsDBNull(0) ? VersionPrincipal(version) : lector.GetInt32(0);
                var edicion = lector.IsDBNull(2) ? null : lector.GetString(2);

                return new ResultadoPruebaConexion(
                    true,
                    cronometro.Elapsed,
                    NombreProducto(versionPrincipal),
                    edicion,
                    version,
                    Advertencia: Capacidades.AvisoVersion(versionPrincipal));
            }
            catch (SqlException ex)
            {
                var mensaje = EsErrorDeConexion(ex) ? MensajeConexion(conexion, ex) : ex.Message;
                return new ResultadoPruebaConexion(false, cronometro.Elapsed, Error: mensaje);
            }
            catch (InvalidOperationException ex)
            {
                return new ResultadoPruebaConexion(false, cronometro.Elapsed, Error: ex.Message);
            }
        }

        public static string CadenaConexion(ConexionModelo conexion, string? contrasena, string? baseDatos = null)
        {
            var constructor = new SqlConnectionStringBuilder
            {
                DataSource = conexion.Puerto.HasValue
                    ? $"{conexion.Servidor},{conexion.Puerto}"
                    : conexion.Servidor,
                TrustServerCertificate = conexion.ConfiarCertificado,
                ConnectTimeout = 6,
                ApplicationName = "MiGestorBDAccesible"
            };

            var catalogo = baseDatos ?? conexion.BaseDatosInicial;

            if (!string.IsNullOrWhiteSpace(catalogo))
            {
                constructor.InitialCatalog = catalogo;
            }

            if (conexion.Autenticacion == TipoAutenticacion.Windows)
            {
                constructor.IntegratedSecurity = true;
            }
            else
            {
                constructor.UserID = conexion.Usuario ?? "";
                constructor.Password = contrasena ?? "";
            }

            return constructor.ConnectionString;
        }

        private static int VersionPrincipal(string? version) =>
            int.TryParse(version?.Split('.')[0], out var principal) ? principal : 0;

        private static string NombreProducto(int versionPrincipal) => versionPrincipal switch
        {
            17 => "SQL Server 2025",
            16 => "SQL Server 2022",
            15 => "SQL Server 2019",
            14 => "SQL Server 2017",
            13 => "SQL Server 2016",
            _ => "SQL Server"
        };
    }
}
