using MiGestorBDAccesible.Core.Conexiones;
using MiGestorBDAccesible.Core.Definicion;
using MiGestorBDAccesible.Core.Estructura;

namespace MiGestorBDAccesible.Pruebas
{
    public class DependenciasPruebas
    {
        private static DependenciaModelo D(TipoResultadoBusqueda tipo, string nombre, bool posible = false, string? tabla = null) => new()
        {
            Objeto = new ResultadoBusquedaObjetoModelo
            {
                Tipo = tipo,
                Esquema = "dbo",
                Nombre = nombre,
                Tabla = tabla is null ? null : new TablaModelo { Esquema = "dbo", Nombre = tabla }
            },
            Posible = posible
        };

        [Fact]
        public void TextoDeCadaDependencia()
        {
            Assert.Equal("Vista dbo.v_ventas", DependenciasService.Describir(D(TipoResultadoBusqueda.Vista, "v_ventas")));
            Assert.Equal("Función dbo.total (posible uso: el nombre aparece en su código)", DependenciasService.Describir(D(TipoResultadoBusqueda.Funcion, "total", true)));
            Assert.Equal("Nombra a dbo.no_existe, que no existe", DependenciasService.Describir(new DependenciaModelo { Inexistente = "dbo.no_existe" }));
            Assert.Equal("Tabla dbo.Pedidos, por la llave FK_x", DependenciasService.Describir(new DependenciaModelo { Objeto = D(TipoResultadoBusqueda.Tabla, "Pedidos").Objeto, Nota = "por la llave FK_x" }));
        }

        [Fact]
        public void AvisoAlEliminar()
        {
            Assert.Null(DependenciasService.AvisoEliminar([], "esta tabla"));
            Assert.Equal("Dejarán de funcionar: la vista dbo.v y la función dbo.f (posible) usan esta tabla.",
                DependenciasService.AvisoEliminar([D(TipoResultadoBusqueda.Vista, "v"), D(TipoResultadoBusqueda.Funcion, "f", true)], "esta tabla"));
            Assert.Equal("Dejarán de funcionar: el procedimiento dbo.p usa esta función.",
                DependenciasService.AvisoEliminar([D(TipoResultadoBusqueda.Procedimiento, "p")], "esta función"));
        }

        [Fact]
        public void AvisoAlModificarSegunElMotor()
        {
            List<DependenciaModelo> usan = [D(TipoResultadoBusqueda.Vista, "v")];

            Assert.StartsWith("La vista dbo.v usa esta tabla. Si usan una columna", DependenciasService.AvisoModificar(usan, TipoMotor.SqlServer));
            Assert.Contains("inválidos", DependenciasService.AvisoModificar(usan, TipoMotor.Oracle));
            Assert.Contains("Renombrar sí", DependenciasService.AvisoModificar(usan, TipoMotor.PostgreSql));
        }

        [Fact]
        public void SoloAvisaCuandoElCambioTocaNombresColumnasOTipos()
        {
            Assert.True(DependenciasService.ModificacionAfecta([TipoCambioTabla.RenombrarColumna], false));
            Assert.True(DependenciasService.ModificacionAfecta([TipoCambioTabla.QuitarColumna], false));
            Assert.True(DependenciasService.ModificacionAfecta([TipoCambioTabla.RenombrarTabla], false));
            Assert.True(DependenciasService.ModificacionAfecta([TipoCambioTabla.CambiarTipo], false));
            Assert.True(DependenciasService.ModificacionAfecta([], true));
            Assert.False(DependenciasService.ModificacionAfecta([TipoCambioTabla.AgregarColumna, TipoCambioTabla.CambiarNulos], false));
        }
    }
}
