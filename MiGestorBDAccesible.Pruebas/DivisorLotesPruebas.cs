using MiGestorBDAccesible.Core.Ejecucion;
using System.Globalization;

namespace MiGestorBDAccesible.Pruebas
{
    public class DivisorLotesPruebas
    {
        [Fact]
        public void SeparaPorGoYGuardaLaLineaDeInicio()
        {
            var lotes = DivisorLotes.Dividir("SELECT 1;\nGO\n\nSELECT 2;\ngo\nSELECT 3;");

            Assert.Equal(3, lotes.Count);
            Assert.Equal(1, lotes[0].LineaInicial);
            Assert.Equal(3, lotes[1].LineaInicial);
            Assert.Equal(6, lotes[2].LineaInicial);
        }

        [Fact]
        public void EntiendeGoConRepeticionesYComentario()
        {
            var lotes = DivisorLotes.Dividir("INSERT INTO T VALUES (1);\nGO 3 -- tres veces\nSELECT 1;");

            Assert.Equal(2, lotes.Count);
            Assert.Equal(3, lotes[0].Repeticiones);
            Assert.Equal(1, lotes[1].Repeticiones);
        }

        [Fact]
        public void NoCortaPorGoDentroDeUnaLinea()
        {
            var lotes = DivisorLotes.Dividir("SELECT 'GO' AS Texto;\nSELECT 1 AS Gol;");

            Assert.Single(lotes);
        }

        [Fact]
        public void IgnoraLotesVacios()
        {
            var lotes = DivisorLotes.Dividir("GO\n\nGO\nSELECT 1;\nGO\n");

            Assert.Single(lotes);
            Assert.Equal(4, lotes[0].LineaInicial);
        }

        [Theory]
        [InlineData("'linea 1\nGO\nDELETE FROM cuentas --'")]
        [InlineData("N'linea 1\nGO\nDELETE FROM cuentas --'")]
        [InlineData("'linea ''citada''\nGO\nDELETE FROM cuentas --'")]
        [InlineData("N'linea ''citada''\nGO\nDELETE FROM cuentas --'")]
        public void AC1_GoDentroDeUnaCadenaDeVariasLineasNoSeparaLotes(string cadena)
        {
            var script = $"INSERT INTO notas VALUES (1, {cadena});\nSELECT 2;";

            var lote = Assert.Single(DivisorLotes.Dividir(script));

            Assert.Equal(script + "\n", lote.Texto);
            Assert.Contains(cadena, lote.Texto);
            Assert.Contains("DELETE FROM cuentas --", lote.Texto);
            Assert.Contains("SELECT 2;", lote.Texto);

            var lotes = DivisorLotes.Dividir(script + "\nGO\nSELECT 3;");

            Assert.Equal(2, lotes.Count);
            Assert.Equal(script + "\n", lotes[0].Texto);
            Assert.Equal("SELECT 3;\n", lotes[1].Texto);
            Assert.Equal(6, lotes[1].LineaInicial);
        }

        [Theory]
        [InlineData("[nombre]]con escape\nGO\ncompleto]")]
        [InlineData("\"nombre\"\"con escape\nGO\ncompleto\"")]
        public void AC1_GoDentroDeUnIdentificadorDeVariasLineasNoSeparaLotes(string identificador)
        {
            var script = $"SELECT 1 AS {identificador};\nSELECT 2;";

            var lote = Assert.Single(DivisorLotes.Dividir(script));

            Assert.Equal(script + "\n", lote.Texto);
            Assert.Contains(identificador, lote.Texto);

            var lotes = DivisorLotes.Dividir(script + "\nGO\nSELECT 3;");

            Assert.Equal(2, lotes.Count);
            Assert.Equal(script + "\n", lotes[0].Texto);
            Assert.Equal("SELECT 3;\n", lotes[1].Texto);
        }

        [Theory]
        [InlineData("/* comentario\nGO\nfin */")]
        [InlineData("/* exterior /* interior */\nGO\nfin exterior */")]
        public void AC2_GoDentroDeUnComentarioDeBloqueNoSeparaLotes(string comentario)
        {
            var script = $"SELECT 1;\n{comentario}\n-- GO\nSELECT 2;";

            var lote = Assert.Single(DivisorLotes.Dividir(script));

            Assert.Equal(script + "\n", lote.Texto);
            Assert.Contains("-- GO", lote.Texto);

            var lotes = DivisorLotes.Dividir(script + "\nGO\nSELECT 3;");

            Assert.Equal(2, lotes.Count);
            Assert.Equal(script + "\n", lotes[0].Texto);
            Assert.Equal("SELECT 3;\n", lotes[1].Texto);
            Assert.Equal(8, lotes[1].LineaInicial);
        }

        [Theory]
        [InlineData("99999999999")]
        [InlineData("2147483648")]
        public void AC3_RechazaRepeticionesQueDesbordanUnEnteroConErrorDeDivision(string repeticiones)
        {
            var script = $"SELECT 1;\nGO\nSELECT 2;\nGO {repeticiones}";

            var error = Assert.Throws<ScriptNoDivisibleException>(() => DivisorLotes.Dividir(script));

            Assert.Equal(4, error.Linea);
            Assert.False(string.IsNullOrWhiteSpace(error.Message));
            Assert.Contains(DivisorLotes.RepeticionesMaximas.ToString(CultureInfo.InvariantCulture), error.Message);
        }

        [Fact]
        public void AC3_AceptaElMaximoYRechazaUnaRepeticionMas()
        {
            var maximo = DivisorLotes.RepeticionesMaximas.ToString(CultureInfo.InvariantCulture);
            var exceso = (DivisorLotes.RepeticionesMaximas + 1).ToString(CultureInfo.InvariantCulture);
            var lote = Assert.Single(DivisorLotes.Dividir($"SELECT 1;\nGO {maximo}"));

            Assert.Equal(10000, DivisorLotes.RepeticionesMaximas);
            Assert.Equal(DivisorLotes.RepeticionesMaximas, lote.Repeticiones);

            var error = Assert.Throws<ScriptNoDivisibleException>(
                () => DivisorLotes.Dividir($"SELECT 1;\nGO\nSELECT 2;\nGO {exceso}"));

            Assert.Equal(4, error.Linea);
            Assert.False(string.IsNullOrWhiteSpace(error.Message));
            Assert.Contains(maximo, error.Message);
        }

        [Fact]
        public void AC3_GoConUnDigitoNoAsciiNoSeparaLotes()
        {
            var script = "SELECT 1\nGO ٣\nSELECT 2";

            var lote = Assert.Single(DivisorLotes.Dividir(script));

            Assert.Equal(script + "\n", lote.Texto);
            Assert.Equal(1, lote.LineaInicial);
            Assert.Equal(1, lote.Repeticiones);
        }
    }
}
