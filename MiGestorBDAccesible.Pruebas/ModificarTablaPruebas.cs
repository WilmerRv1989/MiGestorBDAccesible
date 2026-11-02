using MiGestorBDAccesible.Core.Definicion;
using MiGestorBDAccesible.Core.Estructura;
using MiGestorBDAccesible.Core.Proveedores;
using MiGestorBDAccesible.Core.Proveedores.Oracle;
using MiGestorBDAccesible.Core.Proveedores.PostgreSql;
using MiGestorBDAccesible.Core.Proveedores.SqlServer;

namespace MiGestorBDAccesible.Pruebas
{
    public class ModificarTablaPruebas
    {
        private static DetalleTablaModelo Clientes(string esquema = "dbo", string id = "Id", string nombre = "Nombre", string correo = "Correo", string fax = "Fax", string tabla = "Clientes") => new()
        {
            Tabla = new TablaModelo { Esquema = esquema, Nombre = tabla, EsquemaPredeterminado = esquema },
            Filas = 4,
            Columnas =
            [
                new ColumnaModelo { Nombre = id, TipoDatos = "int", EsIdentidad = true, SemillaIdentidad = 1, IncrementoIdentidad = 1 },
                new ColumnaModelo { Nombre = nombre, TipoDatos = "nvarchar(100)" },
                new ColumnaModelo { Nombre = correo, TipoDatos = "nvarchar(150)", PermiteNulos = true },
                new ColumnaModelo { Nombre = fax, TipoDatos = "nvarchar(20)", PermiteNulos = true, ValorPredeterminado = "(N'x')", NombrePredeterminado = "DF_Clientes_Fax" }
            ],
            Indices =
            [
                new IndiceModelo { Nombre = "PK_Clientes", EsClavePrincipal = true, EsUnico = true, Columnas = [new ColumnaIndiceModelo { Nombre = id }] },
                new IndiceModelo { Nombre = "UQ_Clientes_Correo", EsRestriccionUnica = true, EsUnico = true, Columnas = [new ColumnaIndiceModelo { Nombre = correo }] }
            ]
        };

        private static PlanModificacionModelo Comparar(IProveedorBaseDatos proveedor, DetalleTablaModelo detalle, Action<DefinicionTablaModelo> cambiar)
        {
            var definicion = ComparadorTablaService.DesdeDetalle(detalle, proveedor.TiposDatos);
            cambiar(definicion);
            return ComparadorTablaService.Comparar(detalle, definicion, proveedor.TiposDatos, proveedor.Capacidades.Terminos, proveedor.NombreGuardado);
        }

        [Fact]
        public void SinCambiosNoHayNada()
        {
            var plan = Comparar(new ProveedorSqlServer(), Clientes(), _ => { });

            Assert.False(plan.HayCambios);
        }

        [Fact]
        public void DesdeDetalleQuitaParentesisDelPredeterminadoYMarcaClaves()
        {
            var definicion = ComparadorTablaService.DesdeDetalle(Clientes(), new ProveedorSqlServer().TiposDatos);

            Assert.Equal("N'x'", definicion.Columnas[3].ValorPredeterminado);
            Assert.True(definicion.Columnas[0].EsClavePrincipal);
            Assert.True(definicion.Columnas[2].EsUnica);
            Assert.Equal("nvarchar(150)", definicion.Columnas[2].TipoCompleto);
            Assert.All(definicion.Columnas, c => Assert.Equal(c.Nombre, c.NombreOriginal));
        }

        [Fact]
        public void FrasesEnElOrdenEnQueSeAplican()
        {
            var plan = Comparar(new ProveedorSqlServer(), Clientes(), d =>
            {
                d.Nombre = "Personas";
                d.Columnas[1].Nombre = "NombreCompleto";
                d.Columnas[2].PermiteNulos = false;
                d.Columnas.RemoveAt(3);
                d.Columnas.Add(new DefinicionColumnaModelo { Nombre = "Telefono", Tipo = "varchar", Longitud = 30, PermiteNulos = true });
            });

            Assert.Equal(
            [
                "Quitar la columna Fax y sus datos (DROP COLUMN)",
                "Cambiar el nombre de la columna Nombre a NombreCompleto (RENAME COLUMN)",
                "Hacer obligatoria la columna Correo (NOT NULL)",
                "Agregar la columna Telefono: varchar(30), opcional (ADD)",
                "Cambiar el nombre de la tabla Clientes a Personas (RENAME)"
            ], plan.Cambios.Select(c => c.Frase));
            Assert.Empty(plan.MotivosRecreacion);
        }

        [Fact]
        public void CambiarElOrdenOAgregarEnMedioObligaARecrear()
        {
            var proveedor = new ProveedorSqlServer();

            var orden = Comparar(proveedor, Clientes(), d => (d.Columnas[1], d.Columnas[2]) = (d.Columnas[2], d.Columnas[1]));
            Assert.Contains("orden de las columnas", Assert.Single(orden.MotivosRecreacion));
            Assert.Contains("sus 4 filas", orden.MotivosRecreacion[0]);

            var enMedio = Comparar(proveedor, Clientes(), d => d.Columnas.Insert(1, new DefinicionColumnaModelo { Nombre = "Apodo", Tipo = "nvarchar", Longitud = 20, PermiteNulos = true }));
            Assert.Contains("entre las que ya existen", Assert.Single(enMedio.MotivosRecreacion));
        }

        [Fact]
        public void SqlServerQuitaYVuelveACrearElPredeterminadoAlCambiarElTipo()
        {
            var proveedor = new ProveedorSqlServer();
            var plan = Comparar(proveedor, Clientes(), d => d.Columnas[3].Longitud = 40);
            var script = proveedor.ScriptModificarTabla(plan);

            Assert.Equal(
            [
                "ALTER TABLE [dbo].[Clientes] DROP CONSTRAINT [DF_Clientes_Fax];",
                "ALTER TABLE [dbo].[Clientes] ALTER COLUMN [Fax] nvarchar(40) NULL;",
                "ALTER TABLE [dbo].[Clientes] ADD CONSTRAINT [DF_Clientes_Fax] DEFAULT (N'x') FOR [Fax];"
            ], script.Sentencias.Select(s => s.Sql));
            Assert.StartsWith("SET XACT_ABORT ON;", script.Texto);
            Assert.EndsWith("COMMIT TRANSACTION;", script.Texto);
        }

        [Fact]
        public void SqlServerQuitarColumnaQuitaAntesSuPredeterminadoYSuUnica()
        {
            var proveedor = new ProveedorSqlServer();
            var plan = Comparar(proveedor, Clientes(), d => { d.Columnas.RemoveAt(3); d.Columnas.RemoveAt(2); });
            var sql = proveedor.ScriptModificarTabla(plan).Sentencias.Select(s => s.Sql).ToList();

            Assert.Equal("ALTER TABLE [dbo].[Clientes] DROP CONSTRAINT [UQ_Clientes_Correo];", sql[0]);
            Assert.Equal("ALTER TABLE [dbo].[Clientes] DROP CONSTRAINT [DF_Clientes_Fax];", sql[1]);
            Assert.Contains("ALTER TABLE [dbo].[Clientes] DROP COLUMN [Correo];", sql);
            Assert.Contains("ALTER TABLE [dbo].[Clientes] DROP COLUMN [Fax];", sql);
        }

        [Fact]
        public void SqlServerIdentidadNoSeAplicaDesdeElFormulario()
        {
            var proveedor = new ProveedorSqlServer();
            var plan = Comparar(proveedor, Clientes(), d => d.Columnas[0].EsIdentidad = false);
            var script = proveedor.ScriptModificarTabla(plan);

            Assert.Empty(script.Sentencias);
            Assert.Contains("SQL Server solo lo permite creando la tabla de nuevo", Assert.Single(script.NoAplicables));
        }

        [Fact]
        public void SqlServerRenombraConSpRenameYAgregaConPredeterminado()
        {
            var proveedor = new ProveedorSqlServer();
            var plan = Comparar(proveedor, Clientes(), d =>
            {
                d.Nombre = "Personas";
                d.Columnas[1].Nombre = "Nombre Completo";
                d.Columnas.Add(new DefinicionColumnaModelo { Nombre = "Activo", Tipo = "bit", ValorPredeterminado = "1" });
            });
            var sql = proveedor.ScriptModificarTabla(plan).Sentencias.Select(s => s.Sql).ToList();

            Assert.Equal(
            [
                "EXEC sp_rename N'[dbo].[Clientes].[Nombre]', N'Nombre Completo', N'COLUMN';",
                "ALTER TABLE [dbo].[Clientes] ADD [Activo] bit NOT NULL CONSTRAINT [DF_Personas_Activo] DEFAULT (1);",
                "EXEC sp_rename N'[dbo].[Clientes]', N'Personas';"
            ], sql);
        }

        [Fact]
        public void OracleUsaModifyYRenameColumn()
        {
            var proveedor = new ProveedorOracle();
            var detalle = Clientes("TIENDA", "ID", "NOMBRE", "CORREO", "FAX", "CLIENTES");
            detalle.Columnas[1] = new ColumnaModelo { Nombre = "NOMBRE", TipoDatos = "VARCHAR2(100)" };
            detalle.Columnas[3] = new ColumnaModelo { Nombre = "FAX", TipoDatos = "VARCHAR2(20)", PermiteNulos = true, ValorPredeterminado = "'x' " };
            detalle.Columnas[2] = new ColumnaModelo { Nombre = "CORREO", TipoDatos = "VARCHAR2(150)", PermiteNulos = true };
            detalle.Columnas[0] = new ColumnaModelo { Nombre = "ID", TipoDatos = "NUMBER", EsIdentidad = true, SemillaIdentidad = 1, IncrementoIdentidad = 1 };

            var plan = Comparar(proveedor, detalle, d =>
            {
                d.Columnas[1].Nombre = "nombre_completo";
                d.Columnas[1].Longitud = 200;
                d.Columnas[2].PermiteNulos = false;
                d.Columnas[3].ValorPredeterminado = null;
                d.Columnas[0].EsIdentidad = false;
            });
            var script = proveedor.ScriptModificarTabla(plan);

            Assert.Equal(
            [
                "ALTER TABLE TIENDA.CLIENTES RENAME COLUMN NOMBRE TO NOMBRE_COMPLETO;",
                "ALTER TABLE TIENDA.CLIENTES MODIFY (NOMBRE_COMPLETO VARCHAR2(200));",
                "ALTER TABLE TIENDA.CLIENTES MODIFY (CORREO NOT NULL);",
                "ALTER TABLE TIENDA.CLIENTES MODIFY (FAX DEFAULT NULL);",
                "ALTER TABLE TIENDA.CLIENTES MODIFY (ID DROP IDENTITY);"
            ], script.Sentencias.Select(s => s.Sql));
            Assert.Empty(script.NoAplicables);
            Assert.DoesNotContain("BEGIN", script.Texto);
        }

        [Fact]
        public void PostgreSqlUsaUsingYVaEnTransaccion()
        {
            var proveedor = new ProveedorPostgreSql();
            var detalle = Clientes("public", "id", "nombre", "correo", "fax", "clientes");
            detalle.Columnas[0] = new ColumnaModelo { Nombre = "id", TipoDatos = "integer", EsIdentidad = true, SemillaIdentidad = 1, IncrementoIdentidad = 1 };
            detalle.Columnas[1] = new ColumnaModelo { Nombre = "nombre", TipoDatos = "varchar(100)" };
            detalle.Columnas[2] = new ColumnaModelo { Nombre = "correo", TipoDatos = "varchar(150)", PermiteNulos = true };
            detalle.Columnas[3] = new ColumnaModelo { Nombre = "fax", TipoDatos = "varchar(20)", PermiteNulos = true, ValorPredeterminado = "'x'::character varying" };

            var plan = Comparar(proveedor, detalle, d =>
            {
                d.Columnas[1].Longitud = 50;
                d.Columnas[2].PermiteNulos = false;
                d.Columnas[3].ValorPredeterminado = "'-'";
            });
            var script = proveedor.ScriptModificarTabla(plan);

            Assert.Equal(
            [
                "ALTER TABLE clientes ALTER COLUMN nombre TYPE varchar(50) USING nombre::varchar(50);",
                "ALTER TABLE clientes ALTER COLUMN correo SET NOT NULL;",
                "ALTER TABLE clientes ALTER COLUMN fax SET DEFAULT '-';"
            ], script.Sentencias.Select(s => s.Sql));
            Assert.StartsWith("BEGIN;", script.Texto);
            Assert.EndsWith("COMMIT;", script.Texto);
        }

        [Fact]
        public void CambiarClavePrincipalLaQuitaYLaCreaDeNuevo()
        {
            var plan = Comparar(new ProveedorSqlServer(), Clientes(), d =>
            {
                d.Columnas[0].EsClavePrincipal = false;
                d.Columnas[1].EsClavePrincipal = true;
            });

            Assert.Equal(TipoCambioTabla.QuitarClavePrincipal, plan.Cambios[0].Tipo);
            Assert.Equal("PK_Clientes", plan.Cambios[0].Restriccion);
            Assert.Equal("Crear la clave principal con Nombre (PRIMARY KEY)", plan.Cambios.Single(c => c.Tipo == TipoCambioTabla.CrearClavePrincipal).Frase);
        }

        [Fact]
        public void UnNombreConMayusculasEntreComillasNoSeRenombraSolo()
        {
            var proveedor = new ProveedorPostgreSql();
            var detalle = Clientes("public", "Id", "Nombre", "Correo", "Fax", "Clientes");
            detalle.Columnas[1] = new ColumnaModelo { Nombre = "Nombre", TipoDatos = "varchar(100)" };

            var plan = Comparar(proveedor, detalle, d => d.Columnas[1].Longitud = 120);
            var script = proveedor.ScriptModificarTabla(plan);

            Assert.Equal("ALTER TABLE \"Clientes\" ALTER COLUMN \"Nombre\" TYPE varchar(120) USING \"Nombre\"::varchar(120);", Assert.Single(script.Sentencias).Sql);
        }

        [Fact]
        public void RenombrarSoloLaColumnaDeLaClaveNoTocaLaClave()
        {
            var plan = Comparar(new ProveedorSqlServer(), Clientes(), d => d.Columnas[0].Nombre = "ClienteId");

            Assert.Equal(TipoCambioTabla.RenombrarColumna, Assert.Single(plan.Cambios).Tipo);
        }
    }
}
