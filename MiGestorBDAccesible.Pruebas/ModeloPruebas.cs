using MiGestorBDAccesible.Core.Conexiones;
using MiGestorBDAccesible.Core.Descripcion;
using MiGestorBDAccesible.Core.Estructura;
using MiGestorBDAccesible.Core.Proveedores.Oracle;
using MiGestorBDAccesible.Core.Proveedores.PostgreSql;
using MiGestorBDAccesible.Core.Proveedores.SqlServer;

namespace MiGestorBDAccesible.Pruebas
{
    public class ModeloPruebas
    {
        private static TablaModelo T(string nombre, string esquema = "public") => new() { Esquema = esquema, Nombre = nombre, EsquemaPredeterminado = "public" };

        private static DetalleTablaModelo Tabla(string nombre, long filas, params (string Columna, string Destino)[] llaves) => new()
        {
            Tabla = T(nombre),
            Filas = filas,
            Columnas = [new ColumnaModelo { Nombre = "id", TipoDatos = "integer" }, .. llaves.Select(l => new ColumnaModelo { Nombre = l.Columna, TipoDatos = "integer", PermiteNulos = true })],
            Indices = [new IndiceModelo { Nombre = $"pk_{nombre}", EsClavePrincipal = true, Columnas = [new ColumnaIndiceModelo { Nombre = "id" }] }],
            LlavesForaneas = llaves.Select(l => new LlaveForaneaModelo
            {
                Nombre = $"fk_{nombre}_{l.Columna}",
                TablaOrigen = T(nombre),
                ColumnasOrigen = [l.Columna],
                TablaDestino = T(l.Destino),
                ColumnasDestino = ["id"]
            }).ToList()
        };

        private static ModeloBaseDatosModelo Tienda()
        {
            var modelo = new ModeloBaseDatosModelo
            {
                BaseDatos = "tienda",
                Tablas =
                [
                    Tabla("detalle_pedidos", 900, ("pedido_id", "pedidos"), ("producto_id", "productos")),
                    Tabla("pedidos", 420, ("cliente_id", "clientes")),
                    Tabla("productos", 500, ("categoria_id", "categorias")),
                    Tabla("categorias", 8),
                    Tabla("clientes", 100),
                    Tabla("empleados", 5, ("jefe_id", "empleados"))
                ]
            };

            foreach (var tabla in modelo.Tablas)
            {
                tabla.Referencias.AddRange(modelo.Tablas.SelectMany(t => t.LlavesForaneas).Where(l => l.TablaDestino.NombreCompleto == tabla.Tabla.NombreCompleto));
            }

            return modelo;
        }

        private static List<string> Nombres(IEnumerable<DetalleTablaModelo> tablas) => tablas.Select(t => t.Tabla.Nombre).ToList();

        [Fact]
        public void OrdenDeCargaPorNiveles()
        {
            var orden = AnalizadorModeloService.OrdenCarga(Tienda());

            Assert.Equal(3, orden.Niveles.Count);
            Assert.Equal(["categorias", "clientes", "empleados"], Nombres(orden.Niveles[0]));
            Assert.Equal(["pedidos", "productos"], Nombres(orden.Niveles[1]));
            Assert.Equal(["detalle_pedidos"], Nombres(orden.Niveles[2]));
            Assert.Empty(orden.Ciclos);
        }

        [Fact]
        public void DetectaCiclos()
        {
            var modelo = new ModeloBaseDatosModelo
            {
                BaseDatos = "b",
                Tablas = [Tabla("a", 0, ("b_id", "b")), Tabla("b", 0, ("a_id", "a")), Tabla("c", 0, ("a_id", "a"))]
            };

            var orden = AnalizadorModeloService.OrdenCarga(modelo);

            Assert.Equal(["a", "b"], Nombres(orden.Ciclos.Single()));
            Assert.Equal(["a", "b"], Nombres(orden.Niveles[0]));
            Assert.Equal(["c"], Nombres(orden.Niveles[1]));
        }

        [Fact]
        public void CaminoMasCortoEnCualquierDireccion()
        {
            var modelo = Tienda();
            var caminos = AnalizadorModeloService.CaminosJoin(modelo, T("clientes"), T("productos"));

            var camino = Assert.Single(caminos);
            Assert.Equal(["pedidos", "detalle_pedidos", "productos"], camino.Select(p => p.Nueva.Nombre));
            Assert.Equal("clientes → pedidos (por cliente_id) → detalle_pedidos (por pedido_id) → productos (por producto_id). 3 uniones.",
                NarradorModeloService.DescribirCamino(camino));

            Assert.Empty(AnalizadorModeloService.CaminosJoin(modelo, T("clientes"), T("empleados")));
        }

        [Fact]
        public void VariosCaminosIgualDeCortos()
        {
            var modelo = new ModeloBaseDatosModelo
            {
                BaseDatos = "b",
                Tablas = [Tabla("envios", 0, ("origen_id", "ciudades"), ("destino_id", "ciudades")), Tabla("ciudades", 0)]
            };

            var caminos = AnalizadorModeloService.CaminosJoin(modelo, T("envios"), T("ciudades"));

            Assert.Equal(2, caminos.Count);
            Assert.Equal(["origen_id", "destino_id"], caminos.Select(c => c[0].ColumnasAnterior[0]));
        }

        [Fact]
        public void SelectConLosJoinEnCadaMotor()
        {
            var modelo = Tienda();
            var camino = AnalizadorModeloService.CaminosJoin(modelo, T("clientes"), T("productos"))[0];
            var nl = "\n";

            Assert.Equal($"SELECT *{nl}FROM clientes c{nl}JOIN pedidos p ON p.cliente_id = c.id{nl}JOIN detalle_pedidos dp ON dp.pedido_id = p.id{nl}JOIN productos pr ON pr.id = dp.producto_id;",
                AnalizadorModeloService.ConsultaJoin(new ProveedorPostgreSql().Dialecto, T("clientes"), camino));

            var sqlServer = AnalizadorModeloService.ConsultaJoin(new ProveedorSqlServer().Dialecto, T("clientes"), camino);
            Assert.Contains("JOIN [public].[pedidos] p ON p.[cliente_id] = c.[id]", sqlServer);

            var camello = AnalizadorModeloService.ConsultaJoin(new ProveedorSqlServer().Dialecto, T("PedidoProductos", "dbo"),
                [new PasoJoinModelo { Anterior = T("PedidoProductos", "dbo"), Nueva = T("Pedidos", "dbo"), Llave = new LlaveForaneaModelo { TablaOrigen = T("PedidoProductos", "dbo"), ColumnasOrigen = ["PedidoId"], TablaDestino = T("Pedidos", "dbo"), ColumnasDestino = ["Id"] } }]);
            Assert.EndsWith($"FROM [dbo].[PedidoProductos] pp{nl}JOIN [dbo].[Pedidos] p ON p.[Id] = pp.[PedidoId];", camello);

            var oracle = AnalizadorModeloService.ConsultaJoin(new ProveedorOracle().Dialecto, T("clientes"), camino);
            Assert.Contains("JOIN", oracle);
            Assert.DoesNotContain(" AS ", oracle);
        }

        [Fact]
        public void PosiblesProblemas()
        {
            var modelo = Tienda();
            modelo.Tablas[1].Indices.Add(new IndiceModelo { Nombre = "ix_pedidos_cliente", Columnas = [new ColumnaIndiceModelo { Nombre = "cliente_id" }] });
            modelo.Tablas.Add(new DetalleTablaModelo { Tabla = T("registro"), Columnas = [new ColumnaModelo { Nombre = "texto", TipoDatos = "text" }] });
            modelo.Tablas[2].Columnas[1] = new ColumnaModelo { Nombre = "categoria_id", TipoDatos = "bigint" };

            var problemas = AnalizadorModeloService.Problemas(modelo);

            Assert.Contains(problemas, p => p.StartsWith("registro no tiene clave principal"));
            Assert.Contains(problemas, p => p.StartsWith("La llave foránea fk_detalle_pedidos_pedido_id de detalle_pedidos (pedido_id) no tiene índice"));
            Assert.DoesNotContain(problemas, p => p.Contains("fk_pedidos_cliente_id de pedidos (cliente_id) no tiene índice"));
            Assert.Contains("En la llave foránea fk_productos_categoria_id, productos.categoria_id es bigint y categorias.id es integer: conviene que tengan el mismo tipo.", problemas);
            Assert.Contains(problemas, p => p.StartsWith("registro no se relaciona con ninguna otra tabla"));
            Assert.Contains(problemas, p => p.StartsWith("empleados no se relaciona con ninguna otra tabla"));
        }

        [Fact]
        public void DocumentoDelModelo()
        {
            var modelo = Tienda();
            modelo.Objetos.Add(new DetalleObjetoModelo
            {
                Objeto = new ObjetoBaseDatosModelo { Tipo = TipoObjeto.Funcion, Esquema = "public", Nombre = "total_cliente" },
                Parametros = [new ParametroModelo { Nombre = "p_cliente", TipoDatos = "integer" }],
                Retorno = "numeric"
            });

            var descripcion = new NarradorModeloService().Describir(modelo, TipoMotor.PostgreSql);
            var seccion = (string titulo) => descripcion.Secciones.Single(s => s.Titulo == titulo).Frases;

            Assert.Equal("Modelo de tienda", descripcion.Titulo);
            Assert.Equal($"tienda tiene 6 tablas con {1933:N0} filas en total, 5 relaciones y 1 función.", descripcion.Resumen[0]);
            Assert.Equal("pedidos: 2 columnas, 420 filas, clave principal id.", descripcion.Tablas[1].Texto);
            Assert.Contains("detalle_pedidos apunta a pedidos (por pedido_id) y productos (por producto_id).", seccion("Relaciones"));
            Assert.Contains("empleados apunta a sí misma por jefe_id.", seccion("Relaciones"));
            Assert.Equal("Nivel 1, no dependen de ninguna otra: categorias, clientes y empleados.", seccion("Orden de carga")[0]);
            Assert.Equal("Nivel 2: pedidos (necesita clientes) y productos (necesita categorias).", seccion("Orden de carga")[1]);
            Assert.Contains("Función public.total_cliente (p_cliente integer), devuelve numeric.", seccion("Vistas, procedimientos y funciones"));

            var html = NarradorModeloService.Html(descripcion);
            Assert.Contains("<h1>Modelo de tienda</h1>", html);
            Assert.Contains("<h2>Orden de carga</h2>", html);
            Assert.Contains("→", NarradorModeloService.DescribirCamino(AnalizadorModeloService.CaminosJoin(modelo, T("clientes"), T("pedidos"))[0]));
        }
    }
}
