using MiGestorBDAccesible.Core.Definicion;
using MiGestorBDAccesible.Core.Estructura;
using MiGestorBDAccesible.Core.Proveedores.PostgreSql;

namespace MiGestorBDAccesible.Pruebas
{
    public class PostgreSqlTablasPruebas
    {
        private readonly ProveedorPostgreSql _proveedor = new();
        private readonly ValidadorTablaService _validador = new();

        [Theory]
        [InlineData("Pedidos", "pedidos")]
        [InlineData("DETALLE_PEDIDO", "detalle_pedido")]
        [InlineData("Mi Tabla", "Mi Tabla")]
        [InlineData("User", "User")]
        public void LosNombresSimplesSeGuardanEnMinusculas(string nombre, string esperado)
        {
            Assert.Equal(esperado, _proveedor.NombreGuardado(nombre));
        }

        [Fact]
        public void AvisaComoSeGuardaraElNombre()
        {
            Assert.Equal("PostgreSQL lo guardará como pedidos.", _proveedor.AvisoNombre("Pedidos"));
            Assert.Null(_proveedor.AvisoNombre("pedidos"));
            Assert.StartsWith("PostgreSQL lo guardará entre comillas", _proveedor.AvisoNombre("Mi Tabla"));
        }

        [Fact]
        public void CreaLaTablaEnDialectoPostgreSql()
        {
            var tabla = new DefinicionTablaModelo { Esquema = "public", Nombre = "Resenas" };
            tabla.Columnas.Add(new DefinicionColumnaModelo { Nombre = "Id", Tipo = "integer", EsClavePrincipal = true, EsIdentidad = true });
            tabla.Columnas.Add(new DefinicionColumnaModelo { Nombre = "Texto", Tipo = "text" });
            tabla.Columnas.Add(new DefinicionColumnaModelo { Nombre = "Estrellas", Tipo = "smallint", ValorPredeterminado = "5" });
            tabla.Columnas.Add(new DefinicionColumnaModelo { Nombre = "Creada", Tipo = "timestamptz", FraccionSegundos = 3, PermiteNulos = true, ValorPredeterminado = "now()" });
            tabla.Columnas.Add(new DefinicionColumnaModelo
            {
                Nombre = "Producto_Id",
                Tipo = "integer",
                PermiteNulos = true,
                Referencia = new ReferenciaModelo
                {
                    TablaDestino = new TablaModelo { Esquema = "public", Nombre = "productos" },
                    ColumnaDestino = "id",
                    TipoDestino = "integer",
                    AlEliminar = ReglaReferencial.EstablecerNulo,
                    AlActualizar = ReglaReferencial.Cascada
                }
            });

            var script = _proveedor.ScriptCrearTabla(tabla).ReplaceLineEndings("\n");

            Assert.Equal("""
                CREATE TABLE resenas (
                    id integer GENERATED ALWAYS AS IDENTITY,
                    texto text NOT NULL,
                    estrellas smallint DEFAULT 5 NOT NULL,
                    creada timestamptz(3) DEFAULT now(),
                    producto_id integer,
                    CONSTRAINT pk_resenas PRIMARY KEY (id),
                    CONSTRAINT fk_resenas_productos_producto_id FOREIGN KEY (producto_id) REFERENCES productos (id) ON DELETE SET NULL ON UPDATE CASCADE
                );
                """, script);
        }

        [Fact]
        public void LaLlaveSinComprobarUsaNotValidYAdmiteRestrict()
        {
            var llave = new DefinicionLlaveForaneaModelo
            {
                TablaOrigen = new TablaModelo { Esquema = "public", Nombre = "pedidos" },
                ColumnaOrigen = "cliente_id",
                Referencia = new ReferenciaModelo { TablaDestino = new TablaModelo { Esquema = "public", Nombre = "clientes" }, ColumnaDestino = "id", AlEliminar = ReglaReferencial.Restringir },
                ComprobarExistentes = false
            };

            Assert.Equal(
                "ALTER TABLE pedidos\n    ADD CONSTRAINT fk_pedidos_clientes_cliente_id FOREIGN KEY (cliente_id) REFERENCES clientes (id) ON DELETE RESTRICT NOT VALID;",
                _proveedor.ScriptCrearLlaveForanea(llave).ReplaceLineEndings("\n"));
        }

        [Theory]
        [InlineData("integer", "bigint", true)]
        [InlineData("varchar(50)", "text", true)]
        [InlineData("integer", "numeric(10, 2)", false)]
        [InlineData("timestamp", "timestamptz", false)]
        [InlineData("uuid", "uuid", true)]
        public void ComparaLosTiposPorFamilia(string origen, string destino, bool compatibles)
        {
            Assert.Equal(compatibles, _proveedor.Capacidades.Terminos.TiposCompatibles(origen, destino));
        }

        [Fact]
        public void ElValidadorUsaLasReglasDePostgreSql()
        {
            var terminos = _proveedor.Capacidades.Terminos;
            var numericLibre = new DefinicionColumnaModelo { Nombre = "total", Tipo = "numeric" };
            var fraccionAlta = new DefinicionColumnaModelo { Nombre = "marca", Tipo = "timestamptz", FraccionSegundos = 7 };
            var textoIdentidad = new DefinicionColumnaModelo { Nombre = "codigo", Tipo = "text", EsIdentidad = true };

            Assert.Empty(_validador.ValidarColumna(numericLibre, [], _proveedor.TiposDatos, terminos));
            Assert.Contains(_validador.ValidarColumna(fraccionAlta, [], _proveedor.TiposDatos, terminos), p => p.Mensaje.Contains("entre 0 y 6"));
            Assert.Contains(_validador.ValidarColumna(textoIdentidad, [], _proveedor.TiposDatos, terminos), p => p.Mensaje.Contains("integer o bigint"));
            Assert.True(_proveedor.Capacidades.PuedeDisenarTablas);
        }
    }
}
