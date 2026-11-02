using MiGestorBDAccesible.Core.Ejecucion;

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
    }
}
