using MiGestorBDAccesible.Core.Ejecucion;

namespace MiGestorBDAccesible.Pruebas
{
    public class DivisorMySqlPruebas
    {
        [Fact]
        public void SeparaPorPuntoYComaYGuardaLaLinea()
        {
            var sentencias = DivisorMySql.Dividir("SELECT 1;\n\nSELECT 2;\nSELECT 3");

            Assert.Equal(["SELECT 1", "SELECT 2", "SELECT 3"], sentencias.Select(s => s.Texto));
            Assert.Equal([1, 3, 4], sentencias.Select(s => s.LineaInicial));
        }

        [Fact]
        public void DelimiterPermiteCuerposConPuntoYComa()
        {
            var script = """
                DROP PROCEDURE IF EXISTS p;
                DELIMITER $$
                CREATE PROCEDURE p ()
                BEGIN
                    SELECT 1;
                    SELECT 2;
                END$$
                DELIMITER ;
                CALL p();
                """;

            var sentencias = DivisorMySql.Dividir(script);

            Assert.Equal(3, sentencias.Count);
            Assert.Equal("DROP PROCEDURE IF EXISTS p", sentencias[0].Texto);
            Assert.StartsWith("CREATE PROCEDURE p ()", sentencias[1].Texto);
            Assert.EndsWith("END", sentencias[1].Texto);
            Assert.Contains("SELECT 2;", sentencias[1].Texto);
            Assert.Equal(3, sentencias[1].LineaInicial);
            Assert.Equal("CALL p()", sentencias[2].Texto);
            Assert.Equal(9, sentencias[2].LineaInicial);
        }

        [Fact]
        public void DelimiterNoSeEnviaAlServidorYAceptaOtrosSeparadores()
        {
            var sentencias = DivisorMySql.Dividir("delimiter //\nSELECT 1; SELECT 2//\ndelimiter ;\nSELECT 3;");

            Assert.Equal(["SELECT 1; SELECT 2", "SELECT 3"], sentencias.Select(s => s.Texto));
        }

        [Fact]
        public void RespetaTextosNombresYComentarios()
        {
            var script = """
                SELECT 'a;b', "c;d", `e;f`, 'it\'s;', 'dos '';' -- comentario; con punto y coma
                FROM t # otro; comentario
                /* bloque; */ WHERE x = 1;
                SELECT 2;
                """;

            var sentencias = DivisorMySql.Dividir(script);

            Assert.Equal(2, sentencias.Count);
            Assert.Contains("'it\\'s;'", sentencias[0].Texto);
            Assert.EndsWith("WHERE x = 1", sentencias[0].Texto);
        }

        [Fact]
        public void DosGuionesSinEspacioNoSonComentario()
        {
            var sentencias = DivisorMySql.Dividir("SELECT 5--1;\nSELECT 2;");

            Assert.Equal(["SELECT 5--1", "SELECT 2"], sentencias.Select(s => s.Texto));
        }

        [Fact]
        public void LosComentariosSueltosNoSonSentencias()
        {
            var sentencias = DivisorMySql.Dividir("-- solo un comentario\n# y otro\n/* y uno más */\n");

            Assert.Empty(sentencias);
        }

        [Fact]
        public void LosComentariosEjecutablesSeEnvian()
        {
            var sentencias = DivisorMySql.Dividir("/*!40101 SET NAMES utf8mb4 */;\nSELECT 1;");

            Assert.Equal(["/*!40101 SET NAMES utf8mb4 */", "SELECT 1"], sentencias.Select(s => s.Texto));
        }
    }
}
