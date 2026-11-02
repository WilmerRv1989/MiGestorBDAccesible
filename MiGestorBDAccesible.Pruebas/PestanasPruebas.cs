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
            var pestana = conjunto.Abrir("tienda", "SELECT 1", @"C:\sql\ventas.sql");
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
            var segunda = conjunto.Abrir("b", "SELECT 2", @"C:\x.sql");
            conjunto.Abrir("c", titulo: "Script CREATE de la vista v");
            conjunto.Activar(segunda);

            var recuperado = ConjuntoPestanasModelo.Recuperar(conjunto.Guardar(Guid.NewGuid()));

            Assert.Equal(["Consulta 1", "x.sql", "Script CREATE de la vista v"], recuperado.Pestanas.Select(p => p.Nombre));
            Assert.Equal("x.sql", recuperado.Activa!.Nombre);
            Assert.False(recuperado.Activa.Modificado);
            Assert.Equal("Consulta 4", recuperado.Abrir(null).Nombre);
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
