using MiGestorBDAccesible.Core.Autocompletado;
using MiGestorBDAccesible.Core.Ayuda;
using MiGestorBDAccesible.Core.Conexiones;

namespace MiGestorBDAccesible.Pruebas
{
    public class AutocompletadoPruebas
    {
        private readonly AutocompletadoService _servicio = new(new AyudaService());

        [Fact]
        public void SugierePalabrasQueEmpiezanConLoEscrito()
        {
            var resultado = _servicio.Sugerir(TipoMotor.MySql, "SELECT * FROM clientes ORD", 26);

            Assert.Equal(23, resultado.Inicio);
            Assert.Equal("ORD", resultado.Prefijo);
            Assert.Contains(resultado.Sugerencias, s => s.Texto == "ORDER BY" && s.Tipo == "palabra de SQL" && s.Ayuda == "palabra:order-by");
            Assert.All(resultado.Sugerencias, s => Assert.StartsWith("ORD", s.Texto));
        }

        [Fact]
        public void NoDistingueMayusculasAlFiltrar()
        {
            var resultado = _servicio.Sugerir(TipoMotor.PostgreSql, "sel", 3);

            Assert.Contains(resultado.Sugerencias, s => s.Texto == "SELECT");
        }

        [Fact]
        public void CadaMotorTieneSusPalabras()
        {
            Assert.Contains(_servicio.Sugerir(TipoMotor.MySql, "LIM", 3).Sugerencias, s => s.Texto == "LIMIT");
            Assert.Contains(_servicio.Sugerir(TipoMotor.SqlServer, "TO", 2).Sugerencias, s => s.Texto == "TOP");
        }

        [Fact]
        public void DistingueLasFunciones()
        {
            var resultado = _servicio.Sugerir(TipoMotor.MySql, "CONC", 4);

            Assert.Contains(resultado.Sugerencias, s => s.Texto == "CONCAT" && s.Tipo == "función");
        }

        [Fact]
        public void SinPrefijoOfreceTodasHastaElMaximo()
        {
            var resultado = _servicio.Sugerir(TipoMotor.Oracle, "SELECT ", 7);

            Assert.Equal("", resultado.Prefijo);
            Assert.InRange(resultado.Sugerencias.Count, 1, AutocompletadoService.Maximo);
        }

        public static TheoryData<TipoMotor> Motores => [TipoMotor.SqlServer, TipoMotor.Oracle, TipoMotor.PostgreSql, TipoMotor.MySql, TipoMotor.MariaDb];

        [Theory]
        [MemberData(nameof(Motores))]
        public void CadaMotorTraeSuVocabularioSinRepetidos(TipoMotor motor)
        {
            var vocabulario = AutocompletadoService.Vocabulario(motor);

            Assert.True(vocabulario.Palabras.Count > 50);
            Assert.True(vocabulario.Funciones.Count > 30);
            Assert.True(vocabulario.Tipos.Count > 5);
            Assert.Equal(vocabulario.Palabras.Count, vocabulario.Palabras.Distinct().Count());
            Assert.Equal(vocabulario.Funciones.Count, vocabulario.Funciones.Distinct().Count());
            Assert.Equal(vocabulario.Tipos.Count, vocabulario.Tipos.Distinct().Count());
            Assert.All(vocabulario.Palabras.Concat(vocabulario.Funciones).Concat(vocabulario.Tipos), p => Assert.Equal(p.ToUpperInvariant(), p));
        }

        [Fact]
        public void NoSugierePalabrasDeOtrosMotores()
        {
            Assert.DoesNotContain(_servicio.Sugerir(TipoMotor.SqlServer, "LIM", 3).Sugerencias, s => s.Texto == "LIMIT");
            Assert.DoesNotContain(_servicio.Sugerir(TipoMotor.Oracle, "TO", 2).Sugerencias, s => s.Texto == "TOP");
            Assert.DoesNotContain(_servicio.Sugerir(TipoMotor.PostgreSql, "TO", 2).Sugerencias, s => s.Texto == "TOP");
            Assert.DoesNotContain(_servicio.Sugerir(TipoMotor.MySql, "TO", 2).Sugerencias, s => s.Texto == "TOP");
            Assert.DoesNotContain(_servicio.Sugerir(TipoMotor.MySql, "FULL", 4).Sugerencias, s => s.Texto == "FULL JOIN");
            Assert.DoesNotContain(_servicio.Sugerir(TipoMotor.MySql, "RET", 3).Sugerencias, s => s.Texto == "RETURNING");
            Assert.Contains(_servicio.Sugerir(TipoMotor.MariaDb, "RET", 3).Sugerencias, s => s.Texto == "RETURNING");
            Assert.Contains(_servicio.Sugerir(TipoMotor.Oracle, "MIN", 3).Sugerencias, s => s.Texto == "MINUS");
        }

        [Theory]
        [InlineData(TipoMotor.MySql, "PAG")]
        [InlineData(TipoMotor.Oracle, "PROCEDI")]
        [InlineData(TipoMotor.PostgreSql, "CONVERS")]
        [InlineData(TipoMotor.Oracle, "BLOQUE")]
        [InlineData(TipoMotor.SqlServer, "ENTER")]
        public void NoSugiereTerminosDeBusquedaDeLaAyuda(TipoMotor motor, string prefijo)
        {
            Assert.Empty(_servicio.Sugerir(motor, prefijo, prefijo.Length).Sugerencias);
        }

        [Fact]
        public void SugierePalabrasDeVariasPartesYTiposDeDato()
        {
            Assert.Contains(_servicio.Sugerir(TipoMotor.SqlServer, "beg", 3).Sugerencias, s => s.Texto == "BEGIN TRY");
            Assert.Contains(_servicio.Sugerir(TipoMotor.SqlServer, "gro", 3).Sugerencias, s => s.Texto == "GROUP BY");

            var tipo = Assert.Single(_servicio.Sugerir(TipoMotor.SqlServer, "NVARC", 5).Sugerencias);
            Assert.Equal(AutocompletadoService.TipoDato, tipo.Tipo);
            Assert.Equal("tipo:nvarchar", tipo.Ayuda);
        }

        [Fact]
        public void LaAyudaApuntaASuFichaOBuscaLaPalabra()
        {
            Assert.Contains(_servicio.Sugerir(TipoMotor.SqlServer, "SEL", 3).Sugerencias, s => s.Texto == "SELECT" && s.Ayuda == "palabra:select");
            Assert.Contains(_servicio.Sugerir(TipoMotor.SqlServer, "BIGI", 4).Sugerencias, s => s.Texto == "BIGINT" && s.Ayuda == "tipo:enteros");
            Assert.Contains(_servicio.Sugerir(TipoMotor.SqlServer, "GETD", 4).Sugerencias, s => s.Texto == "GETDATE" && s.Ayuda == "palabra:fechas");
        }

        [Theory]
        [InlineData(TipoMotor.SqlServer, "EXEC |", "concepto:procedimientos")]
        [InlineData(TipoMotor.Oracle, "CALL |", "plsql:procedimientos")]
        [InlineData(TipoMotor.PostgreSql, "CALL |", "plpgsql:procedimientos")]
        [InlineData(TipoMotor.MySql, "CALL |", "concepto:procedimientos")]
        [InlineData(TipoMotor.SqlServer, "SELECT Tot|", "concepto:funciones")]
        [InlineData(TipoMotor.Oracle, "SELECT Tot|", "plsql:procedimientos")]
        [InlineData(TipoMotor.PostgreSql, "SELECT Tot|", "plpgsql:funciones")]
        [InlineData(TipoMotor.MariaDb, "SELECT Tot|", "concepto:funciones")]
        public void LaAyudaDeUnProcedimientoOUnaFuncionExisteEnCadaMotor(TipoMotor motor, string texto, string esperada)
        {
            var cursor = texto.IndexOf('|');
            var sugerencia = _servicio.Sugerir(motor, Catalogo, texto.Remove(cursor, 1), cursor).Sugerencias
                .First(s => s.Texto is "ContarPedidos" or "TotalPedido");

            var ficha = Assert.Single(new AyudaService().ResolverVarias(motor, sugerencia.Ayuda.Split('|')));
            Assert.Equal(esperada, ficha.Id);
        }

        [Fact]
        public void SoloLasPalabrasDelMotorAjustanMayusculas()
        {
            Assert.All(_servicio.Sugerir(TipoMotor.SqlServer, "se", 2).Sugerencias, s => Assert.True(s.AjustarMayusculas));
            Assert.All(En("SELECT * FROM |").Sugerencias, s => Assert.False(s.AjustarMayusculas));
        }

        private static readonly CatalogoModelo Catalogo = new()
        {
            EsquemaPredeterminado = "dbo",
            Objetos =
            [
                new(TipoObjetoCatalogo.Tabla, "dbo", "Clientes", [new("Id", "int"), new("Nombre", "nvarchar(100)"), new("Correo", "nvarchar(150)")]),
                new(TipoObjetoCatalogo.Tabla, "dbo", "Pedidos", [new("Id", "int"), new("ClienteId", "int"), new("Fecha", "date")]),
                new(TipoObjetoCatalogo.Vista, "dbo", "VentasPorCliente", [new("Cliente", "nvarchar(100)")]),
                new(TipoObjetoCatalogo.Tabla, "ventas", "Facturas", [new("Numero", "int")]),
                new(TipoObjetoCatalogo.Procedimiento, "dbo", "ContarPedidos", []),
                new(TipoObjetoCatalogo.Funcion, "dbo", "TotalPedido", [])
            ]
        };

        private ResultadoSugerenciasModelo En(string texto, string marca = "|")
        {
            var cursor = texto.IndexOf(marca, StringComparison.Ordinal);
            return _servicio.Sugerir(TipoMotor.SqlServer, Catalogo, texto.Remove(cursor, marca.Length), cursor);
        }

        [Fact]
        public void DespuesDeFromSugiereTablasYVistas()
        {
            var resultado = En("SELECT * FROM |");

            Assert.Equal(["Clientes", "ventas.Facturas", "Pedidos", "VentasPorCliente"], resultado.Sugerencias.Select(s => s.Texto));
            Assert.Equal("tabla", resultado.Sugerencias[0].Tipo);
            Assert.Equal("concepto:tabla", resultado.Sugerencias[0].Ayuda);
            Assert.Equal("vista", resultado.Sugerencias[^1].Tipo);
        }

        [Fact]
        public void FiltraLasTablasPorLoEscritoAunqueTenganEsquema()
        {
            Assert.Equal(["Pedidos"], En("SELECT * FROM clientes c JOIN Pe|").Sugerencias.Select(s => s.Texto));
            Assert.Equal(["ventas.Facturas"], En("SELECT * FROM Fac|").Sugerencias.Select(s => s.Texto));
        }

        [Fact]
        public void DespuesDeUnAliasYUnPuntoSugiereSusColumnas()
        {
            var resultado = En("SELECT c.| FROM Clientes c JOIN Pedidos AS p ON p.ClienteId = c.Id");

            Assert.Equal(["Id", "Nombre", "Correo"], resultado.Sugerencias.Select(s => s.Texto));
            Assert.Equal("columna de Clientes, nvarchar(150)", resultado.Sugerencias[2].Tipo);
            Assert.Equal("tipo:nvarchar(150)", resultado.Sugerencias[2].Ayuda);
            Assert.Equal(["ClienteId"], En("SELECT * FROM Clientes c JOIN Pedidos AS p ON p.Cl|").Sugerencias.Select(s => s.Texto));
        }

        [Fact]
        public void DespuesDeUnNombreDeTablaYUnPuntoSugiereSusColumnas()
        {
            Assert.Equal(["Fecha"], En("SELECT pedidos.F|").Sugerencias.Select(s => s.Texto));
        }

        [Fact]
        public void DespuesDeUnEsquemaYUnPuntoSugiereSusObjetos()
        {
            Assert.Equal(["Facturas"], En("SELECT * FROM ventas.|").Sugerencias.Select(s => s.Texto));
        }

        [Fact]
        public void EnElSelectSugiereColumnasDelFromLuegoFuncionesYPalabras()
        {
            var resultado = En("SELECT | FROM Pedidos");

            Assert.Equal(["Id", "ClienteId", "Fecha"], resultado.Sugerencias.Take(3).Select(s => s.Texto));
            Assert.Equal("TotalPedido", resultado.Sugerencias[3].Texto);
            Assert.Equal("función de la base", resultado.Sugerencias[3].Tipo);
            Assert.Contains(resultado.Sugerencias, s => s.Tipo == "palabra de SQL");
        }

        [Fact]
        public void DespuesDeExecSugiereProcedimientos()
        {
            Assert.Equal(["ContarPedidos"], En("EXEC |").Sugerencias.Select(s => s.Texto));
        }

        [Fact]
        public void SoloMiraLaSentenciaDelCursor()
        {
            var resultado = En("SELECT * FROM Clientes c;\nSELECT c.| FROM Pedidos p;");

            Assert.Empty(resultado.Sugerencias);
        }

        [Fact]
        public void NoSugiereDentroDeUnTextoNiDeUnComentario()
        {
            Assert.Empty(En("SELECT 'FROM Cl|").Sugerencias);
            Assert.Empty(En("SELECT 1 -- FROM Cl|").Sugerencias);
        }

        [Fact]
        public void LasPalabrasNoSeConfundenConAlias()
        {
            Assert.Equal(["Id", "ClienteId", "Fecha"], En("SELECT * FROM Pedidos WHERE |").Sugerencias.Take(3).Select(s => s.Texto));
        }

        [Theory]
        [InlineData("CREATE TABLE t (id int)", true)]
        [InlineData("alter table t add x int", true)]
        [InlineData("DROP VIEW v", true)]
        [InlineData("SELECT created FROM t", false)]
        [InlineData("UPDATE t SET dropped = 1", false)]
        public void DetectaLosCambiosDeEstructura(string script, bool esperado)
        {
            Assert.Equal(esperado, AutocompletadoService.CambiaEstructura(script));
        }

        [Fact]
        public void ElCursorFueraDelTextoNoFalla()
        {
            Assert.Equal("", _servicio.Sugerir(TipoMotor.MariaDb, "", 10).Prefijo);
        }
    }
}
