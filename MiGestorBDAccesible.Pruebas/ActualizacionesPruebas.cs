using System.Security.Cryptography;
using System.Text;
using MiGestorBDAccesible.Core.Actualizaciones;

namespace MiGestorBDAccesible.Pruebas
{
    public class ActualizacionesPruebas : IDisposable
    {
        private const string Respuesta = """
            {
              "tag_name": "v1.2",
              "name": "Versión 1.2",
              "body": "## Qué trae\n\n- MySQL.",
              "html_url": "https://github.com/WilmerRv1989/MiGestorBDAccesible/releases/tag/v1.2",
              "assets": [
                { "name": "MiGestorBDAccesible-1.2-manual.html", "size": 10, "browser_download_url": "https://x/manual.html", "digest": "sha256:aa" },
                { "name": "MiGestorBDAccesible-1.2-instalador.exe", "size": 52, "browser_download_url": "https://x/instalador.exe", "digest": "sha256:bb" }
              ]
            }
            """;

        private readonly string _carpeta = Path.Combine(Path.GetTempPath(), $"mgbda-act-{Guid.NewGuid():N}");

        public ActualizacionesPruebas()
        {
            Directory.CreateDirectory(_carpeta);
        }

        public void Dispose()
        {
            Directory.Delete(_carpeta, true);
        }

        [Fact]
        public void LeeLaVersionPublicadaYEligeElInstalador()
        {
            var publicada = ActualizacionesService.Leer(Respuesta);

            Assert.Equal(new Version(1, 2, 0), publicada.Version);
            Assert.Equal("1.2", publicada.VersionCorta);
            Assert.Equal("Versión 1.2", publicada.Titulo);
            Assert.Contains("MySQL", publicada.Notas);
            Assert.Equal(new InstaladorPublicadoModelo("MiGestorBDAccesible-1.2-instalador.exe", "https://x/instalador.exe", 52, "sha256:bb"), publicada.Instalador);
        }

        [Theory]
        [InlineData("v1.2", "1.1.0", true)]
        [InlineData("v1.10", "1.9.0", true)]
        [InlineData("v1.1", "1.1.0", false)]
        [InlineData("v1.0", "1.1.0", false)]
        [InlineData("v2.0.1", "2.0.0", true)]
        public void ComparaVersiones(string etiqueta, string actual, bool esMasNueva)
        {
            var publicada = new VersionPublicadaModelo { Etiqueta = etiqueta, Version = ActualizacionesService.LeerVersion(etiqueta)! };

            Assert.Equal(esMasNueva, ActualizacionesService.EsMasNueva(publicada, actual));
        }

        [Fact]
        public void BuscaComoMuchoUnaVezAlDiaYRespetaLaCasilla()
        {
            var ahora = new DateTimeOffset(2026, 11, 3, 10, 0, 0, TimeSpan.Zero);

            Assert.True(ActualizacionesService.DebeBuscar(new PreferenciasModelo(), ahora));
            Assert.False(ActualizacionesService.DebeBuscar(new PreferenciasModelo { UltimaBusqueda = ahora.AddHours(-23) }, ahora));
            Assert.True(ActualizacionesService.DebeBuscar(new PreferenciasModelo { UltimaBusqueda = ahora.AddHours(-25) }, ahora));
            Assert.False(ActualizacionesService.DebeBuscar(new PreferenciasModelo { BuscarAlIniciar = false }, ahora));
        }

        [Fact]
        public void NoAvisaDeLaVersionOmitida()
        {
            var publicada = ActualizacionesService.Leer(Respuesta);

            Assert.True(ActualizacionesService.DebeAvisar(publicada, "1.1.0", new PreferenciasModelo()));
            Assert.False(ActualizacionesService.DebeAvisar(publicada, "1.1.0", new PreferenciasModelo { VersionOmitida = "v1.2" }));
            Assert.Equal("Hay una versión nueva de Mi Gestor BD Accesible. Ábrela con el menú Ayuda, Buscar actualizaciones. Versión 1.2.", ActualizacionesService.AnuncioAlIniciar(publicada));
        }

        [Fact]
        public async Task CompruebaLaHuella()
        {
            var ruta = Path.Combine(_carpeta, "instalador.exe");
            var contenido = Encoding.UTF8.GetBytes("instalador de prueba");
            await File.WriteAllBytesAsync(ruta, contenido);
            var huella = "sha256:" + Convert.ToHexString(SHA256.HashData(contenido)).ToLowerInvariant();

            Assert.True(await ActualizacionesService.HuellaCorrectaAsync(ruta, huella));
            Assert.False(await ActualizacionesService.HuellaCorrectaAsync(ruta, "sha256:" + new string('0', 64)));
            Assert.False(await ActualizacionesService.HuellaCorrectaAsync(ruta, null));
        }

        [Fact]
        public async Task LasPreferenciasSeGuardanYToleranUnArchivoDanado()
        {
            var ruta = Path.Combine(_carpeta, "preferencias.json");
            var servicio = new PreferenciasService(ruta);

            Assert.True((await servicio.LeerAsync()).BuscarAlIniciar);

            await servicio.CambiarAsync(p => { p.BuscarAlIniciar = false; p.VersionOmitida = "v1.2"; });
            var leidas = await new PreferenciasService(ruta).LeerAsync();
            Assert.False(leidas.BuscarAlIniciar);
            Assert.Equal("v1.2", leidas.VersionOmitida);

            await File.WriteAllTextAsync(ruta, "dañado");
            Assert.True((await servicio.LeerAsync()).BuscarAlIniciar);
        }
    }
}
