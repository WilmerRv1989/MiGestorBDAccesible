using MiGestorBDAccesible.Core.Definicion;
using MiGestorBDAccesible.Core.Estructura;
using MiGestorBDAccesible.Core.Proveedores;
using MiGestorBDAccesible.Core.Proveedores.Oracle;
using MiGestorBDAccesible.Core.Proveedores.SqlServer;

namespace MiGestorBDAccesible.Pruebas
{
    public class OracleTablasPruebas
    {
        private readonly ProveedorOracle _proveedor = new();
        private readonly ValidadorTablaService _validador = new();

        [Theory]
        [InlineData("Pedidos", "PEDIDOS")]
        [InlineData("detalle_pedido", "DETALLE_PEDIDO")]
        [InlineData("Mi Tabla", "Mi Tabla")]
        [InlineData("Date", "Date")]
        public void LosNombresSimplesSeGuardanEnMayusculas(string nombre, string esperado)
        {
            Assert.Equal(esperado, _proveedor.NombreGuardado(nombre));
        }

        [Fact]
        public void AvisaComoSeGuardaraElNombre()
        {
            Assert.Equal("Oracle lo guardará como PEDIDOS.", _proveedor.AvisoNombre("Pedidos"));
            Assert.Null(_proveedor.AvisoNombre("PEDIDOS"));
            Assert.StartsWith("Oracle lo guardará entre comillas", _proveedor.AvisoNombre("Mi Tabla"));
            Assert.Null(((IProveedorBaseDatos)new ProveedorSqlServer()).AvisoNombre("Pedidos"));
        }

        [Fact]
        public void CreaLaTablaEnDialectoOracle()
        {
            var tabla = new DefinicionTablaModelo { Esquema = "TIENDA", Nombre = "Resenas" };
            tabla.Columnas.Add(new DefinicionColumnaModelo { Nombre = "Id", Tipo = "NUMBER", EsClavePrincipal = true, EsIdentidad = true });
            tabla.Columnas.Add(new DefinicionColumnaModelo { Nombre = "Texto", Tipo = "VARCHAR2", Longitud = 500 });
            tabla.Columnas.Add(new DefinicionColumnaModelo { Nombre = "Estrellas", Tipo = "NUMBER", Precision = 1, Escala = 0, ValorPredeterminado = "5" });
            tabla.Columnas.Add(new DefinicionColumnaModelo { Nombre = "Fecha", Tipo = "TIMESTAMP WITH TIME ZONE", FraccionSegundos = 3, PermiteNulos = true });
            tabla.Columnas.Add(new DefinicionColumnaModelo
            {
                Nombre = "Producto_Id",
                Tipo = "NUMBER",
                PermiteNulos = true,
                Referencia = new ReferenciaModelo
                {
                    TablaDestino = new TablaModelo { Esquema = "TIENDA", Nombre = "PRODUCTOS" },
                    ColumnaDestino = "ID",
                    TipoDestino = "NUMBER",
                    AlEliminar = ReglaReferencial.EstablecerNulo
                }
            });

            var script = _proveedor.ScriptCrearTabla(tabla).ReplaceLineEndings("\n");

            Assert.Equal("""
                CREATE TABLE TIENDA.RESENAS (
                    ID NUMBER GENERATED ALWAYS AS IDENTITY,
                    TEXTO VARCHAR2(500) NOT NULL,
                    ESTRELLAS NUMBER(1, 0) DEFAULT 5 NOT NULL,
                    FECHA TIMESTAMP(3) WITH TIME ZONE,
                    PRODUCTO_ID NUMBER,
                    CONSTRAINT PK_RESENAS PRIMARY KEY (ID),
                    CONSTRAINT FK_RESENAS_PRODUCTOS_PRODUCTO_ID FOREIGN KEY (PRODUCTO_ID) REFERENCES PRODUCTOS (ID) ON DELETE SET NULL
                );
                """, script);
        }

        [Fact]
        public void LaLlaveForaneaSinComprobarUsaEnableNovalidate()
        {
            var llave = new DefinicionLlaveForaneaModelo
            {
                TablaOrigen = new TablaModelo { Esquema = "TIENDA", Nombre = "PEDIDOS" },
                ColumnaOrigen = "CLIENTE_ID",
                Referencia = new ReferenciaModelo { TablaDestino = new TablaModelo { Esquema = "TIENDA", Nombre = "CLIENTES" }, ColumnaDestino = "ID", AlEliminar = ReglaReferencial.Cascada },
                ComprobarExistentes = false
            };

            var script = _proveedor.ScriptCrearLlaveForanea(llave).ReplaceLineEndings("\n");

            Assert.Equal("ALTER TABLE TIENDA.PEDIDOS\n    ADD CONSTRAINT FK_PEDIDOS_CLIENTES_CLIENTE_ID FOREIGN KEY (CLIENTE_ID) REFERENCES CLIENTES (ID) ON DELETE CASCADE ENABLE NOVALIDATE;", script);
        }

        [Theory]
        [InlineData("NUMBER", "NUMBER(10)", true)]
        [InlineData("INTEGER", "NUMBER", true)]
        [InlineData("VARCHAR2(50)", "VARCHAR2(100 CHAR)", true)]
        [InlineData("CHAR(10)", "VARCHAR2(10)", false)]
        [InlineData("NUMBER", "VARCHAR2(10)", false)]
        public void ComparaLosTiposPorFamiliaEnLasLlavesForaneas(string origen, string destino, bool compatibles)
        {
            Assert.Equal(compatibles, _proveedor.Capacidades.Terminos.TiposCompatibles(origen, destino));
        }

        [Fact]
        public void AplicarTipoEntiendeLosTiposDeOracle()
        {
            var columna = new DefinicionColumnaModelo();

            Assert.True(columna.AplicarTipo("VARCHAR2(100 CHAR)", _proveedor.TiposDatos));
            Assert.Equal("VARCHAR2(100)", columna.TipoCompleto);
            Assert.True(columna.AplicarTipo("NUMBER", _proveedor.TiposDatos));
            Assert.Equal("NUMBER", columna.TipoCompleto);
            Assert.True(columna.AplicarTipo("TIMESTAMP(6) WITH TIME ZONE", _proveedor.TiposDatos));
            Assert.Equal("TIMESTAMP(6) WITH TIME ZONE", columna.TipoCompleto);
        }

        [Fact]
        public void ElValidadorUsaLasReglasDeOracle()
        {
            var terminos = _proveedor.Capacidades.Terminos;
            var sinPrecision = new DefinicionColumnaModelo { Nombre = "TOTAL", Tipo = "NUMBER" };
            var conDecimales = new DefinicionColumnaModelo { Nombre = "ID", Tipo = "NUMBER", Precision = 10, Escala = 2, EsIdentidad = true };
            var fraccion = new DefinicionColumnaModelo { Nombre = "MARCA", Tipo = "TIMESTAMP", FraccionSegundos = 9 };

            Assert.Empty(_validador.ValidarColumna(sinPrecision, [], _proveedor.TiposDatos, terminos));
            Assert.Empty(_validador.ValidarColumna(fraccion, [], _proveedor.TiposDatos, terminos));
            Assert.Equal(
                "ID: una columna de identidad (GENERATED AS IDENTITY) no puede tener decimales; deja la escala en 0.",
                Assert.Single(_validador.ValidarColumna(conDecimales, [], _proveedor.TiposDatos, terminos)).Mensaje);
        }
    }
}
