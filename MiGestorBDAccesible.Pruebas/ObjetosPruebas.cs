using MiGestorBDAccesible.Core.Conexiones;
using MiGestorBDAccesible.Core.Descripcion;
using MiGestorBDAccesible.Core.Estructura;
using MiGestorBDAccesible.Core.Proveedores.Oracle;
using MiGestorBDAccesible.Core.Proveedores.PostgreSql;
using MiGestorBDAccesible.Core.Proveedores.SqlServer;

namespace MiGestorBDAccesible.Pruebas
{
    public class ObjetosPruebas
    {
        private readonly NarradorObjetoService _narrador = new();

        private static DetalleObjetoModelo Procedimiento(string esquema, string nombre, params ParametroModelo[] parametros) => new()
        {
            Objeto = new ObjetoBaseDatosModelo { Tipo = TipoObjeto.Procedimiento, Esquema = esquema, Nombre = nombre },
            Parametros = [.. parametros]
        };

        private static DetalleObjetoModelo Funcion(string esquema, string nombre, string retorno, bool devuelveTabla, params ParametroModelo[] parametros) => new()
        {
            Objeto = new ObjetoBaseDatosModelo { Tipo = TipoObjeto.Funcion, Esquema = esquema, Nombre = nombre },
            Retorno = retorno,
            DevuelveTabla = devuelveTabla,
            Parametros = [.. parametros]
        };

        private static ParametroModelo Parametro(string nombre, string tipo, DireccionParametro direccion = DireccionParametro.Entrada) =>
            new() { Nombre = nombre, TipoDatos = tipo, Direccion = direccion };

        [Fact]
        public void SqlServer_LlamadaConSalidaDeclaraVariableEImprime()
        {
            var detalle = Procedimiento("dbo", "ContarPedidosCliente",
                Parametro("@ClienteId", "int"), Parametro("@Cantidad", "int", DireccionParametro.Salida));

            var script = new ProveedorSqlServer().ScriptLlamada(detalle, new Dictionary<string, string?> { ["@ClienteId"] = "3" });

            Assert.Contains("DECLARE @Cantidad int;", script);
            Assert.Contains("EXEC [dbo].[ContarPedidosCliente]", script);
            Assert.Contains("@ClienteId = 3,", script);
            Assert.Contains("@Cantidad = @Cantidad OUTPUT;", script);
            Assert.Contains("PRINT N'@Cantidad = ' + ISNULL(CONVERT(nvarchar(max), @Cantidad), N'NULL');", script);
        }

        [Fact]
        public void SqlServer_LiteralesSegunTipo()
        {
            var detalle = Procedimiento("dbo", "Registrar",
                Parametro("@Nombre", "nvarchar(100)"), Parametro("@Fecha", "date"), Parametro("@Activo", "bit"), Parametro("@Nota", "varchar(50)"));

            var script = new ProveedorSqlServer().ScriptLlamada(detalle, new Dictionary<string, string?>
            {
                ["@Nombre"] = "O'Brien",
                ["@Fecha"] = "2026-01-31",
                ["@Activo"] = "sí",
                ["@Nota"] = ""
            });

            Assert.Contains("@Nombre = N'O''Brien'", script);
            Assert.Contains("@Fecha = '2026-01-31'", script);
            Assert.Contains("@Activo = 1", script);
            Assert.Contains("@Nota = NULL", script);
        }

        [Fact]
        public void SqlServer_FuncionesEscalarYDeTabla()
        {
            var proveedor = new ProveedorSqlServer();
            var valores = new Dictionary<string, string?> { ["@Id"] = "1" };

            Assert.Equal("SELECT [dbo].[TotalPedido](1) AS resultado;",
                proveedor.ScriptLlamada(Funcion("dbo", "TotalPedido", "decimal(12, 2)", false, Parametro("@Id", "int")), valores));
            Assert.Equal($"SELECT *{Environment.NewLine}FROM [dbo].[ProductosDeCategoria](1);",
                proveedor.ScriptLlamada(Funcion("dbo", "ProductosDeCategoria", "una tabla", true, Parametro("@Id", "int")), valores));
        }

        [Fact]
        public void SqlServer_DisparadoresDeTablaYDeBase()
        {
            var proveedor = new ProveedorSqlServer();
            var deTabla = new ObjetoBaseDatosModelo
            {
                Tipo = TipoObjeto.Disparador, Esquema = "dbo", Nombre = "trg_Precio",
                Tabla = new TablaModelo { Esquema = "dbo", Nombre = "Productos" }
            };
            var deBase = new ObjetoBaseDatosModelo { Tipo = TipoObjeto.Disparador, Nombre = "trg_Ddl" };

            Assert.Equal("DISABLE TRIGGER [dbo].[trg_Precio] ON [dbo].[Productos];", proveedor.ScriptHabilitarDisparador(deTabla, false));
            Assert.Equal("ENABLE TRIGGER [trg_Ddl] ON DATABASE;", proveedor.ScriptHabilitarDisparador(deBase, true));
            Assert.Equal("DROP TRIGGER [dbo].[trg_Precio];", proveedor.ScriptEliminarObjeto(deTabla));
            Assert.Equal("DROP TRIGGER [trg_Ddl] ON DATABASE;", proveedor.ScriptEliminarObjeto(deBase));
            Assert.Equal("trg_Ddl", deBase.NombreCompleto);
        }

        [Fact]
        public void Oracle_LlamadaConSalidaUsaBloqueYDbmsOutput()
        {
            var detalle = Procedimiento("TIENDA", "CONTAR_PEDIDOS_CLIENTE",
                Parametro("P_CLIENTE_ID", "NUMBER"), Parametro("P_CANTIDAD", "NUMBER", DireccionParametro.Salida));

            var script = new ProveedorOracle().ScriptLlamada(detalle, new Dictionary<string, string?> { ["P_CLIENTE_ID"] = "1" });

            Assert.Equal(string.Join(Environment.NewLine,
                "DECLARE",
                "    V_CANTIDAD NUMBER;",
                "BEGIN",
                "    TIENDA.CONTAR_PEDIDOS_CLIENTE(P_CLIENTE_ID => 1, P_CANTIDAD => V_CANTIDAD);",
                "    DBMS_OUTPUT.PUT_LINE('P_CANTIDAD = ' || NVL(TO_CHAR(V_CANTIDAD), 'NULL'));",
                "END;",
                "/"), script);
        }

        [Fact]
        public void Oracle_FuncionSoloDeEntradaUsaDualYFechas()
        {
            var detalle = Funcion("TIENDA", "DIAS_DESDE", "NUMBER", false, Parametro("P_FECHA", "DATE"), Parametro("P_NOMBRE", "VARCHAR2"));

            var script = new ProveedorOracle().ScriptLlamada(detalle, new Dictionary<string, string?> { ["P_FECHA"] = "2026-01-31", ["P_NOMBRE"] = "Ana" });

            Assert.Equal($"SELECT TIENDA.DIAS_DESDE(DATE '2026-01-31', 'Ana') AS resultado{Environment.NewLine}FROM dual;", script);
        }

        [Fact]
        public void Oracle_ScriptsDeSecuenciaYPaquete()
        {
            var proveedor = new ProveedorOracle();

            Assert.Equal("ALTER SEQUENCE TIENDA.NUMERO_FACTURA RESTART START WITH 1000;",
                proveedor.ScriptReiniciarSecuencia(new ObjetoBaseDatosModelo { Tipo = TipoObjeto.Secuencia, Esquema = "TIENDA", Nombre = "NUMERO_FACTURA" }, 1000));
            Assert.Equal("DROP PACKAGE TIENDA.TIENDA_UTIL;",
                proveedor.ScriptEliminarObjeto(new ObjetoBaseDatosModelo { Tipo = TipoObjeto.Paquete, Esquema = "TIENDA", Nombre = "TIENDA_UTIL" }));
            Assert.Equal("ALTER TRIGGER TIENDA.TRG_PRECIO ENABLE;",
                proveedor.ScriptHabilitarDisparador(new ObjetoBaseDatosModelo { Tipo = TipoObjeto.Disparador, Esquema = "TIENDA", Nombre = "TRG_PRECIO" }, true));
        }

        [Fact]
        public void PostgreSql_ProcedimientoConSalidaPasaNull()
        {
            var detalle = Procedimiento("public", "contar_pedidos_cliente",
                Parametro("p_cliente_id", "integer"), Parametro("p_cantidad", "integer", DireccionParametro.Salida));

            Assert.Equal("CALL contar_pedidos_cliente(1, NULL);",
                new ProveedorPostgreSql().ScriptLlamada(detalle, new Dictionary<string, string?> { ["p_cliente_id"] = "1" }));
        }

        [Fact]
        public void PostgreSql_FuncionesYLiterales()
        {
            var proveedor = new ProveedorPostgreSql();

            Assert.Equal("SELECT ventas.total('2026-01-31', true) AS resultado;",
                proveedor.ScriptLlamada(Funcion("ventas", "total", "numeric", false, Parametro("p_dia", "date"), Parametro("p_activo", "boolean")),
                    new Dictionary<string, string?> { ["p_dia"] = "2026-01-31", ["p_activo"] = "sí" }));
            Assert.Equal($"SELECT *{Environment.NewLine}FROM productos_de_categoria(2);",
                proveedor.ScriptLlamada(Funcion("public", "productos_de_categoria", "TABLE(id integer)", true, Parametro("p_categoria_id", "integer")),
                    new Dictionary<string, string?> { ["p_categoria_id"] = "2" }));
        }

        [Fact]
        public void PostgreSql_EliminarConFirmaYDisparadores()
        {
            var proveedor = new ProveedorPostgreSql();
            var tabla = new TablaModelo { Esquema = "public", Nombre = "productos" };

            Assert.Equal("DROP FUNCTION total(integer, date);",
                proveedor.ScriptEliminarObjeto(new ObjetoBaseDatosModelo { Tipo = TipoObjeto.Funcion, Esquema = "public", Nombre = "total", Firma = "integer, date" }));
            Assert.Equal("DROP MATERIALIZED VIEW ventas.resumen;",
                proveedor.ScriptEliminarObjeto(new ObjetoBaseDatosModelo { Tipo = TipoObjeto.Vista, Esquema = "ventas", Nombre = "resumen", Materializada = true }));
            Assert.Equal("DROP TRIGGER trg_precio ON productos;",
                proveedor.ScriptEliminarObjeto(new ObjetoBaseDatosModelo { Tipo = TipoObjeto.Disparador, Esquema = "public", Nombre = "trg_precio", Tabla = tabla }));
            Assert.Equal("ALTER TABLE productos DISABLE TRIGGER trg_precio;",
                proveedor.ScriptHabilitarDisparador(new ObjetoBaseDatosModelo { Tipo = TipoObjeto.Disparador, Esquema = "public", Nombre = "trg_precio", Tabla = tabla }, false));
            Assert.Equal("DROP EVENT TRIGGER registrar_ddl;",
                proveedor.ScriptEliminarObjeto(new ObjetoBaseDatosModelo { Tipo = TipoObjeto.Disparador, Nombre = "registrar_ddl" }));
        }

        [Fact]
        public void Narrador_DescribeDisparadorDeTabla()
        {
            var detalle = new DetalleObjetoModelo
            {
                Objeto = new ObjetoBaseDatosModelo
                {
                    Tipo = TipoObjeto.Disparador, Esquema = "public", Nombre = "trg_precio",
                    Tabla = new TablaModelo { Esquema = "public", Nombre = "productos", EsquemaPredeterminado = "public" }
                },
                Momento = "BEFORE",
                Eventos = ["INSERT", "UPDATE"],
                PorFila = true,
                Habilitado = true,
                FuncionDisparador = "validar_precio"
            };

            var descripcion = _narrador.Describir(detalle, TipoMotor.PostgreSql);

            Assert.Equal("Descripción del disparador public.trg_precio", descripcion.Titulo);
            Assert.Equal("Se ejecuta antes de cada INSERT o UPDATE (BEFORE), fila por fila (FOR EACH ROW), sobre la tabla productos; llama a la función validar_precio; está habilitado.",
                descripcion.Resumen.Single());
        }

        [Fact]
        public void Narrador_DescribeProcedimientoOracleInvalido()
        {
            var detalle = new DetalleObjetoModelo
            {
                Objeto = new ObjetoBaseDatosModelo { Tipo = TipoObjeto.Procedimiento, Esquema = "TIENDA", Nombre = "P_PRUEBA", Valido = false },
                Errores = [new ErrorCompilacionModelo { Linea = 3, Columna = 3, Texto = "PLS-00201: el identificador 'X' se debe declarar" }]
            };

            var descripcion = _narrador.Describir(detalle, TipoMotor.Oracle);

            Assert.Equal(["Procedimiento sin parámetros.", "Está inválido (INVALID): tiene 1 error de compilación."], descripcion.Resumen);
            Assert.Equal("Línea 3, columna 3: PLS-00201: el identificador 'X' se debe declarar",
                descripcion.Secciones.Single(s => s.Titulo == "Errores de compilación").Frases.Single());
        }

        [Fact]
        public void Narrador_DescribeSecuenciaSinUsar()
        {
            var detalle = new DetalleObjetoModelo
            {
                Objeto = new ObjetoBaseDatosModelo { Tipo = TipoObjeto.Secuencia, Esquema = "dbo", Nombre = "NumeroFactura" },
                Retorno = "int", Inicio = "1000", Incremento = "1", Minimo = "1", Maximo = "2147483647", Ciclo = false
            };

            var frases = _narrador.Describir(detalle, TipoMotor.SqlServer).Secciones.Single().Frases;

            Assert.Equal("Todavía no se ha usado; el primer valor será 1000.", frases[0]);
            Assert.Equal("Al llegar al máximo da error en vez de repetir números (NO CYCLE).", frases[^1]);
        }

        [Fact]
        public void Narrador_FuncionDeDisparadorNoSeEjecuta()
        {
            var detalle = Funcion("public", "validar_precio", "trigger", false);

            Assert.True(NarradorObjetoService.EsFuncionDisparador(detalle));
            Assert.StartsWith("Función sin parámetros que devuelve trigger", _narrador.Describir(detalle, TipoMotor.PostgreSql).Resumen[0]);
        }

        [Fact]
        public void Capacidades_OracleTienePaquetesYDisparadoresDelEsquema()
        {
            Assert.Contains(TipoObjeto.Paquete, new ProveedorOracle().Capacidades.TiposObjeto);
            Assert.Equal("Disparadores del esquema", new ProveedorOracle().Capacidades.TituloDisparadoresBase);
            Assert.DoesNotContain(TipoObjeto.Paquete, new ProveedorSqlServer().Capacidades.TiposObjeto);
            Assert.DoesNotContain(TipoObjeto.Paquete, new ProveedorPostgreSql().Capacidades.TiposObjeto);
        }
    }
}
