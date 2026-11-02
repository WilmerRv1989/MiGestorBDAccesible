using MiGestorBDAccesible.Core.Ejecucion;

namespace MiGestorBDAccesible.Pruebas
{
    public class DivisorOraclePruebas
    {
        [Fact]
        public void SeparaSentenciasPorPuntoYComaYQuitaElPuntoYComa()
        {
            var sentencias = DivisorOracle.Dividir("SELECT 1 FROM dual;\nSELECT 2 FROM dual;");

            Assert.Collection(sentencias,
                s => Assert.Equal(new SentenciaOracle("SELECT 1 FROM dual", 1, false), s),
                s => Assert.Equal(new SentenciaOracle("SELECT 2 FROM dual", 2, false), s));
        }

        [Fact]
        public void GuardaLaLineaRealDondeEmpiezaCadaSentencia()
        {
            var sentencias = DivisorOracle.Dividir("\n\n-- clientes\nSELECT *\nFROM clientes;\n\n/* nota\n   larga */\nDELETE FROM pedidos\nWHERE id = 3;");

            Assert.Equal([4, 9], sentencias.Select(s => s.LineaInicial));
            Assert.Equal("SELECT *\nFROM clientes", sentencias[0].Texto);
            Assert.Equal("DELETE FROM pedidos\nWHERE id = 3", sentencias[1].Texto);
        }

        [Fact]
        public void NoCortaDentroDeTextosNiComentariosNiNombresEntreComillas()
        {
            var script = "INSERT INTO t VALUES ('uno; dos', 'it''s; ok');\nSELECT \"col;rara\" FROM t -- comentario; con punto y coma\n;\nSELECT q'[a;b'c]' FROM dual;\nSELECT 1 /* ; */ FROM dual;";
            var sentencias = DivisorOracle.Dividir(script);

            Assert.Equal(4, sentencias.Count);
            Assert.Equal("INSERT INTO t VALUES ('uno; dos', 'it''s; ok')", sentencias[0].Texto);
            Assert.StartsWith("SELECT \"col;rara\" FROM t -- comentario; con punto y coma", sentencias[1].Texto);
            Assert.Equal("SELECT q'[a;b'c]' FROM dual", sentencias[2].Texto);
            Assert.Equal("SELECT 1 /* ; */ FROM dual", sentencias[3].Texto);
        }

        [Fact]
        public void LosBloquesPlSqlTerminanEnLaBarraYConservanSusPuntosYComa()
        {
            var script = "BEGIN\n  DBMS_OUTPUT.PUT_LINE('Hola');\n  NULL;\nEND;\n/\nSELECT 1 FROM dual;";
            var sentencias = DivisorOracle.Dividir(script);

            Assert.Equal(2, sentencias.Count);
            Assert.Equal(new SentenciaOracle("BEGIN\n  DBMS_OUTPUT.PUT_LINE('Hola');\n  NULL;\nEND;", 1, true), sentencias[0]);
            Assert.Equal(new SentenciaOracle("SELECT 1 FROM dual", 6, false), sentencias[1]);
        }

        [Theory]
        [InlineData("DECLARE\n  n NUMBER;\nBEGIN\n  n := 1;\nEND;")]
        [InlineData("CREATE OR REPLACE PROCEDURE p AS\nBEGIN\n  NULL;\nEND;")]
        [InlineData("create function f return number is\nbegin\n  return 1;\nend;")]
        [InlineData("CREATE OR REPLACE PACKAGE BODY pk AS\n  PROCEDURE a IS BEGIN NULL; END;\nEND;")]
        [InlineData("CREATE OR REPLACE EDITIONABLE TRIGGER tr BEFORE INSERT ON t\nFOR EACH ROW\nBEGIN\n  :NEW.id := 1;\nEND;")]
        [InlineData("CREATE TYPE punto AS OBJECT (x NUMBER, y NUMBER);")]
        public void ReconoceLosBloquesPlSql(string bloque)
        {
            var sentencias = DivisorOracle.Dividir($"{bloque}\n/\nSELECT 1 FROM dual;");

            Assert.Equal(2, sentencias.Count);
            Assert.True(sentencias[0].EsPlSql);
            Assert.Equal(bloque, sentencias[0].Texto);
            Assert.False(sentencias[1].EsPlSql);
        }

        [Fact]
        public void LaBarraSolaTambienCierraUnaSentenciaSql()
        {
            var sentencias = DivisorOracle.Dividir("SELECT 1 FROM dual\n/\nSELECT 10 / 2 FROM dual;");

            Assert.Equal(["SELECT 1 FROM dual", "SELECT 10 / 2 FROM dual"], sentencias.Select(s => s.Texto));
        }

        [Fact]
        public void CreateTableNoEsPlSql()
        {
            var sentencias = DivisorOracle.Dividir("CREATE TABLE t (id NUMBER);\nCREATE INDEX ix ON t (id);");

            Assert.Equal(2, sentencias.Count);
            Assert.All(sentencias, s => Assert.False(s.EsPlSql));
        }

        [Fact]
        public void LaUltimaSentenciaSinPuntoYComaTambienCuentaYLosComentariosSueltosNo()
        {
            var sentencias = DivisorOracle.Dividir("SELECT 1 FROM dual;\nSELECT 2 FROM dual\n-- fin");

            Assert.Equal(2, sentencias.Count);
            Assert.Equal("SELECT 2 FROM dual\n-- fin", sentencias[1].Texto);
            Assert.Empty(DivisorOracle.Dividir("-- solo un comentario\n/* y otro */\n"));
        }

        [Fact]
        public void LosNombresQueEmpiezanConQoNNoSeConfundenConTextos()
        {
            var sentencias = DivisorOracle.Dividir("SELECT nombre, q FROM t WHERE nombre = N'Ana;';\nSELECT 1 FROM dual;");

            Assert.Equal(2, sentencias.Count);
            Assert.Equal("SELECT nombre, q FROM t WHERE nombre = N'Ana;'", sentencias[0].Texto);
        }

        [Fact]
        public void AdmiteSaltosDeLineaDeWindows()
        {
            var sentencias = DivisorOracle.Dividir("SELECT 1 FROM dual;\r\nBEGIN\r\n  NULL;\r\nEND;\r\n/\r\n");

            Assert.Equal(2, sentencias.Count);
            Assert.Equal(new SentenciaOracle("BEGIN\n  NULL;\nEND;", 2, true), sentencias[1]);
        }
    }
}
