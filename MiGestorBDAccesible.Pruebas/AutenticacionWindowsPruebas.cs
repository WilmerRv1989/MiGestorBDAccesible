using MiGestorBDAccesible.Core.Conexiones;
using MiGestorBDAccesible.Core.Proveedores.SqlServer;

namespace MiGestorBDAccesible.Pruebas
{
    public class AutenticacionWindowsPruebas : IDisposable
    {
        private const string Mensaje = "La autenticación de Windows solo está disponible en Windows.";

        private readonly string _carpeta;
        private readonly string _ruta;

        public AutenticacionWindowsPruebas()
        {
            _carpeta = Path.Combine(Path.GetTempPath(), $"mgbda-{Guid.NewGuid():N}");
            Directory.CreateDirectory(_carpeta);
            _ruta = Path.Combine(_carpeta, "conexiones.json");
        }

        public void Dispose()
        {
            Directory.Delete(_carpeta, true);
        }

        [FactSoloMacOS]
        public async Task AC7_ProbarConAutenticacionWindowsFallaFueraDeWindows()
        {
            var resultado = await new ProveedorSqlServer().ProbarConexionAsync(ConexionWindows(), null);

            Assert.False(resultado.Exito);
            Assert.Equal(Mensaje, resultado.Error);
        }

        [FactSoloMacOS]
        public void AC7_LaCadenaDeConexionConAutenticacionWindowsFallaFueraDeWindows()
        {
            var error = Assert.Throws<InvalidOperationException>(() => ProveedorSqlServer.CadenaConexion(ConexionWindows(), null));

            Assert.Equal(Mensaje, error.Message);
        }

        [FactSoloMacOS]
        public async Task AC7_LasConexionesConAutenticacionWindowsSiguenEnLaLista()
        {
            await File.WriteAllTextAsync(_ruta, """
                [
                  {
                    "Id": "7f2c1d5e-0000-4000-8000-000000000003",
                    "Nombre": "dominio",
                    "Motor": "SqlServer",
                    "Servidor": "servidor",
                    "Puerto": null,
                    "Autenticacion": "Windows",
                    "Usuario": null,
                    "ContrasenaCifrada": null,
                    "ConfiarCertificado": false,
                    "BaseDatosInicial": null,
                    "NombreServicio": null
                  }
                ]
                """);

            var carga = await new AlmacenConexionesService(_ruta).CargarAsync();

            Assert.Equal(EstadoCargaConexiones.Correcta, carga.Estado);
            Assert.Equal(TipoAutenticacion.Windows, Assert.Single(carga.Conexiones).Autenticacion);
        }

        [FactSoloWindows]
        public void AC8_SqlServerAdmiteAutenticacionWindowsEnWindows()
        {
            Assert.True(TipoMotor.SqlServer.AdmiteAutenticacionWindows());
        }

        [FactSoloMacOS]
        public void AC8_SqlServerNoAdmiteAutenticacionWindowsEnMacOS()
        {
            Assert.False(TipoMotor.SqlServer.AdmiteAutenticacionWindows());
        }

        private static ConexionModelo ConexionWindows() => new()
        {
            Nombre = "windows",
            Motor = TipoMotor.SqlServer,
            Servidor = "127.0.0.1",
            Puerto = 9,
            Autenticacion = TipoAutenticacion.Windows
        };
    }
}
