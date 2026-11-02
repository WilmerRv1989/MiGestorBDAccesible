using MiGestorBDAccesible.Core.Definicion;
using MiGestorBDAccesible.Core.Descripcion;
using MiGestorBDAccesible.Core.Estructura;
using MiGestorBDAccesible.Core.Proveedores.SqlServer;

namespace MiGestorBDAccesible.Pruebas
{
    public class NarradorYScriptsPruebas
    {
        private readonly NarradorTablaService _narrador = new();
        private readonly ProveedorSqlServer _proveedor = new();

        private static DefinicionTablaModelo TablaIntermedia()
        {
            var tabla = new DefinicionTablaModelo { Nombre = "PedidoProductos" };

            foreach (var (columna, destino) in new[] { ("PedidoId", "Pedidos"), ("ProductoId", "Productos") })
            {
                tabla.Columnas.Add(new DefinicionColumnaModelo
                {
                    Nombre = columna,
                    Tipo = "int",
                    EsClavePrincipal = true,
                    Referencia = new ReferenciaModelo
                    {
                        TablaDestino = new TablaModelo { Nombre = destino },
                        ColumnaDestino = "Id",
                        TipoDestino = "int",
                        AlEliminar = destino == "Pedidos" ? ReglaReferencial.Cascada : ReglaReferencial.SinAccion
                    }
                });
            }

            tabla.Columnas.Add(new DefinicionColumnaModelo { Nombre = "Cantidad", Tipo = "int" });
            return tabla;
        }

        [Theory]
        [InlineData(new string[0], "")]
        [InlineData(new[] { "A" }, "A")]
        [InlineData(new[] { "A", "B" }, "A y B")]
        [InlineData(new[] { "A", "B", "C" }, "A, B y C")]
        public void UneListasEnEspanol(string[] elementos, string esperado)
        {
            Assert.Equal(esperado, NarradorTablaService.Unir(elementos));
        }

        [Theory]
        [InlineData("((0))", "0")]
        [InlineData("(CONVERT([date],getdate()))", "CONVERT([date],getdate())")]
        [InlineData("([Precio]>=(0))", "[Precio]>=(0)")]
        [InlineData("(a)+(b)", "(a)+(b)")]
        public void QuitaSoloLosParentesisExternos(string expresion, string esperado)
        {
            Assert.Equal(esperado, NarradorTablaService.QuitarParentesisExternos(expresion));
        }

        [Fact]
        public void ReconoceUnaTablaIntermediaAntesDeCrearla()
        {
            var frases = _narrador.DescribirDefinicion(TablaIntermedia());

            Assert.Contains(frases, f => f.Contains("Será una tabla intermedia") && f.Contains("Pedidos y Productos"));
            Assert.Contains(frases, f => f.StartsWith("La clave principal (PRIMARY KEY) es compuesta"));
            Assert.Contains(frases, f => f.Contains("ON DELETE") && f.Contains("CASCADE"));
        }

        [Fact]
        public void DescribeColumnasConElTerminoDeSqlEntreParentesis()
        {
            var tabla = new DefinicionTablaModelo { Nombre = "Clientes" };
            tabla.Columnas.Add(new DefinicionColumnaModelo { Nombre = "Id", Tipo = "int", EsClavePrincipal = true, EsIdentidad = true });

            var frase = _narrador.DescribirDefinicion(tabla)[1];

            Assert.Equal("Id: int, clave principal (PRIMARY KEY), número automático que empieza en 1 y sube de 1 en 1 (IDENTITY(1, 1)), obligatoria (NOT NULL).", frase);
        }

        [Fact]
        public void GeneraCreateTableConClaveCompuestaYLlaves()
        {
            var sql = _proveedor.ScriptCrearTabla(TablaIntermedia());

            Assert.Contains("CREATE TABLE [dbo].[PedidoProductos] (", sql);
            Assert.Contains("CONSTRAINT [PK_PedidoProductos] PRIMARY KEY ([PedidoId], [ProductoId])", sql);
            Assert.Contains("FOREIGN KEY ([PedidoId]) REFERENCES [dbo].[Pedidos] ([Id]) ON DELETE CASCADE", sql);
            Assert.Contains("FOREIGN KEY ([ProductoId]) REFERENCES [dbo].[Productos] ([Id])", sql);
            Assert.DoesNotContain("ON DELETE NO ACTION", sql);
        }

        [Fact]
        public void GeneraValoresPredeterminadosConNombre()
        {
            var tabla = new DefinicionTablaModelo { Nombre = "Pedidos" };
            tabla.Columnas.Add(new DefinicionColumnaModelo { Nombre = "Fecha", Tipo = "date", ValorPredeterminado = "GETDATE()" });

            Assert.Contains("[Fecha] date NOT NULL CONSTRAINT [DF_Pedidos_Fecha] DEFAULT (GETDATE())", _proveedor.ScriptCrearTabla(tabla));
        }

        [Fact]
        public void DelimitaNombresConCorchetes()
        {
            Assert.Equal("CREATE DATABASE [Mi]]Base];", _proveedor.ScriptCrearBaseDatos("Mi]Base"));
        }
    }
}
