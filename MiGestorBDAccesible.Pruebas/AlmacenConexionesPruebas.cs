using System.Text;
using MiGestorBDAccesible.Core.Conexiones;

namespace MiGestorBDAccesible.Pruebas
{
    public class AlmacenConexionesPruebas : IDisposable
    {
        private const string NombreCopia = "conexiones.json.danado-20261102-153045";

        private readonly string _carpeta;
        private readonly string _ruta;

        public AlmacenConexionesPruebas()
        {
            _carpeta = Path.Combine(Path.GetTempPath(), $"mgbda-{Guid.NewGuid():N}");
            Directory.CreateDirectory(_carpeta);
            _ruta = Path.Combine(_carpeta, "conexiones.json");
        }

        public void Dispose()
        {
            Directory.Delete(_carpeta, true);
        }

        [Theory]
        [InlineData("")]
        [InlineData("[{\"Nombre\": \"trunc")]
        [InlineData("{\"Nombre\": \"no es un arreglo\"}")]
        public async Task AC1_ArchivoNoValidoSeApartaComoCopiaYLaListaQuedaVacia(string contenido)
        {
            var original = Escribir(contenido);

            var resultado = await Almacen().CargarAsync();

            Assert.Equal(EstadoCargaConexiones.Danado, resultado.Estado);
            Assert.Empty(resultado.Conexiones);
            Assert.False(File.Exists(_ruta));
            var copia = Path.Combine(_carpeta, NombreCopia);
            Assert.Equal(copia, resultado.RutaCopia);
            Assert.Equal(original, File.ReadAllBytes(copia));
        }

        [Fact]
        public async Task AC1_ListaAsyncNoLanzaConArchivoDanado()
        {
            Escribir("[{\"Nombre\": \"trunc");

            var conexiones = await Almacen().ListaAsync();

            Assert.Empty(conexiones);
        }

        [Fact]
        public async Task AC1_NoSobrescribeUnaCopiaAnteriorConElMismoNombre()
        {
            var original = Escribir("[{\"Nombre\": \"trunc");
            var anterior = Path.Combine(_carpeta, NombreCopia);
            var contenidoAnterior = Encoding.UTF8.GetBytes("copia anterior");
            File.WriteAllBytes(anterior, contenidoAnterior);

            var resultado = await Almacen().CargarAsync();

            Assert.Equal(EstadoCargaConexiones.Danado, resultado.Estado);
            Assert.Equal(contenidoAnterior, File.ReadAllBytes(anterior));
            Assert.NotNull(resultado.RutaCopia);
            Assert.NotEqual(anterior, resultado.RutaCopia);
            Assert.Equal(_carpeta, Path.GetDirectoryName(resultado.RutaCopia));
            Assert.StartsWith(NombreCopia, Path.GetFileName(resultado.RutaCopia));
            Assert.Equal(original, File.ReadAllBytes(resultado.RutaCopia));
            Assert.Equal(2, Copias().Length);
        }

        [Fact]
        public async Task AC2_CargaLasLegiblesYCuentaLasDescartadas()
        {
            var original = Escribir(ArchivoParcial);

            var resultado = await Almacen().CargarAsync();

            Assert.Equal(EstadoCargaConexiones.Parcial, resultado.Estado);
            Assert.Equal(["alfa", "beta"], resultado.Conexiones.Select(c => c.Nombre));
            Assert.Equal(2, resultado.Descartadas);
            var copia = Path.Combine(_carpeta, NombreCopia);
            Assert.Equal(copia, resultado.RutaCopia);
            Assert.Equal(original, File.ReadAllBytes(copia));
        }

        [Fact]
        public async Task AC2_LaSegundaCargaNoRepiteLaCopia()
        {
            Escribir(ArchivoParcial);
            await Almacen().CargarAsync();

            var resultado = await Almacen().CargarAsync();

            Assert.Equal(EstadoCargaConexiones.Correcta, resultado.Estado);
            Assert.Equal(["alfa", "beta"], resultado.Conexiones.Select(c => c.Nombre));
            Assert.Equal(0, resultado.Descartadas);
            Assert.Null(resultado.RutaCopia);
            Assert.Single(Copias());
        }

        [Fact]
        public async Task AC3_GuardarTrasArchivoDanadoConservaLaCopia()
        {
            var original = Escribir("[{\"Nombre\": \"trunc");
            var almacen = Almacen();
            await almacen.CargarAsync();

            await almacen.GuardarAsync(ConexionWindows("nueva"), null);
            var resultado = await almacen.CargarAsync();

            Assert.Equal(EstadoCargaConexiones.Correcta, resultado.Estado);
            Assert.Equal("nueva", Assert.Single(resultado.Conexiones).Nombre);
            Assert.Equal(original, File.ReadAllBytes(Assert.Single(Copias())));
        }

        [Fact]
        public async Task AC3_GuardarTrasCargaParcialConservaLegiblesYCopia()
        {
            var original = Escribir(ArchivoParcial);
            var almacen = Almacen();
            await almacen.CargarAsync();

            await almacen.GuardarAsync(ConexionWindows("delta"), null);
            var resultado = await almacen.CargarAsync();

            Assert.Equal(EstadoCargaConexiones.Correcta, resultado.Estado);
            Assert.Equal(["alfa", "beta", "delta"], resultado.Conexiones.Select(c => c.Nombre));
            Assert.Equal(original, File.ReadAllBytes(Assert.Single(Copias())));
        }

        [Fact]
        public async Task AC4_ErrorDeLecturaNoMueveElArchivoYRechazaGuardar()
        {
            var original = Escribir(ArchivoValido);
            var almacen = Almacen();
            ResultadoCargaConexiones resultado;

            using (new FileStream(_ruta, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                resultado = await almacen.CargarAsync();
                await Assert.ThrowsAnyAsync<IOException>(() => almacen.GuardarAsync(ConexionWindows("nueva"), null));
            }

            Assert.Equal(EstadoCargaConexiones.ErrorLectura, resultado.Estado);
            Assert.IsAssignableFrom<IOException>(resultado.Error);
            Assert.Empty(resultado.Conexiones);
            Assert.Null(resultado.RutaCopia);
            Assert.Equal(original, File.ReadAllBytes(_ruta));
            Assert.Empty(Copias());
        }

        [Fact]
        public async Task AC5_FalloAMitadDeEscrituraConservaElContenidoAnterior()
        {
            var original = Escribir(ArchivoValido);
            var almacen = new AlmacenQueFallaAlEscribir(_ruta);

            await Assert.ThrowsAnyAsync<IOException>(() => almacen.GuardarAsync(ConexionWindows("nueva"), null));

            Assert.Equal(original, File.ReadAllBytes(_ruta));
            var resultado = await Almacen().CargarAsync();
            Assert.Equal(EstadoCargaConexiones.Correcta, resultado.Estado);
            Assert.Equal("alfa", Assert.Single(resultado.Conexiones).Nombre);
        }

        private static string Conexion(string nombre, string motor) => $$"""
            {
              "Id": "{{Guid.NewGuid()}}",
              "Nombre": "{{nombre}}",
              "Motor": "{{motor}}",
              "Servidor": "localhost",
              "Puerto": null,
              "Autenticacion": "Windows",
              "Usuario": null,
              "ContrasenaCifrada": null,
              "ConfiarCertificado": true,
              "BaseDatosInicial": null
            }
            """;

        private static string ArchivoValido => $"[{Conexion("alfa", "SqlServer")}]";

        // Dos conexiones legibles, una de un motor que esta versión no conoce y
        // un elemento que no es un objeto.
        private static string ArchivoParcial =>
            $"[{Conexion("beta", "PostgreSql")}, {Conexion("gamma", "Firebird")}, {Conexion("alfa", "Oracle")}, 42]";

        private static ConexionModelo ConexionWindows(string nombre) => new()
        {
            Nombre = nombre,
            Autenticacion = TipoAutenticacion.Windows
        };

        private AlmacenConexionesService Almacen() => new(_ruta, new RelojFijo());

        private byte[] Escribir(string contenido)
        {
            var bytes = Encoding.UTF8.GetBytes(contenido);
            File.WriteAllBytes(_ruta, bytes);
            return bytes;
        }

        private string[] Copias() => Directory.GetFiles(_carpeta, "conexiones.json.danado-*");

        private sealed class RelojFijo : TimeProvider
        {
            public override DateTimeOffset GetUtcNow() => new(2026, 11, 2, 15, 30, 45, TimeSpan.Zero);

            public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
        }

        private sealed class AlmacenQueFallaAlEscribir(string rutaArchivo) : AlmacenConexionesService(rutaArchivo)
        {
            protected override Stream AbrirParaEscribir(string ruta) => new FlujoQueFalla(ruta);
        }

        // Escribe los primeros bytes en el archivo y luego falla, como un corte a
        // mitad de guardado.
        private sealed class FlujoQueFalla(string ruta) : FileStream(ruta, FileMode.Create, FileAccess.Write, FileShare.None)
        {
            private const int Limite = 16;
            private int _escritos;

            public override void Write(byte[] datos, int inicio, int cantidad)
            {
                var permitidos = Math.Min(cantidad, Limite - _escritos);
                base.Write(datos, inicio, permitidos);
                base.Flush();
                _escritos += permitidos;

                if (permitidos < cantidad)
                {
                    throw new IOException("Fallo simulado a mitad de escritura.");
                }
            }

            public override void Write(ReadOnlySpan<byte> datos) => Write(datos.ToArray(), 0, datos.Length);

            public override Task WriteAsync(byte[] datos, int inicio, int cantidad, CancellationToken cancelacion)
            {
                Write(datos.AsSpan(inicio, cantidad));
                return Task.CompletedTask;
            }

            public override ValueTask WriteAsync(ReadOnlyMemory<byte> datos, CancellationToken cancelacion = default)
            {
                Write(datos.Span);
                return ValueTask.CompletedTask;
            }

            public override void WriteByte(byte valor) => Write([valor]);
        }
    }
}
