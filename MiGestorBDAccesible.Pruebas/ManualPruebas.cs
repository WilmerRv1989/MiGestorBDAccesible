using MiGestorBDAccesible.Core.Ayuda;

namespace MiGestorBDAccesible.Pruebas
{
    public class ManualPruebas
    {
        [Fact]
        public void ElManualVieneIncluidoYTieneSusSecciones()
        {
            var manual = ManualService.Manual();

            Assert.Equal("Manual de usuario de Mi Gestor BD Accesible", manual.Titulo);
            Assert.Contains(manual.Secciones, s => s.Titulo == "Conexiones");
            Assert.Contains(manual.Secciones, s => s.Titulo == "Atajos de teclado");
            Assert.Contains("<table>", manual.Html);
            Assert.Contains("<q lang=\"la\">Nosce te ipsum</q>", manual.Html);
            Assert.DoesNotContain("<h1", manual.Html);
        }

        [Fact]
        public void BajaUnNivelLosEncabezadosYLesPoneId()
        {
            var manual = ManualService.Convertir("# Título\n\nIntro.\n\n## Primera `parte`\n\n### Detalle\n\n## Segunda", 1);

            Assert.Equal("Título", manual.Titulo);
            Assert.Equal([new SeccionManualModelo("manual-1", "Primera parte"), new SeccionManualModelo("manual-2", "Segunda")], manual.Secciones);
            Assert.Contains("<h3 id=\"manual-1\">Primera <code>parte</code></h3>", manual.Html);
            Assert.Contains("<h4>Detalle</h4>", manual.Html);
        }
    }
}
