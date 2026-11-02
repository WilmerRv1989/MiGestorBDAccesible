using MiGestorBDAccesible.Core.Estructura;

namespace MiGestorBDAccesible.Pruebas
{
    public class BuscarObjetoPruebas
    {
        private static ResultadoBusquedaObjetoModelo R(TipoResultadoBusqueda tipo, string nombre, string? tabla = null, string? detalle = null) => new()
        {
            Tipo = tipo,
            Esquema = "dbo",
            Nombre = nombre,
            Tabla = tabla is null ? null : new TablaModelo { Esquema = "dbo", Nombre = tabla },
            Detalle = detalle
        };

        [Fact]
        public void ElGuionBajoYLosComodinesSeTomanTalCual()
        {
            Assert.Equal(@"%cliente\_id%", BuscadorObjetosService.Contiene("cliente_id"));
            Assert.Equal(@"100\%\\x\[a]%", BuscadorObjetosService.EmpiezaPor(@"100%\x[a]"));
        }

        [Fact]
        public void PrimeroElExactoDespuesLosQueEmpiezanYAlFinalLosQueContienen()
        {
            var ordenados = BuscadorObjetosService.Ordenar(
            [
                R(TipoResultadoBusqueda.Columna, "PedidoId", "PedidoProductos"),
                R(TipoResultadoBusqueda.Tabla, "PedidoProductos"),
                R(TipoResultadoBusqueda.LlaveForanea, "FK_Pedidos_Clientes", "Pedidos"),
                R(TipoResultadoBusqueda.Tabla, "pedido")
            ], "Pedido");

            Assert.Equal(["pedido", "PedidoProductos", "PedidoId", "FK_Pedidos_Clientes"], ordenados.Select(r => r.Nombre));
        }

        [Fact]
        public void TiposDeCadaCategoria()
        {
            Assert.Equal("0", BuscadorObjetosService.ListaTipos(CategoriaBusqueda.Tablas));
            Assert.Equal("3, 4, 5, 6", BuscadorObjetosService.ListaTipos(CategoriaBusqueda.Rutinas));
            Assert.Equal("7, 8, 9, 10, 11", BuscadorObjetosService.ListaTipos(CategoriaBusqueda.IndicesRestricciones));
            Assert.Equal(14, BuscadorObjetosService.Tipos(CategoriaBusqueda.Todo).Count);
        }

        [Fact]
        public void TextoDeCadaResultado()
        {
            Assert.Equal("Columna ClienteId de la tabla dbo.Pedidos, int", BuscadorObjetosService.Describir(R(TipoResultadoBusqueda.Columna, "ClienteId", "Pedidos", "int")));
            Assert.Equal("Función dbo.total(p integer)", BuscadorObjetosService.Describir(R(TipoResultadoBusqueda.Funcion, "total", detalle: "p integer")));
            Assert.Equal("Subprograma PRECIO del paquete dbo.UTIL", BuscadorObjetosService.Describir(R(TipoResultadoBusqueda.Subprograma, "PRECIO", detalle: "UTIL")));
            Assert.Equal("Llave foránea fk_x de la tabla dbo.Pedidos", BuscadorObjetosService.Describir(R(TipoResultadoBusqueda.LlaveForanea, "fk_x", "Pedidos")));
            Assert.Equal("Disparador auditoria del esquema", BuscadorObjetosService.Describir(R(TipoResultadoBusqueda.Disparador, "auditoria"), "del esquema"));
            Assert.Equal("Disparador trg de la tabla dbo.Productos", BuscadorObjetosService.Describir(R(TipoResultadoBusqueda.Disparador, "trg", "Productos")));
        }
    }
}
