using MiGestorBDAccesible.Core.Definicion;
using MiGestorBDAccesible.Core.Estructura;
using MiGestorBDAccesible.Core.Proveedores.SqlServer;

namespace MiGestorBDAccesible.Pruebas
{
    public class ValidadorTablaPruebas
    {
        private readonly ValidadorTablaService _validador = new();
        private readonly IReadOnlyList<TipoDatosModelo> _tipos = new ProveedorSqlServer().TiposDatos;

        private List<ProblemaValidacion> Validar(params DefinicionColumnaModelo[] columnas)
        {
            var tabla = new DefinicionTablaModelo { Nombre = "Prueba" };
            tabla.Columnas.AddRange(columnas);
            return _validador.ValidarTabla(tabla, _tipos);
        }

        private static bool Contiene(List<ProblemaValidacion> problemas, string texto) =>
            problemas.Any(p => p.Mensaje.Contains(texto, StringComparison.OrdinalIgnoreCase));

        [Fact]
        public void UnaTablaCorrectaNoTieneProblemas()
        {
            var problemas = Validar(
                new DefinicionColumnaModelo { Nombre = "Id", Tipo = "int", EsClavePrincipal = true, EsIdentidad = true },
                new DefinicionColumnaModelo { Nombre = "Nombre", Tipo = "nvarchar", Longitud = 100 });

            Assert.Empty(problemas);
        }

        [Fact]
        public void LaClavePrincipalNoAdmiteNulos()
        {
            var problemas = Validar(new DefinicionColumnaModelo { Nombre = "Id", Tipo = "int", EsClavePrincipal = true, PermiteNulos = true });

            Assert.True(Contiene(problemas, "no puede admitir NULL"));
        }

        [Fact]
        public void LaIdentidadNoAdmiteValorPredeterminadoNiTiposNoEnteros()
        {
            var problemas = Validar(
                new DefinicionColumnaModelo { Nombre = "Id", Tipo = "int", EsClavePrincipal = true },
                new DefinicionColumnaModelo { Nombre = "Codigo", Tipo = "nvarchar", Longitud = 10, EsIdentidad = true, ValorPredeterminado = "N'A'" });

            Assert.True(Contiene(problemas, "solo los tipos enteros"));
            Assert.True(Contiene(problemas, "no puede tener valor predeterminado"));
        }

        [Fact]
        public void DetectaNombresRepetidosSinDistinguirMayusculas()
        {
            var problemas = Validar(
                new DefinicionColumnaModelo { Nombre = "Id", Tipo = "int", EsClavePrincipal = true },
                new DefinicionColumnaModelo { Nombre = "id", Tipo = "int" });

            Assert.True(Contiene(problemas, "Ya hay una columna llamada"));
        }

        [Fact]
        public void UnTipoMaxNoPuedeSerClave()
        {
            var problemas = Validar(new DefinicionColumnaModelo { Nombre = "Texto", Tipo = "nvarchar", EsMax = true, EsClavePrincipal = true });

            Assert.True(Contiene(problemas, "longitud máxima (MAX)"));
        }

        [Fact]
        public void SinClavePrincipalSoloAdvierte()
        {
            var problemas = Validar(new DefinicionColumnaModelo { Nombre = "Nombre", Tipo = "nvarchar", Longitud = 50 });

            var problema = Assert.Single(problemas);
            Assert.True(problema.EsAdvertencia);
        }

        [Fact]
        public void LaLlaveForaneaExigeTiposIgualesYReglasPosibles()
        {
            var referencia = new ReferenciaModelo
            {
                TablaDestino = new TablaModelo { Nombre = "Clientes" },
                ColumnaDestino = "Id",
                TipoDestino = "int",
                AlEliminar = ReglaReferencial.EstablecerNulo
            };

            var problemas = _validador.ValidarReferencia("ClienteId", "bigint", false, false, referencia);

            Assert.True(Contiene(problemas, "mismo tipo de datos"));
            Assert.True(Contiene(problemas, "SET NULL"));
        }

        [Theory]
        [InlineData("datetime2(7)", "datetime2", true)]
        [InlineData("int", "int", true)]
        [InlineData("int", "bigint", false)]
        [InlineData("nvarchar(100)", "nvarchar(50)", false)]
        public void ComparaTiposDeLlave(string origen, string destino, bool compatibles)
        {
            Assert.Equal(compatibles, ReglaReferencialExtensiones.TiposCompatibles(origen, destino));
        }

        [Theory]
        [InlineData("decimal(10, 2)", "decimal(10, 2)")]
        [InlineData("nvarchar(max)", "nvarchar(max)")]
        [InlineData("nvarchar(150)", "nvarchar(150)")]
        [InlineData("int", "int")]
        [InlineData("datetime2", "datetime2(7)")]
        public void AplicarTipoConservaElTipoCompleto(string tipo, string esperado)
        {
            var columna = new DefinicionColumnaModelo();

            Assert.True(columna.AplicarTipo(tipo, _tipos));
            Assert.Equal(esperado, columna.TipoCompleto);
        }
    }
}
