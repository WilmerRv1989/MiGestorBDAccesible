using MiGestorBDAccesible.Core.Definicion;
using MiGestorBDAccesible.Core.Estructura;
using MiGestorBDAccesible.Core.Proveedores.Oracle;
using MiGestorBDAccesible.Core.Proveedores.PostgreSql;
using MiGestorBDAccesible.Core.Proveedores.SqlServer;

namespace MiGestorBDAccesible.Pruebas
{
    public class IndicesCheckPruebas
    {
        private static DefinicionIndiceModelo Indice(string esquema, string tabla, string nombre, params (string Nombre, bool Desc)[] columnas) => new()
        {
            Tabla = new TablaModelo { Esquema = esquema, Nombre = tabla },
            Nombre = nombre,
            Columnas = columnas.Select(c => new ColumnaDefinicionIndiceModelo { Nombre = c.Nombre, Descendente = c.Desc }).ToList()
        };

        [Fact]
        public void SqlServerIndiceConTodasLasOpciones()
        {
            var indice = Indice("dbo", "Clientes", "IX_Clientes_Apellido_Nombre", ("Apellido", false), ("Nombre", true));
            indice.EsUnico = true;
            indice.Incluidas = ["Correo"];
            indice.Filtro = "Activo = 1";

            Assert.Equal(string.Join(Environment.NewLine,
                "CREATE UNIQUE NONCLUSTERED INDEX [IX_Clientes_Apellido_Nombre] ON [dbo].[Clientes]",
                "    ([Apellido] ASC, [Nombre] DESC)",
                "    INCLUDE ([Correo])",
                "    WHERE Activo = 1;"), new ProveedorSqlServer().ScriptCrearIndice(indice));
        }

        [Fact]
        public void OracleIndiceGuardaElNombreEnMayusculas()
        {
            var indice = Indice("TIENDA", "CLIENTES", "ix_clientes_nombre", ("NOMBRE", false));

            Assert.Equal("CREATE INDEX TIENDA.IX_CLIENTES_NOMBRE\n    ON TIENDA.CLIENTES (NOMBRE);",
                new ProveedorOracle().ScriptCrearIndice(indice).Replace("\r\n", "\n"));
            Assert.Equal("DROP INDEX TIENDA.IX_CLIENTES_NOMBRE;",
                new ProveedorOracle().ScriptEliminarIndice(new TablaModelo { Esquema = "TIENDA", Nombre = "CLIENTES" }, new IndiceModelo { Nombre = "IX_CLIENTES_NOMBRE" }));
        }

        [Fact]
        public void PostgreSqlIndiceConMetodoYConcurrente()
        {
            var indice = Indice("public", "productos", "IX_Productos_Datos", ("datos", false));
            indice.Metodo = "gin";
            indice.Concurrente = true;

            Assert.Equal(string.Join(Environment.NewLine,
                "CREATE INDEX CONCURRENTLY ix_productos_datos ON productos USING gin",
                "    (datos);"), new ProveedorPostgreSql().ScriptCrearIndice(indice));
        }

        [Fact]
        public void CheckSinComprobarExistentesEnCadaMotor()
        {
            DefinicionCheckModelo Check(string esquema, string tabla) => new()
            {
                Tabla = new TablaModelo { Esquema = esquema, Nombre = tabla },
                Nombre = "CK_Precio",
                Condicion = "precio >= 0",
                ComprobarExistentes = false
            };

            Assert.Contains("WITH NOCHECK", new ProveedorSqlServer().ScriptCrearCheck(Check("dbo", "Productos")));
            Assert.EndsWith("CHECK (precio >= 0) ENABLE NOVALIDATE;", new ProveedorOracle().ScriptCrearCheck(Check("TIENDA", "PRODUCTOS")));
            Assert.EndsWith("ADD CONSTRAINT ck_precio CHECK (precio >= 0) NOT VALID;", new ProveedorPostgreSql().ScriptCrearCheck(Check("public", "productos")));
        }

        [Theory]
        [InlineData(ComparacionCheck.MayorOIgual, "0", null, "[Precio] >= 0")]
        [InlineData(ComparacionCheck.Entre, "1", "100", "[Precio] BETWEEN 1 AND 100")]
        [InlineData(ComparacionCheck.EnLista, "1, 2,3", null, "[Precio] IN (1, 2, 3)")]
        public void AsistenteConNumeros(ComparacionCheck comparacion, string valor, string? segundo, string esperado)
        {
            Assert.Equal(esperado, AsistenteCheckService.Condicion(new ProveedorSqlServer().Dialecto, "Precio", "decimal(10, 2)", comparacion, valor, segundo));
        }

        [Fact]
        public void AsistenteConTextoPoneComillasSegunElMotor()
        {
            Assert.Equal("[Estado] IN (N'Activo', N'O''Brien')",
                AsistenteCheckService.Condicion(new ProveedorSqlServer().Dialecto, "Estado", "nvarchar(20)", ComparacionCheck.EnLista, "Activo, O'Brien", null));
            Assert.Equal("ESTADO IN ('Activo', 'Baja')",
                AsistenteCheckService.Condicion(new ProveedorOracle().Dialecto, "ESTADO", "VARCHAR2(20)", ComparacionCheck.EnLista, "Activo, Baja", null));
            Assert.Equal("TRIM(ESTADO) IS NOT NULL",
                AsistenteCheckService.Condicion(new ProveedorOracle().Dialecto, "ESTADO", "VARCHAR2(20)", ComparacionCheck.NoVacio, null, null));
            Assert.Equal("estado <> ''",
                AsistenteCheckService.Condicion(new ProveedorPostgreSql().Dialecto, "estado", "varchar(20)", ComparacionCheck.NoVacio, null, null));
        }

        [Fact]
        public void AsistenteSinValorNoDaCondicion()
        {
            Assert.Null(AsistenteCheckService.Condicion(new ProveedorSqlServer().Dialecto, "Precio", "int", ComparacionCheck.Entre, "1", ""));
            Assert.Null(AsistenteCheckService.Condicion(new ProveedorSqlServer().Dialecto, "Precio", "int", ComparacionCheck.MayorQue, " ", null));
        }
    }
}
