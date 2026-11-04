using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using MiGestorBDAccesible.Core.Conexiones;

namespace MiGestorBDAccesible.Pruebas
{
    public class ContrasenasPortablesPruebas : IDisposable
    {
        private const string Contrasena = "Prueba-Contrasena-2026";

        private const UnixFileMode SoloPropietario = UnixFileMode.UserRead | UnixFileMode.UserWrite;

        private readonly string _carpeta;
        private readonly string _ruta;
        private readonly string _claves;

        public ContrasenasPortablesPruebas()
        {
            _carpeta = Path.Combine(Path.GetTempPath(), $"mgbda-{Guid.NewGuid():N}");
            Directory.CreateDirectory(_carpeta);
            _ruta = Path.Combine(_carpeta, "conexiones.json");
            _claves = Path.Combine(_carpeta, "claves");
        }

        public void Dispose()
        {
            Directory.Delete(_carpeta, true);
        }

        [Fact]
        public async Task AC2_GuardarYLeerDevuelveLaMismaContrasena()
        {
            await new AlmacenConexionesService(_ruta).GuardarAsync(ConexionConUsuario(), Contrasena);

            var almacen = new AlmacenConexionesService(_ruta);
            var conexion = Assert.Single(await almacen.ListaAsync());

            Assert.Equal(ContrasenaGuardada.Disponible(Contrasena), almacen.ObtenerContrasena(conexion));
        }

        [Fact]
        public async Task AC2_ElArchivoNoContieneLaContrasenaEnClaro()
        {
            await new AlmacenConexionesService(_ruta).GuardarAsync(ConexionConUsuario(), Contrasena);

            var conexion = Assert.Single(await new AlmacenConexionesService(_ruta).ListaAsync());
            var archivo = await File.ReadAllTextAsync(_ruta);

            Assert.False(string.IsNullOrEmpty(conexion.ContrasenaCifrada));
            Assert.DoesNotContain(Contrasena, archivo);
            Assert.DoesNotContain(Convert.ToBase64String(Encoding.UTF8.GetBytes(Contrasena)), archivo);
        }

        [Fact]
        public void AC2_LaContrasenaNoApareceAlMostrarElResultado()
        {
            Assert.DoesNotContain(Contrasena, ContrasenaGuardada.Disponible(Contrasena).ToString());
        }

        [FactSoloMacOS]
        public async Task AC2_SinElAnilloDeClavesNoSePuedeLeer()
        {
            await new AlmacenConexionesService(_ruta).GuardarAsync(ConexionConUsuario(), Contrasena);
            Directory.Delete(_claves, true);

            var almacen = new AlmacenConexionesService(_ruta);
            var conexion = Assert.Single(await almacen.ListaAsync());

            Assert.Equal(ContrasenaGuardada.NoDescifrable, almacen.ObtenerContrasena(conexion));
        }

        [FactSoloWindows]
        [SupportedOSPlatform("windows")]
        public async Task AC3_LeeContrasenasCifradasConDpapiPorLaVersion10()
        {
            var cifrada = Convert.ToBase64String(ProtectedData.Protect(
                Encoding.UTF8.GetBytes(Contrasena),
                null,
                DataProtectionScope.CurrentUser));
            var original = Encoding.UTF8.GetBytes($$"""
                [
                  {
                    "Id": "7f2c1d5e-0000-4000-8000-000000000002",
                    "Nombre": "practica",
                    "Motor": "SqlServer",
                    "Servidor": "localhost",
                    "Puerto": 1433,
                    "Autenticacion": "UsuarioYContrasena",
                    "Usuario": "sa",
                    "ContrasenaCifrada": "{{cifrada}}",
                    "ConfiarCertificado": true,
                    "BaseDatosInicial": null,
                    "NombreServicio": null
                  }
                ]
                """);
            await File.WriteAllBytesAsync(_ruta, original);

            var almacen = new AlmacenConexionesService(_ruta);
            var conexion = Assert.Single(await almacen.ListaAsync());

            Assert.Equal(ContrasenaGuardada.Disponible(Contrasena), almacen.ObtenerContrasena(conexion));
            Assert.Equal(original, await File.ReadAllBytesAsync(_ruta));
            Assert.Equal([_ruta], Directory.GetFileSystemEntries(_carpeta));
        }

        [FactSoloWindows]
        [SupportedOSPlatform("windows")]
        public async Task AC3_GuardarEnWindowsSigueUsandoDpapi()
        {
            await new AlmacenConexionesService(_ruta).GuardarAsync(ConexionConUsuario(), Contrasena);

            var conexion = Assert.Single(await new AlmacenConexionesService(_ruta).ListaAsync());
            var datos = ProtectedData.Unprotect(
                Convert.FromBase64String(conexion.ContrasenaCifrada!),
                null,
                DataProtectionScope.CurrentUser);

            Assert.Equal(Contrasena, Encoding.UTF8.GetString(datos));
        }

        [FactSoloMacOS]
        [SupportedOSPlatform("macos")]
        public async Task AC4_LaPrimeraContrasenaCreaLasClavesSoloParaElPropietario()
        {
            await new AlmacenConexionesService(_ruta).GuardarAsync(ConexionConUsuario(), Contrasena);

            ComprobarClavesSoloParaElPropietario();
        }

        [FactSoloMacOS]
        [SupportedOSPlatform("macos")]
        public async Task AC4_CorrigeLosPermisosDeUnaCarpetaDeClavesQueYaExistia()
        {
            Directory.CreateDirectory(_claves);
            File.SetUnixFileMode(_claves, SoloPropietario | UnixFileMode.UserExecute
                | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
                | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);

            await new AlmacenConexionesService(_ruta).GuardarAsync(ConexionConUsuario(), Contrasena);

            ComprobarClavesSoloParaElPropietario();
        }

        [Theory]
        [InlineData("AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8=")]
        [InlineData("no es base64")]
        public void AC5_ContrasenaQueNoSePuedeDescifrarDaNoDescifrable(string cifrada)
        {
            var conexion = ConexionConUsuario();
            conexion.ContrasenaCifrada = cifrada;

            Assert.Equal(ContrasenaGuardada.NoDescifrable, new AlmacenConexionesService(_ruta).ObtenerContrasena(conexion));
        }

        [FactSoloMacOS]
        public async Task AC5_UnAnilloDeClavesDanadoDaNoDescifrable()
        {
            await new AlmacenConexionesService(_ruta).GuardarAsync(ConexionConUsuario(), Contrasena);
            foreach (var archivo in Directory.GetFiles(_claves))
            {
                await File.WriteAllTextAsync(archivo, "<esto no es xml");
            }

            var almacen = new AlmacenConexionesService(_ruta);
            var conexion = Assert.Single(await almacen.ListaAsync());

            Assert.Equal(ContrasenaGuardada.NoDescifrable, almacen.ObtenerContrasena(conexion));
        }

        [FactSoloWindows]
        [SupportedOSPlatform("windows")]
        public void AC5_UnBloqueDpapiAjenoDaNoDescifrable()
        {
            var conexion = ConexionConUsuario();
            conexion.ContrasenaCifrada = Convert.ToBase64String(ProtectedData.Protect(
                Encoding.UTF8.GetBytes(Contrasena),
                [1, 2, 3],
                DataProtectionScope.CurrentUser));

            Assert.Equal(ContrasenaGuardada.NoDescifrable, new AlmacenConexionesService(_ruta).ObtenerContrasena(conexion));
        }

        [FactSoloWindows]
        [SupportedOSPlatform("windows")]
        public void AC5_LoCifradoEnUnSistemaNoSeLeeEnElOtro()
        {
            var dpapi = new ProtectorDpapi();
            var dataProtection = new ProtectorDataProtection(_claves);

            Assert.Equal(ContrasenaGuardada.NoDescifrable, dpapi.Desproteger(dataProtection.Proteger(Contrasena)));
            Assert.Equal(ContrasenaGuardada.NoDescifrable, dataProtection.Desproteger(dpapi.Proteger(Contrasena)));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void AC5_SinContrasenaGuardadaNoEsUnError(string? cifrada)
        {
            var conexion = ConexionConUsuario();
            conexion.ContrasenaCifrada = cifrada;

            Assert.Equal(ContrasenaGuardada.SinContrasena, new AlmacenConexionesService(_ruta).ObtenerContrasena(conexion));
        }

        [Fact]
        public void AC5_ElAvisoPideVolverAEscribirla()
        {
            Assert.Equal("La contraseña guardada no se puede usar en este equipo. Vuelve a escribirla.", ContrasenaGuardada.AvisoNoDescifrable);
        }

        [SupportedOSPlatform("macos")]
        private void ComprobarClavesSoloParaElPropietario()
        {
            Assert.True(Directory.Exists(_claves));
            Assert.Equal(SoloPropietario | UnixFileMode.UserExecute, File.GetUnixFileMode(_claves));

            var archivos = Directory.GetFiles(_claves);
            Assert.NotEmpty(archivos);
            Assert.All(archivos, archivo => Assert.Equal(SoloPropietario, File.GetUnixFileMode(archivo)));
        }

        private static ConexionModelo ConexionConUsuario() => new()
        {
            Nombre = "practica",
            Usuario = "sa"
        };
    }
}
