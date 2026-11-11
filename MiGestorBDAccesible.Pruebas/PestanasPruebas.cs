using MiGestorBDAccesible.Core.Conexiones;
using MiGestorBDAccesible.Core.Proveedores;
using MiGestorBDAccesible.Core.Trabajo;

namespace MiGestorBDAccesible.Pruebas
{
    public class PestanasPruebas
    {
        [Fact]
        public void AbrirNumeraYDejaActivaLaNueva()
        {
            var conjunto = new ConjuntoPestanasModelo();
            var primera = conjunto.Abrir("tienda");
            var segunda = conjunto.Abrir("ventas");

            Assert.Equal("Consulta 1", primera.Nombre);
            Assert.Equal("Consulta 2", segunda.Nombre);
            Assert.Same(segunda, conjunto.Activa);
            Assert.Equal(2, conjunto.Posicion(segunda));
        }

        [Fact]
        public void LaNuevaSeAbreJustoDespuesDeLaActiva()
        {
            var conjunto = new ConjuntoPestanasModelo();
            var primera = conjunto.Abrir(null);
            conjunto.Abrir(null);
            conjunto.Activar(primera);

            var tercera = conjunto.Abrir(null);

            Assert.Equal(2, conjunto.Posicion(tercera));
            Assert.Equal("Consulta 3", tercera.Nombre);
        }

        [Fact]
        public void CerrarActivaPasaALaSiguienteOALaAnterior()
        {
            var conjunto = new ConjuntoPestanasModelo();
            var primera = conjunto.Abrir(null);
            var segunda = conjunto.Abrir(null);
            var tercera = conjunto.Abrir(null);

            conjunto.Activar(segunda);
            Assert.Same(tercera, conjunto.Cerrar(segunda));
            Assert.Same(primera, conjunto.Cerrar(tercera));
            Assert.Null(conjunto.Cerrar(primera));
            Assert.Equal("Consulta 1", conjunto.Abrir(null).Nombre);
        }

        [Fact]
        public void MoverDaLaVuelta()
        {
            var conjunto = new ConjuntoPestanasModelo();
            var primera = conjunto.Abrir(null);
            var segunda = conjunto.Abrir(null);

            Assert.Same(primera, conjunto.Mover(1));
            Assert.Same(segunda, conjunto.Mover(-1));
            Assert.Same(primera, conjunto.Mover(-1));
        }

        [Fact]
        public void EtiquetaDiceNombreBaseYEstado()
        {
            var conjunto = new ConjuntoPestanasModelo();
            var pestana = conjunto.Abrir("tienda", "SELECT 1", Path.Combine(Path.GetTempPath(), "sql", "ventas.sql"));
            pestana.Texto = "SELECT 2";
            pestana.CambiosPendientes = true;

            Assert.Equal("ventas.sql, tienda, con cambios sin guardar, con cambios sin confirmar", pestana.Etiqueta);
            Assert.Equal("Primeras 100 filas de dbo.Productos, base predeterminada",
                conjunto.Abrir(null, titulo: "Primeras 100 filas de dbo.Productos").Etiqueta);
        }

        [Fact]
        public void GuardarYRecuperarConservaOrdenActivaYNumeracion()
        {
            var conjunto = new ConjuntoPestanasModelo();
            conjunto.Abrir("a", "SELECT 1");
            var segunda = conjunto.Abrir("b", "SELECT 2", Path.Combine(Path.GetTempPath(), "x.sql"));
            conjunto.Abrir("c", titulo: "Script CREATE de la vista v");
            conjunto.Activar(segunda);

            var recuperado = ConjuntoPestanasModelo.Recuperar(conjunto.Guardar(Guid.NewGuid()), r => r.EndsWith("x.sql") ? "SELECT 2" : null);

            Assert.Equal(["Consulta 1", "x.sql", "Script CREATE de la vista v"], recuperado.Pestanas.Select(p => p.Nombre));
            Assert.Equal("x.sql", recuperado.Activa!.Nombre);
            Assert.False(recuperado.Activa.Modificado);
            Assert.Equal("Consulta 4", recuperado.Abrir(null).Nombre);
        }

        [Fact]
        public void UnArchivoSinCambiosNoGuardaSuTextoEnLaSesion()
        {
            var conjunto = new ConjuntoPestanasModelo();
            var ruta = Path.Combine(Path.GetTempPath(), "poblar.sql");
            var grande = new string('x', 2_000_000);
            conjunto.Abrir("pokemon", grande, ruta);

            var guardada = Assert.Single(conjunto.Guardar(Guid.NewGuid()).Pestanas);

            Assert.True(guardada.TextoEnArchivo);
            Assert.Equal("", guardada.Texto);
            Assert.Equal("", guardada.TextoGuardado);

            var recuperada = ConjuntoPestanasModelo.Recuperar(new SesionEditorModelo { Pestanas = [guardada] }, r => r == ruta ? grande : null).Activa!;
            Assert.Equal(grande, recuperada.Texto);
            Assert.Equal(ruta, recuperada.Archivo);
            Assert.False(recuperada.Modificado);
        }

        [Fact]
        public void UnArchivoConCambiosGuardaSoloTuTexto()
        {
            var conjunto = new ConjuntoPestanasModelo();
            var ruta = Path.Combine(Path.GetTempPath(), "consulta.sql");
            var pestana = conjunto.Abrir(null, "SELECT 1", ruta);
            pestana.Texto = "SELECT 2";

            var guardada = Assert.Single(conjunto.Guardar(Guid.NewGuid()).Pestanas);

            Assert.True(guardada.OriginalEnArchivo);
            Assert.Equal("SELECT 2", guardada.Texto);
            Assert.Equal("", guardada.TextoGuardado);

            var recuperada = ConjuntoPestanasModelo.Recuperar(new SesionEditorModelo { Pestanas = [guardada] }, _ => "SELECT 1").Activa!;
            Assert.Equal("SELECT 2", recuperada.Texto);
            Assert.True(recuperada.Modificado);
        }

        [Fact]
        public void UnArchivoQueYaNoExisteDejaLaPestanaVaciaYAvisa()
        {
            var conjunto = new ConjuntoPestanasModelo();
            conjunto.Abrir("pokemon", "SELECT 1", Path.Combine(Path.GetTempPath(), "borrado.sql"));
            var cambiado = conjunto.Abrir("pokemon", "SELECT 1", Path.Combine(Path.GetTempPath(), "cambiado.sql"));
            cambiado.Texto = "SELECT 2";

            var recuperado = ConjuntoPestanasModelo.Recuperar(conjunto.Guardar(Guid.NewGuid()), _ => null);

            Assert.Equal(["borrado.sql", "cambiado.sql"], recuperado.ArchivosNoEncontrados);
            Assert.Equal("borrado.sql", recuperado.Pestanas[0].Nombre);
            Assert.Null(recuperado.Pestanas[0].Archivo);
            Assert.Equal("", recuperado.Pestanas[0].Texto);
            Assert.Equal("SELECT 2", recuperado.Pestanas[1].Texto);
            Assert.True(recuperado.Pestanas[1].Modificado);
        }

        [Fact]
        public void UnaPestanaSinArchivoNoDuplicaSuTexto()
        {
            var conjunto = new ConjuntoPestanasModelo();
            var pestana = conjunto.Abrir("pokemon", titulo: "Script del historial");
            pestana.Texto = "SELECT * FROM pokemon";
            pestana.TextoGuardado = "SELECT * FROM pokemon";
            var escrita = conjunto.Abrir("pokemon", "SELECT 1");

            var guardadas = conjunto.Guardar(Guid.NewGuid()).Pestanas;

            Assert.True(guardadas[0].SinCambios);
            Assert.Equal("", guardadas[0].TextoGuardado);
            Assert.False(guardadas[1].SinCambios);

            var recuperado = ConjuntoPestanasModelo.Recuperar(new SesionEditorModelo { Pestanas = guardadas });
            Assert.False(recuperado.Pestanas[0].Modificado);
            Assert.Equal("SELECT 1", recuperado.Pestanas[1].Texto);
            Assert.True(recuperado.Pestanas[1].Modificado);
        }

        [Fact]
        public void LeerArchivoDevuelveNuloSiNoExiste()
        {
            Assert.Null(ConjuntoPestanasModelo.LeerArchivo(Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.sql")));
        }

        [Fact]
        public void RecuperaLaSesionDeAntesDeLasPestanas()
        {
            var recuperado = ConjuntoPestanasModelo.Recuperar(new SesionEditorModelo { BaseDatos = "tienda", Texto = "SELECT 1" });

            Assert.Single(recuperado.Pestanas);
            Assert.Equal("tienda", recuperado.Activa!.BaseDatos);
            Assert.Empty(ConjuntoPestanasModelo.Recuperar(new SesionEditorModelo()).Pestanas);
        }

        [Fact]
        public void LaConexionPrincipalIncluyeLasSesionesDeSusPestanas()
        {
            var principal = new ConexionActiva(new ConexionModelo(), null, new ResultadoPruebaConexion(true, TimeSpan.Zero));
            var pestana = principal.NuevaSesion();
            var otra = principal.NuevaSesion();

            Assert.True(principal.Incluye(pestana));
            Assert.True(pestana.Incluye(pestana));
            Assert.False(pestana.Incluye(otra));
            Assert.False(pestana.Incluye(principal));
        }
    }
}
