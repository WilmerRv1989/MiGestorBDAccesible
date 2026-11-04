using MiGestorBDAccesible.Core.Ejecucion;

namespace MiGestorBDAccesible.Pruebas
{
    public class DivisorPostgreSqlPruebas
    {
        [Fact]
        public void SeparaPorPuntoYComaYGuardaLaLineaReal()
        {
            var sentencias = DivisorPostgreSql.Dividir("\n-- clientes\nSELECT *\nFROM clientes;\n\nDELETE FROM pedidos\nWHERE id = 3;");

            Assert.Collection(sentencias,
                s => Assert.Equal(new SentenciaPostgreSql("SELECT *\nFROM clientes", 3), s),
                s => Assert.Equal(new SentenciaPostgreSql("DELETE FROM pedidos\nWHERE id = 3", 6), s));
        }

        [Fact]
        public void LosCuerposEntreSignosDeDolarNoSeCortan()
        {
            var script = "CREATE FUNCTION f() RETURNS int AS $$\nBEGIN\n  RETURN 1;\nEND;\n$$ LANGUAGE plpgsql;\nSELECT f();";
            var sentencias = DivisorPostgreSql.Dividir(script);

            Assert.Equal(2, sentencias.Count);
            Assert.Equal("CREATE FUNCTION f() RETURNS int AS $$\nBEGIN\n  RETURN 1;\nEND;\n$$ LANGUAGE plpgsql", sentencias[0].Texto);
            Assert.Equal(6, sentencias[1].LineaInicial);
        }

        [Fact]
        public void AdmiteEtiquetasDeDolarYBloquesDo()
        {
            var script = "DO $cuerpo$\nBEGIN\n  RAISE NOTICE 'hola; mundo $$';\nEND\n$cuerpo$;\nSELECT 1;";
            var sentencias = DivisorPostgreSql.Dividir(script);

            Assert.Equal(2, sentencias.Count);
            Assert.EndsWith("$cuerpo$", sentencias[0].Texto);
            Assert.Equal("SELECT 1", sentencias[1].Texto);
        }

        [Fact]
        public void LosParametrosConDolarNoSonEtiquetas()
        {
            var sentencias = DivisorPostgreSql.Dividir("PREPARE p AS SELECT $1 + $2;\nEXECUTE p(1, 2);");

            Assert.Equal(["PREPARE p AS SELECT $1 + $2", "EXECUTE p(1, 2)"], sentencias.Select(s => s.Texto));
        }

        [Fact]
        public void RespetaTextosNombresYComentariosAnidados()
        {
            var script = "SELECT 'uno; dos', 'it''s', \"col;rara\" FROM t /* a /* b; */ c; */ WHERE x = E'a\\';b';\nSELECT 2;";
            var sentencias = DivisorPostgreSql.Dividir(script);

            Assert.Equal(2, sentencias.Count);
            Assert.EndsWith("WHERE x = E'a\\';b'", sentencias[0].Texto);
        }

        [Fact]
        public void LosComandosDePsqlVanSolosEnSuLinea()
        {
            var sentencias = DivisorPostgreSql.Dividir("\\d clientes\nSELECT 1;");

            Assert.Equal(2, sentencias.Count);
            Assert.True(DivisorPostgreSql.EsComandoPsql(sentencias[0].Texto));
            Assert.Equal(new SentenciaPostgreSql("SELECT 1", 2), sentencias[1]);
        }

        [Fact]
        public void LosComentariosSueltosNoSonSentencias()
        {
            Assert.Empty(DivisorPostgreSql.Dividir("-- nada\n/* tampoco\n */\n"));
            Assert.Equal(3, DivisorPostgreSql.Dividir("/* uno\ndos */\nSELECT 1")[0].LineaInicial);
        }

        [Fact]
        public void AdmiteSaltosDeLineaDeWindows()
        {
            var sentencias = DivisorPostgreSql.Dividir("SELECT 1;\r\nSELECT 2;\r\n");

            Assert.Equal([1, 2], sentencias.Select(s => s.LineaInicial));
        }

        [Fact]
        public void AC4_ConservaElCuerpoDeUnaFuncionConEtiquetaNoAscii()
        {
            var parametros = DivisorPostgreSql.Dividir("SELECT $1a$; SELECT 2;");
            Assert.Equal(["SELECT $1a$", "SELECT 2"], parametros.Select(s => s.Texto));

            var mayusculas = Assert.Single(DivisorPostgreSql.Dividir("SELECT $A$a;$a$;b$A$;"));
            Assert.Equal("SELECT $A$a;$a$;b$A$", mayusculas.Texto);

            var funcion = "CREATE FUNCTION f() RETURNS int AS $función$\nBEGIN\n  UPDATE cuentas SET saldo = 0;\n  RETURN 1;\nEND;\n$función$ LANGUAGE plpgsql";
            var sentencias = DivisorPostgreSql.Dividir(funcion + ";\nSELECT f();");

            Assert.Equal(2, sentencias.Count);
            Assert.Equal(funcion, sentencias[0].Texto);
            Assert.Equal(1, sentencias[0].LineaInicial);
            Assert.Equal(new SentenciaPostgreSql("SELECT f()", 7), sentencias[1]);
        }

        [Theory]
        [InlineData("é")]
        [InlineData("€")]
        [InlineData("–")]
        [InlineData("😀")]
        [InlineData("日本")]
        [InlineData("\u0080")]
        [InlineData("\u00a0")]
        [InlineData("\u0301")]
        [InlineData("a1é")]
        [InlineData("_é1")]
        public void AC4_AdmiteCaracteresNoAsciiEnLasEtiquetas(string etiqueta)
        {
            var texto = $"SELECT ${etiqueta}$a;b${etiqueta}$";

            var sentencia = Assert.Single(DivisorPostgreSql.Dividir(texto + ";"));

            Assert.Equal(texto, sentencia.Texto);
            Assert.Equal(1, sentencia.LineaInicial);
        }

        [Theory]
        [InlineData("€")]
        [InlineData("–")]
        [InlineData("😀")]
        [InlineData("\u0080")]
        [InlineData("\u0301")]
        public void AC4_NoAbreUnaCadenaDolarDentroDeUnIdentificadorNoAscii(string caracter)
        {
            var primera = $"select 1 as x{caracter}$t$";
            var sentencias = DivisorPostgreSql.Dividir(primera + "; delete from t; select 2 as y$t$;");

            Assert.Equal([primera, "delete from t", "select 2 as y$t$"], sentencias.Select(s => s.Texto));
        }

        [Theory]
        [InlineData("$tag$")]
        [InlineData("$función$")]
        [InlineData("$€$")]
        [InlineData("$$")]
        public void AC5_RechazaUnaCadenaDolarSinCerrarAunqueHayaSentenciasAnteriores(string etiqueta)
        {
            var script = $"SELECT 1;\n\nDO\n{etiqueta}\nBEGIN\n  UPDATE cuentas SET saldo = 0;\nEND;";

            var error = Assert.Throws<ScriptNoDivisibleException>(() => DivisorPostgreSql.Dividir(script));

            Assert.Equal(4, error.Linea);
            Assert.False(string.IsNullOrWhiteSpace(error.Message));
        }

        [Theory]
        [InlineData("$A$", "$a$")]
        [InlineData("$É$", "$é$")]
        public void AC5_UnaEtiquetaConDistintasMayusculasNoCierraLaCadena(string apertura, string cierre)
        {
            var script = $"SELECT 1;\nSELECT\n{apertura}a;\n{cierre};\nUPDATE cuentas SET saldo = 0;";

            var error = Assert.Throws<ScriptNoDivisibleException>(() => DivisorPostgreSql.Dividir(script));

            Assert.Equal(3, error.Linea);
            Assert.False(string.IsNullOrWhiteSpace(error.Message));
        }
    }
}
