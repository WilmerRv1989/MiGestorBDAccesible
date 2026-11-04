using MiGestorBDAccesible.Core.Conexiones;
using MiGestorBDAccesible.Core.Definicion;
using MiGestorBDAccesible.Core.Ejecucion;
using MiGestorBDAccesible.Core.Estructura;
using MiGestorBDAccesible.Core.Proveedores;
using MiGestorBDAccesible.Core.Proveedores.MySql;
using MySqlConnector;

namespace MiGestorBDAccesible.Pruebas
{
    public class MySqlPruebas
    {
        [Fact]
        public void LosMotoresSeOfrecenEnOrdenAlfabetico()
        {
            Assert.Equal(["MariaDB", "MySQL", "Oracle", "PostgreSQL", "SQL Server"], TipoMotorExtensiones.EnOrdenAlfabetico.Select(m => m.Nombre()));
        }

        [Fact]
        public void MySqlYMariaDbSonMotoresDistintos()
        {
            var proveedores = new ProveedoresService();

            Assert.IsType<ProveedorMySql>(proveedores.Obtener(TipoMotor.MySql));
            Assert.IsType<ProveedorMariaDb>(proveedores.Obtener(TipoMotor.MariaDb));
            Assert.Equal("MySQL", TipoMotor.MySql.Nombre());
            Assert.Equal("MariaDB", TipoMotor.MariaDb.Nombre());
            Assert.Equal(3306, TipoMotor.MySql.PuertoPredeterminado());
            Assert.Equal(3306, TipoMotor.MariaDb.PuertoPredeterminado());
        }

        [Fact]
        public void NoTienenEsquemasNiDisparadoresQueSeDeshabiliten()
        {
            foreach (IProveedorBaseDatos proveedor in new ProveedorMySqlBase[] { new ProveedorMySql(), new ProveedorMariaDb() })
            {
                var capacidades = proveedor.Capacidades;

                Assert.False(capacidades.TieneEsquemas);
                Assert.True(capacidades.PuedeCrearContenedores);
                Assert.True(capacidades.PuedeDisenarTablas);
                Assert.False(capacidades.PuedeDeshabilitarDisparadores);
                Assert.True(capacidades.SalidasEnResultados);
                Assert.DoesNotContain(ReglaReferencial.EstablecerPredeterminado, capacidades.ReglasAlEliminar);
                Assert.Contains(ReglaReferencial.Restringir, capacidades.ReglasAlEliminar);
                Assert.Equal("", proveedor.EsquemaPredeterminado(null!));
            }
        }

        [Theory]
        [InlineData("8.4.13", 8, 4)]
        [InlineData("8.0.36-log", 8, 0)]
        [InlineData("11.8.9-MariaDB-ubu2404", 11, 8)]
        [InlineData("10.6.21-MariaDB", 10, 6)]
        [InlineData("desconocida", 0, 0)]
        public void LeeLaVersionDelServidor(string version, int principal, int menor)
        {
            Assert.Equal((principal, menor), ProveedorMySqlBase.LeerVersion(version));
        }

        [Fact]
        public void AvisaDesdeQueVersionFunciona()
        {
            var mysql = new ProveedorMySql().Capacidades;
            var mariadb = new ProveedorMariaDb().Capacidades;

            Assert.Null(mysql.AvisoVersion(804));
            Assert.Null(mysql.AvisoVersion(800));
            Assert.Contains("desde MySQL 8.0", mysql.AvisoVersion(507));
            Assert.Null(mariadb.AvisoVersion(1108));
            Assert.Null(mariadb.AvisoVersion(1006));
            Assert.Contains("desde MariaDB 10.6", mariadb.AvisoVersion(1005));
        }

        [Fact]
        public void LaCadenaDeConexionUsaElPuertoYLaBaseInicial()
        {
            var conexion = new ConexionModelo { Motor = TipoMotor.MySql, Servidor = "localhost", Puerto = 3307, Usuario = "tienda", BaseDatosInicial = "tiendapractica" };
            var cadena = new MySqlConnectionStringBuilder(new ProveedorMySql().CadenaConexion(conexion, "secreta"));

            Assert.Equal("localhost", cadena.Server);
            Assert.Equal(3307u, cadena.Port);
            Assert.Equal("tienda", cadena.UserID);
            Assert.Equal("secreta", cadena.Password);
            Assert.Equal("tiendapractica", cadena.Database);
            Assert.Equal(MySqlSslMode.Preferred, cadena.SslMode);
        }

        [Theory]
        [InlineData("int(11)", "int")]
        [InlineData("int(10) unsigned", "int unsigned")]
        [InlineData("bigint(20)", "bigint")]
        [InlineData("tinyint(1)", "tinyint(1)")]
        [InlineData("tinyint(4)", "tinyint")]
        [InlineData("decimal(10,2)", "decimal(10, 2)")]
        [InlineData("enum('nuevo','enviado')", "enum('nuevo','enviado')")]
        public void LosTiposSeLeenSinAnchoDeMostrar(string tipo, string esperado)
        {
            Assert.Equal(esperado, ProveedorMySqlBase.FormatearTipo(tipo));
        }

        [Theory]
        [InlineData("nuevo", "", "'nuevo'")]
        [InlineData("'nuevo'", "", "'nuevo'")]
        [InlineData("1", "", "1")]
        [InlineData("10.50", "", "10.50")]
        [InlineData("curdate()", "DEFAULT_GENERATED", "curdate()")]
        [InlineData("current_timestamp()", "", "current_timestamp()")]
        [InlineData("CURRENT_TIMESTAMP", "DEFAULT_GENERATED", "CURRENT_TIMESTAMP")]
        [InlineData("NULL", "", null)]
        [InlineData(null, "", null)]
        public void ElValorPredeterminadoSeMuestraComoSeEscribe(string? valor, string extra, string? esperado)
        {
            Assert.Equal(esperado, ProveedorMySqlBase.FormatearPredeterminado(valor, extra, "varchar(20)"));
        }

        [Fact]
        public void LaRestriccionCheckSeLeeSinAcentosGraves()
        {
            var columnas = new List<ColumnaModelo> { new() { Nombre = "precio" }, new() { Nombre = "cantidad" } };

            var mysql = ProveedorMySqlBase.LeerRestriccion("ck_precio", "(`precio` >= 0)", columnas);
            var mariadb = ProveedorMySqlBase.LeerRestriccion("ck_precio", "`precio` >= 0", columnas);
            var varias = ProveedorMySqlBase.LeerRestriccion("ck_total", "(`precio` * `cantidad` < 1000)", columnas);

            Assert.Equal("precio >= 0", mysql.Definicion);
            Assert.Equal("precio", mysql.Columna);
            Assert.Equal("precio >= 0", mariadb.Definicion);
            Assert.Equal("precio", mariadb.Columna);
            Assert.Null(varias.Columna);
        }

        [Fact]
        public void ElNumeroAutomaticoDiceCualEsElSiguiente()
        {
            var terminos = new ProveedorMySql().Capacidades.Terminos;
            var columna = new ColumnaModelo { Nombre = "id", TipoDatos = "int", EsIdentidad = true, IncrementoIdentidad = 1, SiguienteIdentidad = 7 };
            var detalle = new DetalleTablaModelo { Tabla = new TablaModelo { Esquema = "", Nombre = "productos", EsquemaPredeterminado = "" }, Columnas = [columna] };

            var frase = new Core.Descripcion.NarradorTablaService().DescribirColumna(columna, detalle, terminos);

            Assert.Equal("id: int, número automático que sube de 1 en 1 (AUTO_INCREMENT), el siguiente será 7, obligatoria (NOT NULL).", frase);
        }

        [Fact]
        public void AvisaCuandoLaTablaNoEsInnoDb()
        {
            var narrador = new Core.Descripcion.NarradorTablaService();
            var tabla = new TablaModelo { Esquema = "", Nombre = "viejos", EsquemaPredeterminado = "" };

            var myisam = narrador.Describir(new DetalleTablaModelo { Tabla = tabla, Almacenamiento = "MyISAM" });
            var innodb = narrador.Describir(new DetalleTablaModelo { Tabla = tabla, Almacenamiento = "InnoDB" });

            Assert.Contains("Se guarda con el motor MyISAM (ENGINE), que no admite llaves foráneas ni transacciones.", myisam.Resumen);
            Assert.DoesNotContain(innodb.Resumen, f => f.Contains("ENGINE"));
        }

        private static List<string> Palabras(string texto) =>
            System.Text.RegularExpressions.Regex.Matches(texto, "[A-Za-z_][A-Za-z0-9_]*").Select(m => m.Value.ToUpperInvariant()).Take(12).ToList();

        [Fact]
        public void LosCambiosQuedanPendientesSoloDentroDeUnaTransaccion()
        {
            var estado = new ProveedorMySqlBase.EstadoTransaccionMySql();
            void Ejecutar(string sql) => ProveedorMySqlBase.ActualizarTransaccionPara(estado, Palabras(sql), sql);

            Ejecutar("INSERT INTO t VALUES (1)");
            Assert.False(estado.CambiosPendientes);

            Ejecutar("START TRANSACTION");
            Ejecutar("UPDATE t SET x = 1");
            Assert.True(estado.CambiosPendientes);

            Ejecutar("ROLLBACK TO SAVEPOINT a");
            Assert.True(estado.CambiosPendientes);

            Ejecutar("COMMIT");
            Assert.False(estado.CambiosPendientes);

            Ejecutar("SET autocommit = 0");
            Ejecutar("DELETE FROM t");
            Assert.True(estado.CambiosPendientes);

            Ejecutar("CREATE TABLE u (id int)");
            Assert.False(estado.CambiosPendientes);

            Ejecutar("SET autocommit = 1");
            Ejecutar("DELETE FROM t");
            Assert.False(estado.CambiosPendientes);
        }

        [Theory]
        [InlineData("CREATE TABLE t (id int)", true)]
        [InlineData("ALTER TABLE t ADD c int", true)]
        [InlineData("TRUNCATE TABLE t", true)]
        [InlineData("START TRANSACTION", true)]
        [InlineData("CREATE TEMPORARY TABLE t (id int)", false)]
        [InlineData("INSERT INTO t VALUES (1)", false)]
        [InlineData("SELECT 1", false)]
        public void SabeQueSentenciasConfirmanSolas(string sql, bool confirma)
        {
            Assert.Equal(confirma, ProveedorMySqlBase.ConfirmaImplicitamente(Palabras(sql)));
        }

        [Theory]
        [InlineData("CREATE OR REPLACE VIEW v AS SELECT 1", "Vista creada (CREATE VIEW).")]
        [InlineData("DROP PROCEDURE IF EXISTS p", "Procedimiento eliminado (DROP PROCEDURE).")]
        [InlineData("CREATE DEFINER = CURRENT_USER TRIGGER t", "Disparador creado (CREATE TRIGGER).")]
        [InlineData("USE tiendapractica", "Base de datos cambiada (USE).")]
        [InlineData("START TRANSACTION", "Transacción iniciada (START TRANSACTION): los cambios quedan pendientes hasta COMMIT o ROLLBACK.")]
        public void AnunciaQueHizoCadaSentencia(string sql, string esperado)
        {
            Assert.Equal(esperado, ProveedorMySqlBase.TextoSentencia(Palabras(sql)));
        }

        [Fact]
        public void LosValoresSeMuestranComoEnMySql()
        {
            Assert.Equal("2026-01-15", ProveedorMySqlBase.FormatearValor(new DateTime(2026, 1, 15), "DATE"));
            Assert.Equal("2026-01-15 08:30:00", ProveedorMySqlBase.FormatearValor(new DateTime(2026, 1, 15, 8, 30, 0), "DATETIME"));
            Assert.Equal("0000-00-00", ProveedorMySqlBase.FormatearValor(new MySqlDateTime(), "DATE"));
            Assert.Equal("26:05:00", ProveedorMySqlBase.FormatearValor(new TimeSpan(1, 2, 5, 0), "TIME"));
            Assert.Equal("0x0A1B", ProveedorMySqlBase.FormatearValor(new byte[] { 10, 27 }, "BLOB"));
            Assert.Equal("1", ProveedorMySqlBase.FormatearValor(true, "BIT"));
            Assert.Equal("59.90", ProveedorMySqlBase.FormatearValor(59.90m, "DECIMAL"));
        }

        [Fact]
        public void LasSecuenciasSoloExistenEnMariaDb()
        {
            Assert.DoesNotContain(TipoObjeto.Secuencia, new ProveedorMySql().Capacidades.TiposObjeto);
            Assert.Contains(TipoObjeto.Secuencia, new ProveedorMariaDb().Capacidades.TiposObjeto);
        }

        [Theory]
        [InlineData("clientes", "clientes")]
        [InlineData("order", "`order`")]
        [InlineData("con espacio", "`con espacio`")]
        [InlineData("raro`nombre", "`raro``nombre`")]
        public void LosNombresSeDelimitanSoloCuandoHaceFalta(string nombre, string esperado)
        {
            Assert.Equal(esperado, ProveedorMySqlBase.Identificador(nombre));
        }

        [Theory]
        [InlineData("varchar(100)", "''")]
        [InlineData("enum('nuevo','enviado')", "'nuevo'")]
        [InlineData("date", "'2026-01-01'")]
        [InlineData("datetime", "NOW()")]
        [InlineData("int unsigned", "0")]
        [InlineData("decimal(10, 2)", "0")]
        public void ElValorDeEjemploSigueElTipo(string tipo, string esperado)
        {
            Assert.Equal(esperado, ProveedorMySqlBase.ValorEjemplo(new ColumnaModelo { TipoDatos = tipo }));
        }

        [Fact]
        public void ElScriptDeUnProcedimientoLlevaDelimiterYNoElDefinidor()
        {
            var script = ProveedorMySqlBase.ArmarScriptObjeto(TipoObjeto.Procedimiento,
                "CREATE DEFINER=`tienda`@`%` PROCEDURE `p`()\nBEGIN\n  SELECT 1;\nEND");

            Assert.DoesNotContain("DEFINER", script);
            Assert.StartsWith("DELIMITER $$", script);
            Assert.Contains("END$$", script);
            Assert.EndsWith($"DELIMITER ;{Environment.NewLine}", script);
            Assert.Equal(["CREATE PROCEDURE `p`()\nBEGIN\n  SELECT 1;\nEND"], DivisorMySql.Dividir(script).Select(s => s.Texto.Replace("\r\n", "\n")));
        }

        [Fact]
        public void LaVistaNoNecesitaDelimiter()
        {
            var script = ProveedorMySqlBase.ArmarScriptObjeto(TipoObjeto.Vista, "CREATE ALGORITHM=UNDEFINED DEFINER=`tienda`@`%` SQL SECURITY DEFINER VIEW `v` AS select 1");

            Assert.Equal($"CREATE ALGORITHM=UNDEFINED SQL SECURITY DEFINER VIEW `v` AS select 1;{Environment.NewLine}", script);
        }

        [Fact]
        public void LlamarUnProcedimientoConSalidaLeeSusVariables()
        {
            var detalle = new DetalleObjetoModelo
            {
                Objeto = new ObjetoBaseDatosModelo { Tipo = TipoObjeto.Procedimiento, Nombre = "contar" },
                Parametros =
                [
                    new() { Nombre = "p_id", TipoDatos = "int", Direccion = DireccionParametro.Entrada },
                    new() { Nombre = "p_texto", TipoDatos = "varchar(20)", Direccion = DireccionParametro.Entrada },
                    new() { Nombre = "p_total", TipoDatos = "int", Direccion = DireccionParametro.Salida }
                ]
            };

            var script = new ProveedorMySql().ScriptLlamada(detalle, new Dictionary<string, string?> { ["p_id"] = "3", ["p_texto"] = "d'Ana" });

            Assert.Equal($"CALL contar(3, 'd''Ana', @p_total);{Environment.NewLine}SELECT @p_total AS p_total;", script);
        }

        [Fact]
        public void LlamarUnaFuncionEsUnSelect()
        {
            var detalle = new DetalleObjetoModelo
            {
                Objeto = new ObjetoBaseDatosModelo { Tipo = TipoObjeto.Funcion, Nombre = "total_pedido" },
                Parametros = [new() { Nombre = "p_id", TipoDatos = "int" }]
            };

            Assert.Equal("SELECT total_pedido(1) AS resultado;", new ProveedorMariaDb().ScriptLlamada(detalle, new Dictionary<string, string?> { ["p_id"] = "1" }));
        }

        [Fact]
        public void LasDependenciasSalenDelTextoDeCadaObjeto()
        {
            var objetos = new List<ProveedorMySqlBase.ObjetoConTexto>
            {
                new(TipoResultadoBusqueda.Tabla, "clientes", null, ""),
                new(TipoResultadoBusqueda.Tabla, "clientes_viejos", null, ""),
                new(TipoResultadoBusqueda.Vista, "v_clientes", null, "select `tienda`.`clientes`.`id` from `tienda`.`clientes`"),
                new(TipoResultadoBusqueda.Procedimiento, "p_contar", null, "BEGIN SELECT COUNT(*) FROM clientes; END"),
                new(TipoResultadoBusqueda.Disparador, "trg_clientes", "clientes", "BEGIN SET NEW.id = 1; END")
            };

            var tabla = ProveedorMySqlBase.CalcularDependencias(new ResultadoBusquedaObjetoModelo { Tipo = TipoResultadoBusqueda.Tabla, Nombre = "clientes" }, objetos);
            var vista = ProveedorMySqlBase.CalcularDependencias(new ResultadoBusquedaObjetoModelo { Tipo = TipoResultadoBusqueda.Vista, Nombre = "v_clientes" }, objetos);
            var disparador = ProveedorMySqlBase.CalcularDependencias(new ResultadoBusquedaObjetoModelo { Tipo = TipoResultadoBusqueda.Disparador, Nombre = "trg_clientes" }, objetos);

            Assert.Equal(["v_clientes", "p_contar", "trg_clientes"], tabla.UsadoPor.Select(d => d.Objeto!.Nombre));
            Assert.Equal([false, true, false], tabla.UsadoPor.Select(d => d.Posible));
            Assert.Equal(["clientes"], vista.Usa.Select(d => d.Objeto!.Nombre));
            Assert.False(vista.Usa[0].Posible);
            Assert.Equal(["clientes"], disparador.Usa.Select(d => d.Objeto!.Nombre));
        }

        [Fact]
        public void SinBaseInicialSeConectaAlServidor()
        {
            var conexion = new ConexionModelo { Motor = TipoMotor.MariaDb, Servidor = "localhost", Usuario = "tienda" };
            var cadena = new MySqlConnectionStringBuilder(new ProveedorMariaDb().CadenaConexion(conexion, null));

            Assert.Equal(3306u, cadena.Port);
            Assert.Equal("", cadena.Database);
            Assert.True(TipoMotor.MariaDb.AdmiteBaseInicial());
            Assert.Null(TipoMotor.MariaDb.BaseInicialPredeterminada());
            Assert.Contains("eliges la base en el explorador", TipoMotor.MariaDb.AyudaBaseInicial());
            Assert.Contains("Si la dejas vacía se usa postgres.", TipoMotor.PostgreSql.AyudaBaseInicial());
            Assert.Contains("base predeterminada del usuario", TipoMotor.SqlServer.AyudaBaseInicial());
        }
    }
}
