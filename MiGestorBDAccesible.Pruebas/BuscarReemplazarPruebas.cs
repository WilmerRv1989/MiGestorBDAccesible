using MiGestorBDAccesible.Core.Trabajo;

namespace MiGestorBDAccesible.Pruebas
{
    public class BuscarReemplazarPruebas
    {
        private const string Consulta = "SELECT c.nombre\nFROM clientes c\nJOIN pedidos p ON p.cliente_id = c.id\nWHERE p.Cliente_Id = 3 AND p.cliente_ident = 1;";

        [Fact]
        public void BuscaSinDistinguirMayusculasPorOmision()
        {
            var busqueda = new BusquedaModelo { Texto = "cliente_id" };

            Assert.Equal(3, BuscadorTextoService.Coincidencias(Consulta, busqueda).Count);

            busqueda.CoincidirMayusculas = true;
            Assert.Equal(2, BuscadorTextoService.Coincidencias(Consulta, busqueda).Count);
        }

        [Fact]
        public void PalabraCompletaDescartaLasQueSiguen()
        {
            var busqueda = new BusquedaModelo { Texto = "cliente_id", PalabraCompleta = true };

            Assert.Equal(2, BuscadorTextoService.Coincidencias(Consulta, busqueda).Count);
            Assert.Single(BuscadorTextoService.Coincidencias("clientes c, c.id", new BusquedaModelo { Texto = "c", PalabraCompleta = true }), c => c.Inicio == 9);
        }

        [Fact]
        public void SiguienteDesdeElCursorYVueltaAlPrincipio()
        {
            var busqueda = new BusquedaModelo { Texto = "cliente_id", PalabraCompleta = true };

            var primera = BuscadorTextoService.Buscar(Consulta, busqueda, 0, 0, false)!;
            Assert.Equal((1, 2, false), (primera.Indice, primera.Total, primera.DioLaVuelta));
            Assert.Equal("Línea 3: JOIN pedidos p ON p.cliente_id = c.id. Coincidencia 1 de 2.", BuscadorTextoService.Anuncio(Consulta, primera));

            var segunda = BuscadorTextoService.Buscar(Consulta, busqueda, primera.Coincidencia.Inicio, primera.Coincidencia.Fin, false)!;
            Assert.Equal("Línea 4: WHERE p.Cliente_Id = 3 AND p.cliente_ident = 1; Coincidencia 2 de 2.", BuscadorTextoService.Anuncio(Consulta, segunda));

            var tercera = BuscadorTextoService.Buscar(Consulta, busqueda, segunda.Coincidencia.Inicio, segunda.Coincidencia.Fin, false)!;
            Assert.True(tercera.DioLaVuelta);
            Assert.StartsWith("Volviendo al principio. Línea 3", BuscadorTextoService.Anuncio(Consulta, tercera));

            var anterior = BuscadorTextoService.Buscar(Consulta, busqueda, tercera.Coincidencia.Inicio, tercera.Coincidencia.Fin, true)!;
            Assert.StartsWith("Volviendo al final. Línea 4", BuscadorTextoService.Anuncio(Consulta, anterior));
        }

        [Fact]
        public void SinCoincidencias()
        {
            Assert.Null(BuscadorTextoService.Buscar(Consulta, new BusquedaModelo { Texto = "cliente_idd" }, 0, 0, false));
            Assert.Null(BuscadorTextoService.Buscar(Consulta, new BusquedaModelo(), 0, 0, false));
        }

        [Fact]
        public void ReemplazarTodo()
        {
            var (texto, cantidad) = BuscadorTextoService.ReemplazarTodo(Consulta, new BusquedaModelo { Texto = "cliente_id", Reemplazo = "id_cliente", PalabraCompleta = true });

            Assert.Equal(2, cantidad);
            Assert.Contains("p.id_cliente = c.id", texto);
            Assert.Contains("p.id_cliente = 3 AND p.cliente_ident = 1", texto);
        }
    }
}
