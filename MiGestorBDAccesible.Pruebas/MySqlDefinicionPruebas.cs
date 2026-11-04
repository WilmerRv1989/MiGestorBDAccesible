using MiGestorBDAccesible.Core.Definicion;
using MiGestorBDAccesible.Core.Edicion;
using MiGestorBDAccesible.Core.Estructura;
using MiGestorBDAccesible.Core.Proveedores.MySql;

namespace MiGestorBDAccesible.Pruebas
{
    public class MySqlDefinicionPruebas
    {
        private static readonly ProveedorMySql Proveedor = new();

        private static TablaModelo Tabla(string nombre) => new() { Esquema = "", Nombre = nombre, EsquemaPredeterminado = "" };

        [Fact]
        public void CreaLaTablaConAutoIncrementYMotorInnoDb()
        {
            var tabla = new DefinicionTablaModelo { Esquema = "", Nombre = "resenas" };
            tabla.Columnas.Add(new DefinicionColumnaModelo { Nombre = "id", Tipo = "int", EsClavePrincipal = true, EsIdentidad = true });
            tabla.Columnas.Add(new DefinicionColumnaModelo { Nombre = "texto", Tipo = "varchar", Longitud = 200, PermiteNulos = true, ValorPredeterminado = "'sin texto'" });
            tabla.Columnas.Add(new DefinicionColumnaModelo
            {
                Nombre = "producto_id",
                Tipo = "int",
                Referencia = new ReferenciaModelo { TablaDestino = Tabla("productos"), ColumnaDestino = "id", AlEliminar = ReglaReferencial.Cascada }
            });

            var script = Proveedor.ScriptCrearTabla(tabla).Replace("\r\n", "\n");

            Assert.Equal("""
                CREATE TABLE resenas (
                    id int NOT NULL AUTO_INCREMENT,
                    texto varchar(200) DEFAULT 'sin texto',
                    producto_id int NOT NULL,
                    CONSTRAINT pk_resenas PRIMARY KEY (id),
                    CONSTRAINT fk_resenas_productos_producto_id FOREIGN KEY (producto_id) REFERENCES productos (id) ON DELETE CASCADE
                ) ENGINE = InnoDB;
                """.Replace("\r\n", "\n"), script);
        }

        [Fact]
        public void ElValorInicialVaEnLaTabla()
        {
            var tabla = new DefinicionTablaModelo { Esquema = "", Nombre = "t" };
            tabla.Columnas.Add(new DefinicionColumnaModelo { Nombre = "id", Tipo = "int", EsClavePrincipal = true, EsIdentidad = true, Semilla = 1000 });

            Assert.EndsWith(") ENGINE = InnoDB AUTO_INCREMENT = 1000;", Proveedor.ScriptCrearTabla(tabla));
        }

        [Fact]
        public void LaLlaveSinComprobarApagaLasComprobacionesSoloParaElla()
        {
            var llave = new DefinicionLlaveForaneaModelo
            {
                TablaOrigen = Tabla("pedidos"),
                ColumnaOrigen = "cliente_id",
                Referencia = new ReferenciaModelo { TablaDestino = Tabla("clientes"), ColumnaDestino = "id" },
                ComprobarExistentes = false
            };

            var lineas = Proveedor.ScriptCrearLlaveForanea(llave).Split(Environment.NewLine);

            Assert.Equal("SET FOREIGN_KEY_CHECKS = 0;", lineas[0]);
            Assert.Equal("SET FOREIGN_KEY_CHECKS = 1;", lineas[^1]);
        }

        [Fact]
        public void LosIndicesUsanSuClase()
        {
            var normal = new DefinicionIndiceModelo { Tabla = Tabla("t"), Nombre = "ix_a", EsUnico = true, Columnas = [new() { Nombre = "a", Descendente = true }] };
            var texto = new DefinicionIndiceModelo { Tabla = Tabla("t"), Nombre = "ft_a", Metodo = "FULLTEXT", Columnas = [new() { Nombre = "a" }] };

            Assert.StartsWith("CREATE UNIQUE INDEX ix_a ON t", Proveedor.ScriptCrearIndice(normal));
            Assert.EndsWith("(a DESC);", Proveedor.ScriptCrearIndice(normal));
            Assert.StartsWith("CREATE FULLTEXT INDEX ft_a ON t", Proveedor.ScriptCrearIndice(texto));
            Assert.Equal("DROP INDEX ix_a ON t;", Proveedor.ScriptEliminarIndice(Tabla("t"), new IndiceModelo { Nombre = "ix_a" }));
        }

        [Fact]
        public void ModificarUsaLaDefinicionCompletaDeLaColumna()
        {
            var detalle = new DetalleTablaModelo
            {
                Tabla = Tabla("t"),
                Columnas =
                [
                    new ColumnaModelo { Nombre = "id", TipoDatos = "int", EsIdentidad = true, IncrementoIdentidad = 1, SiguienteIdentidad = 5 },
                    new ColumnaModelo { Nombre = "nota", TipoDatos = "varchar(50)", PermiteNulos = true, ValorPredeterminado = "'x'" }
                ],
                Indices = [new IndiceModelo { Nombre = "PRIMARY", EsClavePrincipal = true, EsUnico = true, Columnas = [new() { Nombre = "id" }] }]
            };

            var nueva = ComparadorTablaService.DesdeDetalle(detalle, Proveedor.TiposDatos);
            nueva.Columnas[1].Longitud = 80;
            nueva.Columnas[1].Nombre = "comentario";
            var plan = ComparadorTablaService.Comparar(detalle, nueva, Proveedor.TiposDatos, Proveedor.Capacidades.Terminos, Proveedor.NombreGuardado);

            var sentencias = Proveedor.ScriptModificarTabla(plan).Sentencias.Select(s => s.Sql).ToList();

            Assert.Equal(["ALTER TABLE t RENAME COLUMN nota TO comentario;", "ALTER TABLE t MODIFY COLUMN comentario varchar(80) DEFAULT 'x';"], sentencias);
        }

        [Fact]
        public void ElDialectoPaginaConLimit()
        {
            var d = Proveedor.Dialecto;
            var detalle = new DetalleTablaModelo { Tabla = Tabla("order") };
            var columnas = new List<ColumnaEdicionModelo> { new() { Columna = new ColumnaModelo { Nombre = "id", TipoDatos = "int" } } };

            Assert.EndsWith("ORDER BY id LIMIT 50 OFFSET 100", EdicionFilasService.ConsultaPagina(d, detalle, columnas, ["id"], "", 100));
            Assert.Contains("FROM `order`", EdicionFilasService.ConsultaPagina(d, detalle, columnas, ["id"], "", 0));
            Assert.Equal("INSERT INTO t () VALUES ();", d.InsertarPredeterminados("t"));
            Assert.Equal("'d''Ana \\\\ x'", d.Texto("varchar", "d'Ana \\ x"));
            Assert.True(d.SoloLectura("longblob"));
            Assert.False(d.SoloLectura("varchar(10)"));
        }

        [Theory]
        [InlineData("int unsigned", ClaseValor.Numero)]
        [InlineData("mediumint", ClaseValor.Numero)]
        [InlineData("double", ClaseValor.Numero)]
        [InlineData("enum('a','b')", ClaseValor.Texto)]
        [InlineData("longtext", ClaseValor.Texto)]
        [InlineData("datetime", ClaseValor.Fecha)]
        public void ClasificaLosTiposDeMySql(string tipo, ClaseValor esperada)
        {
            Assert.Equal(esperada, EdicionFilasService.Clasificar(tipo));
        }

        [Theory]
        [InlineData("int", "int", true)]
        [InlineData("int", "bigint", false)]
        [InlineData("int unsigned", "int", false)]
        [InlineData("varchar(10)", "char(10)", true)]
        public void LaLlaveExigeElMismoTipo(string origen, string destino, bool compatibles)
        {
            Assert.Equal(compatibles, ProveedorMySqlBase.TiposCompatiblesMySql(origen, destino));
        }

        [Fact]
        public void UnaLlaveQueYaExisteNoSeRechazaPorNoConocerElTipoDestino()
        {
            var referencia = new ReferenciaModelo { TablaDestino = Tabla("clientes"), ColumnaDestino = "id" };

            var problemas = new ValidadorTablaService().ValidarReferencia("cliente_id", "int", true, false, referencia, Proveedor.Capacidades.Terminos);

            Assert.Empty(problemas);
        }

        [Fact]
        public void AvisaDeMayusculasYDeNombresConAcentosGraves()
        {
            Assert.Null(Proveedor.AvisoNombre("clientes"));
            Assert.Contains("distingue mayúsculas", Proveedor.AvisoNombre("Clientes"));
            Assert.Contains("`mi tabla`", Proveedor.AvisoNombre("mi tabla"));
        }
    }
}
