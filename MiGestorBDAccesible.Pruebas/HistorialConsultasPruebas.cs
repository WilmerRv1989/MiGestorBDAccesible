using MiGestorBDAccesible.Core.Trabajo;

namespace MiGestorBDAccesible.Pruebas
{
    public class HistorialConsultasPruebas : IDisposable
    {
        private readonly string _carpeta = Path.Combine(Path.GetTempPath(), $"historial-{Guid.NewGuid():N}");

        public HistorialConsultasPruebas()
        {
            Directory.CreateDirectory(_carpeta);
        }

        public void Dispose()
        {
            Directory.Delete(_carpeta, true);
        }

        private static string Script(int lineas) =>
            string.Join('\n', Enumerable.Range(1, lineas).Select(i => $"INSERT INTO pokemon (id, nombre) VALUES ({i}, 'Pokémon número {i}');"));

        [Fact]
        public void UnaConsultaNormalSeGuardaCompleta()
        {
            var entrada = new EntradaHistorialModelo { Texto = "SELECT * FROM pokemon" };

            var guardada = HistorialConsultasService.Recortar(entrada);

            Assert.Same(entrada, guardada);
            Assert.False(guardada.Recortado);
        }

        [Fact]
        public void DeUnScriptGrandeSeGuardaSoloElInicioEnLineasCompletas()
        {
            var texto = Script(2000);
            var conexion = Guid.NewGuid();

            var guardada = HistorialConsultasService.Recortar(new EntradaHistorialModelo { ConexionId = conexion, BaseDatos = "pokemon", Texto = texto, Resumen = "2000 filas." });

            Assert.True(guardada.Recortado);
            Assert.Equal(2000, guardada.LineasOriginales);
            Assert.True(guardada.Texto.Length <= HistorialConsultasService.LongitudInicio);
            Assert.StartsWith(guardada.Texto, texto);
            Assert.EndsWith(");", guardada.Texto);
            Assert.Equal(conexion, guardada.ConexionId);
            Assert.Equal("pokemon", guardada.BaseDatos);
            Assert.Equal("2000 filas.", guardada.Resumen);
        }

        [Fact]
        public async Task ElArchivoDelHistorialNoCreceConUnScriptGrande()
        {
            var ruta = Path.Combine(_carpeta, "historial.json");
            var servicio = new HistorialConsultasService(ruta);
            var conexion = Guid.NewGuid();
            var texto = Script(20000);

            await servicio.AgregarAsync(new EntradaHistorialModelo { ConexionId = conexion, Texto = texto });
            await servicio.AgregarAsync(new EntradaHistorialModelo { ConexionId = conexion, Texto = texto });

            var entradas = await new HistorialConsultasService(ruta).ListaAsync(conexion);
            var entrada = Assert.Single(entradas);
            Assert.True(entrada.Recortado);
            Assert.Equal(20000, entrada.LineasOriginales);
            Assert.True(new FileInfo(ruta).Length < 20_000);
        }
    }
}
