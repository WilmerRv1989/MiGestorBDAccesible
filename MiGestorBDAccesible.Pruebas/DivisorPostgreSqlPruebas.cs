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
    }
}
