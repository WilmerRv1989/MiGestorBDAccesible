using MiGestorBDAccesible.Core;

namespace MiGestorBDAccesible.Pruebas
{
    public class InformacionAplicacionPruebas
    {
        [Fact]
        public void LaVersionVieneDeLosDatosDelProyecto()
        {
            Assert.Equal("1.0.0", InformacionAplicacion.VersionCompleta(typeof(InformacionAplicacion).Assembly));
        }

        [Fact]
        public void VersionCortaSinCerosDeMas()
        {
            Assert.Equal("1.0", InformacionAplicacion.VersionCorta("1.0.0"));
            Assert.Equal("1.2.3", InformacionAplicacion.VersionCorta("1.2.3"));
            Assert.Equal("2.1", InformacionAplicacion.VersionCorta("2.1.0-beta"));
        }
    }
}
