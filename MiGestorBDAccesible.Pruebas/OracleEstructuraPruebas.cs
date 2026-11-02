using MiGestorBDAccesible.Core.Descripcion;
using MiGestorBDAccesible.Core.Estructura;
using MiGestorBDAccesible.Core.Proveedores;
using MiGestorBDAccesible.Core.Proveedores.Oracle;

namespace MiGestorBDAccesible.Pruebas
{
    public class OracleEstructuraPruebas
    {
        [Theory]
        [InlineData("VARCHAR2", 100, 100, "B", null, null, "VARCHAR2(100)")]
        [InlineData("VARCHAR2", 400, 100, "C", null, null, "VARCHAR2(100 CHAR)")]
        [InlineData("NVARCHAR2", 200, 100, "C", null, null, "NVARCHAR2(100)")]
        [InlineData("NUMBER", 22, 0, null, null, null, "NUMBER")]
        [InlineData("NUMBER", 22, 0, null, null, 0, "NUMBER(38, 0)")]
        [InlineData("NUMBER", 22, 0, null, 10, 0, "NUMBER(10)")]
        [InlineData("NUMBER", 22, 0, null, 10, 2, "NUMBER(10, 2)")]
        [InlineData("RAW", 16, 0, null, null, null, "RAW(16)")]
        [InlineData("DATE", 7, 0, null, null, null, "DATE")]
        [InlineData("TIMESTAMP(6)", 11, 0, null, null, 6, "TIMESTAMP(6)")]
        public void FormateaLosTiposDeOracle(string tipo, int longitud, int caracteres, string? unidad, int? precision, int? escala, string esperado)
        {
            Assert.Equal(esperado, ProveedorOracle.FormatearTipo(tipo, longitud, caracteres, unidad, precision, escala));
        }

        [Theory]
        [InlineData("CLIENTES", "CLIENTES")]
        [InlineData("PEDIDO_PRODUCTOS", "PEDIDO_PRODUCTOS")]
        [InlineData("Clientes", "\"Clientes\"")]
        [InlineData("DATE", "\"DATE\"")]
        [InlineData("MI TABLA", "\"MI TABLA\"")]
        [InlineData("1ERO", "\"1ERO\"")]
        public void LosNombresSoloLlevanComillasCuandoHacenFalta(string nombre, string esperado)
        {
            Assert.Equal(esperado, ProveedorOracle.Identificador(nombre));
        }

        [Fact]
        public void EliminarUnaTablaPuedeIrALaPapeleraOPurgarse()
        {
            var proveedor = new ProveedorOracle();
            var tabla = new TablaModelo { Esquema = "TIENDA", Nombre = "CLIENTES" };

            Assert.Equal("DROP TABLE TIENDA.CLIENTES;", proveedor.ScriptEliminarTabla(tabla));
            Assert.Equal("DROP TABLE TIENDA.CLIENTES PURGE;", proveedor.ScriptEliminarTabla(tabla, true));
            Assert.True(proveedor.Capacidades.TienePapelera);
            Assert.True(proveedor.Capacidades.PuedeDisenarTablas);
            Assert.False(CapacidadesMotorModelo.SqlServer.TienePapelera);
            Assert.True(CapacidadesMotorModelo.SqlServer.PuedeDisenarTablas);
        }

        [Fact]
        public void ElNombreCortoOcultaElEsquemaQueSeRecorre()
        {
            var propia = new TablaModelo { Esquema = "TIENDA", Nombre = "CLIENTES", EsquemaPredeterminado = "TIENDA" };
            var ajena = new TablaModelo { Esquema = "RH", Nombre = "EMPLEADOS", EsquemaPredeterminado = "TIENDA" };
            var sqlServer = new TablaModelo { Nombre = "Clientes" };

            Assert.Equal("CLIENTES", propia.NombreCorto());
            Assert.Equal("RH.EMPLEADOS", ajena.NombreCorto());
            Assert.Equal("Clientes", sqlServer.NombreCorto());
        }

        [Fact]
        public void LaDescripcionDeOracleUsaSusTerminos()
        {
            var terminos = new ProveedorOracle().Capacidades.Terminos;
            var pedidos = new TablaModelo { Esquema = "TIENDA", Nombre = "PEDIDOS", EsquemaPredeterminado = "TIENDA" };
            var clientes = new TablaModelo { Esquema = "TIENDA", Nombre = "CLIENTES", EsquemaPredeterminado = "TIENDA" };

            var detalle = new DetalleTablaModelo
            {
                Tabla = pedidos,
                Columnas =
                [
                    new ColumnaModelo { Nombre = "ID", TipoDatos = "NUMBER", EsIdentidad = true, SemillaIdentidad = 1, IncrementoIdentidad = 1, IdentidadSiempre = true },
                    new ColumnaModelo { Nombre = "CLIENTE_ID", TipoDatos = "NUMBER" },
                    new ColumnaModelo { Nombre = "TOTAL_IVA", TipoDatos = "NUMBER", PermiteNulos = true, Formula = "TOTAL*1.13" }
                ],
                Indices = [new IndiceModelo { Nombre = "PK_PEDIDOS", EsClavePrincipal = true, EsUnico = true, Columnas = [new ColumnaIndiceModelo { Nombre = "ID" }] }],
                LlavesForaneas =
                [
                    new LlaveForaneaModelo
                    {
                        Nombre = "FK_PEDIDOS_CLIENTES",
                        TablaOrigen = pedidos,
                        ColumnasOrigen = ["CLIENTE_ID"],
                        TablaDestino = clientes,
                        ColumnasDestino = ["ID"],
                        AlEliminar = ReglaReferencial.Cascada,
                        Habilitada = false
                    }
                ]
            };

            var descripcion = new NarradorTablaService().Describir(detalle, terminos);
            var texto = string.Join(" ", descripcion.Resumen.Concat(descripcion.Secciones.SelectMany(s => s.Frases)));

            Assert.StartsWith("PEDIDOS tiene 3 columnas", descripcion.Resumen[0]);
            Assert.Contains("(GENERATED AS IDENTITY), no se puede escribir a mano (ALWAYS)", texto);
            Assert.Contains("apunta a CLIENTES.ID (FOREIGN KEY)", texto);
            Assert.Contains("(GENERATED ALWAYS AS … VIRTUAL)", texto);
            Assert.Contains("(ON DELETE): en cascada", texto);
            Assert.DoesNotContain("ON UPDATE", texto);
            Assert.Contains("(DISABLE)", texto);
            Assert.DoesNotContain("CLUSTERED", texto);
            Assert.DoesNotContain("TIENDA.", texto);
        }
    }
}
