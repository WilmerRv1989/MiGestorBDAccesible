using MiGestorBDAccesible.Core.Edicion;
using MiGestorBDAccesible.Core.Estructura;
using MiGestorBDAccesible.Core.Proveedores.Oracle;
using MiGestorBDAccesible.Core.Proveedores.PostgreSql;
using MiGestorBDAccesible.Core.Proveedores.SqlServer;

namespace MiGestorBDAccesible.Pruebas
{
    public class EdicionFilasPruebas
    {
        private static DetalleTablaModelo Clientes(string esquema, string tabla, string texto, string fecha, string logico) => new()
        {
            Tabla = new TablaModelo { Esquema = esquema, Nombre = tabla },
            Columnas =
            [
                new ColumnaModelo { Nombre = "id", TipoDatos = "int", EsIdentidad = true },
                new ColumnaModelo { Nombre = "nombre", TipoDatos = texto },
                new ColumnaModelo { Nombre = "alta", TipoDatos = fecha, PermiteNulos = true },
                new ColumnaModelo { Nombre = "activo", TipoDatos = logico, ValorPredeterminado = "1" },
                new ColumnaModelo { Nombre = "precio", TipoDatos = "decimal(10, 2)", PermiteNulos = true }
            ],
            Indices = [new IndiceModelo { Nombre = "pk", EsClavePrincipal = true, Columnas = [new ColumnaIndiceModelo { Nombre = "id" }] }]
        };

        [Fact]
        public void SinClavePrincipalUsaUnUnicoSinNulosONoHayClave()
        {
            var detalle = Clientes("dbo", "Clientes", "nvarchar(50)", "date", "bit");
            detalle.Indices.Clear();
            Assert.Null(EdicionFilasService.Clave(detalle));

            detalle.Indices.Add(new IndiceModelo { Nombre = "uq", EsUnico = true, Columnas = [new ColumnaIndiceModelo { Nombre = "nombre" }] });
            Assert.Equal(["nombre"], EdicionFilasService.Clave(detalle));
        }

        [Fact]
        public void ClasificaYMarcaSoloLectura()
        {
            var columnas = EdicionFilasService.Columnas(Clientes("dbo", "Clientes", "nvarchar(50)", "datetime2", "bit"), new ProveedorSqlServer().Dialecto);

            Assert.Equal([ClaseValor.Numero, ClaseValor.Texto, ClaseValor.Fecha, ClaseValor.Logico, ClaseValor.Numero], columnas.Select(c => c.Clase));
            Assert.True(columnas[0].SoloLectura);
            Assert.Equal("id (int, número automático, solo lectura)", columnas[0].Etiqueta);
            Assert.Equal("nombre (nvarchar(50), obligatoria)", columnas[1].Etiqueta);
            Assert.Equal(50, columnas[1].Longitud);
        }

        [Fact]
        public void SqlServerInsertaConLiteralesYOmiteLoAutomatico()
        {
            var d = new ProveedorSqlServer().Dialecto;
            var detalle = Clientes("dbo", "Clientes", "nvarchar(50)", "date", "bit");
            var columnas = EdicionFilasService.Columnas(detalle, d);

            var sql = EdicionFilasService.Insertar(d, detalle, columnas, new Dictionary<string, string?>
            {
                ["id"] = "5", ["nombre"] = "O'Brien", ["alta"] = "2026-01-31", ["precio"] = "12,5"
            });

            Assert.Equal($"INSERT INTO [dbo].[Clientes] ([nombre], [alta], [precio]){Environment.NewLine}VALUES (N'O''Brien', '2026-01-31', 12.5);", sql);
        }

        [Fact]
        public void ActualizaSoloLoQueCambioConLaClaveOriginal()
        {
            var d = new ProveedorPostgreSql().Dialecto;
            var detalle = Clientes("public", "clientes", "varchar(50)", "date", "boolean");
            var columnas = EdicionFilasService.Columnas(detalle, d);
            var original = new Dictionary<string, string?> { ["id"] = "3", ["nombre"] = "Ana", ["alta"] = null, ["activo"] = "1", ["precio"] = "10.00" };
            var nuevos = new Dictionary<string, string?>(original) { ["nombre"] = "", ["activo"] = "0" };

            Assert.Equal($"UPDATE clientes{Environment.NewLine}SET nombre = '', activo = false{Environment.NewLine}WHERE id = 3;",
                EdicionFilasService.Actualizar(d, detalle, columnas, ["id"], original, nuevos));
            Assert.Null(EdicionFilasService.Actualizar(d, detalle, columnas, ["id"], original, original));
            Assert.Equal($"DELETE FROM clientes{Environment.NewLine}WHERE id = 3;", EdicionFilasService.Eliminar(d, detalle, columnas, ["id"], original));
        }

        [Fact]
        public void OracleEscribeFechasConTipo()
        {
            var d = new ProveedorOracle().Dialecto;
            var detalle = Clientes("TIENDA", "CLIENTES", "VARCHAR2(50)", "DATE", "NUMBER(1)");
            var columnas = EdicionFilasService.Columnas(detalle, d);

            Assert.Equal("DATE '2026-01-31'", EdicionFilasService.Literal(d, columnas[2], "2026-01-31"));
            Assert.Equal("TO_DATE('2026-01-31 14:30:00', 'YYYY-MM-DD HH24:MI:SS')", EdicionFilasService.Literal(d, columnas[2], "2026-01-31 14:30:00"));
            Assert.Equal("NULL", EdicionFilasService.Literal(d, columnas[1], null));
        }

        [Fact]
        public void BuscarYPaginar()
        {
            var d = new ProveedorSqlServer().Dialecto;
            var detalle = Clientes("dbo", "Clientes", "nvarchar(50)", "date", "bit");
            var columnas = EdicionFilasService.Columnas(detalle, d);
            var condicion = EdicionFilasService.Condicion(d, columnas, "12", "[activo] = 1");

            Assert.Equal(" WHERE (UPPER([nombre]) LIKE UPPER(N'%12%') OR [id] = 12 OR [precio] = 12) AND ([activo] = 1)", condicion);
            Assert.Equal("SELECT [id], [nombre], [alta], [activo], [precio] FROM [dbo].[Clientes] WHERE (UPPER([nombre]) LIKE UPPER(N'%12%') OR [id] = 12 OR [precio] = 12) AND ([activo] = 1) ORDER BY [id] OFFSET 50 ROWS FETCH NEXT 50 ROWS ONLY",
                EdicionFilasService.ConsultaPagina(d, detalle, columnas, ["id"], condicion, 50));
        }

        [Fact]
        public void ValidaObligatoriasNumerosFechasYLargo()
        {
            var columnas = EdicionFilasService.Columnas(Clientes("dbo", "Clientes", "nvarchar(5)", "date", "bit"), new ProveedorSqlServer().Dialecto);

            var problemas = EdicionFilasService.Validar(columnas, new Dictionary<string, string?>
            {
                ["nombre"] = "Demasiado largo", ["alta"] = "31/01/2026", ["precio"] = "doce", ["activo"] = null
            }, false);

            Assert.Equal(4, problemas.Count);
            Assert.Contains(problemas, p => p.StartsWith("nombre admite como mucho 5 caracteres"));
            Assert.Contains(problemas, p => p.StartsWith("alta debe ser una fecha"));
            Assert.Contains(problemas, p => p.StartsWith("precio debe ser un número"));
            Assert.Contains(problemas, p => p.StartsWith("activo es obligatoria"));
        }

        [Fact]
        public void ResumenDeUnaFila()
        {
            var columnas = EdicionFilasService.Columnas(Clientes("dbo", "Clientes", "nvarchar(50)", "date", "bit"), new ProveedorSqlServer().Dialecto);

            Assert.Equal("Fila 3: Ana, vacío, 1", EdicionFilasService.Resumen(columnas, ["id"], ["3", "Ana", null, "1", "10.00"]));
        }
    }
}
